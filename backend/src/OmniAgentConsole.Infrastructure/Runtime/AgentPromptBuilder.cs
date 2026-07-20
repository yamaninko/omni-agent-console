using System.Text;
using System.Text.Json;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Infrastructure.Runtime;

/// <summary>
/// Builds everything the model request needs from task state: chat messages
/// (system + user), per-role instructions, the mandatory-skills block, request
/// metadata, and task-context parsing. Pure string/JSON work — no I/O.
/// </summary>
internal static class AgentPromptBuilder
{
    public static IReadOnlyList<ChatMessage> BuildMessages(
        TaskRun taskRun,
        AgentDefinition agentDefinition,
        IReadOnlyList<AgentOutput> previousOutputs,
        string? skillsBlock,
        string? objectiveOverride = null,
        string? roleInstructionOverride = null)
    {
        var systemPromptParts = new List<string>
        {
            agentDefinition.SystemPrompt.Trim(),
            roleInstructionOverride ?? GetRoleInstruction(agentDefinition.Type)
        };

        if (!string.IsNullOrWhiteSpace(skillsBlock))
        {
            systemPromptParts.Add(skillsBlock);
        }

        systemPromptParts.Add("Respond in the same language as the user prompt unless the user asks otherwise. Keep output concise and actionable.");

        var systemPrompt = string.Join("\n\n", systemPromptParts);

        var userBuilder = new StringBuilder();
        userBuilder.AppendLine("User task:");
        userBuilder.AppendLine(taskRun.InputPrompt.Trim());

        if (!string.IsNullOrWhiteSpace(taskRun.InputContextJson))
        {
            userBuilder.AppendLine();
            userBuilder.AppendLine("Input context JSON:");
            userBuilder.AppendLine(TrimForPrompt(taskRun.InputContextJson, 6000));
        }

        if (previousOutputs.Count > 0)
        {
            userBuilder.AppendLine();
            userBuilder.AppendLine("Previous agent outputs:");
            foreach (var output in previousOutputs)
            {
                userBuilder.AppendLine($"[{output.Name} / {output.Type}]");
                userBuilder.AppendLine(TrimForPrompt(output.Content, 6000));
                userBuilder.AppendLine();
            }
        }

        userBuilder.AppendLine();
        userBuilder.AppendLine("Current agent objective:");
        userBuilder.AppendLine(objectiveOverride ?? GetObjective(agentDefinition.Type));

        return
        [
            new ChatMessage("system", systemPrompt),
            new ChatMessage("user", userBuilder.ToString())
        ];
    }

    public static Dictionary<string, string> BuildRequestMetadata(
        TaskRun taskRun,
        AgentRun agentRun,
        AgentDefinition agentDefinition)
    {
        // Never put raw API keys in metadata — the provider resolves them via
        // IApiCredentialKeyResolver (Vault / secret store / legacy column).
        return new Dictionary<string, string>
        {
            ["taskRunId"] = taskRun.Id.ToString(),
            ["agentRunId"] = agentRun.Id.ToString(),
            ["agentType"] = agentDefinition.Type.ToString(),
            ["agentDefinitionId"] = agentDefinition.Id.ToString(),
            ["apiCredentialId"] = agentDefinition.ApiCredentialId?.ToString()
                ?? agentDefinition.ApiCredential?.Id.ToString()
                ?? string.Empty,
            ["customApiUrl"] = agentDefinition.ApiCredential != null
                ? (agentDefinition.ApiCredential.BaseUrl ?? "")
                : (agentDefinition.CustomApiUrl ?? ""),
            ["provider"] = agentDefinition.ApiCredential != null
                ? (agentDefinition.ApiCredential.Provider ?? "")
                : agentDefinition.Provider.ToString()
        };
    }

    // Context JSON is user-controlled; unknown properties and malformed ids are ignored.
    public static (string? WorkspacePath, List<Guid> SkillIds) ParseTaskContext(string? inputContextJson)
    {
        string? workspacePath = null;
        var skillIds = new List<Guid>();

        if (string.IsNullOrWhiteSpace(inputContextJson))
        {
            return (workspacePath, skillIds);
        }

        try
        {
            using var doc = JsonDocument.Parse(inputContextJson);

            if (doc.RootElement.TryGetProperty("workspacePath", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
            {
                workspacePath = pathProp.GetString();
            }

            if (doc.RootElement.TryGetProperty("skillIds", out var skillsProp) && skillsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in skillsProp.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var skillId))
                    {
                        skillIds.Add(skillId);
                    }
                }
            }
        }
        catch { }

        return (workspacePath, skillIds);
    }

    public static string? BuildSkillsBlock(IReadOnlyList<SkillDefinition> skills)
    {
        if (skills.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Selected project skills. These are mandatory conventions for this task; every agent must follow them and the reviewer must flag violations:");
        foreach (var skill in skills)
        {
            builder.AppendLine();
            builder.AppendLine($"### {skill.Name} ({skill.Category})");
            builder.AppendLine(TrimForPrompt(skill.Instructions, 2000));
        }

        return builder.ToString().TrimEnd();
    }

    private static string GetRoleInstruction(AgentType agentType)
    {
        return agentType switch
        {
            AgentType.Planner => "Create an execution plan. Include selected agents, ordered steps, assumptions, and model suitability notes.",
            AgentType.Research => "Analyze only supplied prompt/context. Extract useful facts, unknowns, constraints, and follow-up research needs.",
            AgentType.Coder => "Build the project directly in the workspace using the provided filesystem tools. Call write_file once per file with the complete file content; use list_files/read_file to check your work. Always include README.md, Dockerfile, and docker-compose.yml (service name app, ports \"${HOST_PORT:-18080}:<port>\", healthcheck on GET /health). You cannot execute code, run tests, or use a shell — do not create scratch/check scripts, and do not rewrite a file unless you are fixing a concrete mistake. When every file is written, reply with a short plain-text summary of the project (no code blocks). Only if no tools are available: emit one fenced code block per file, tagged with a first-line comment like // filepath: path/to/file.go.",
            AgentType.Reviewer => "Review previous outputs for correctness, security, consistency, missing steps, and architectural fit. Always check for Dockerfile + docker-compose.yml + /health, and when API docs skill is applied: Swagger UI + /openapi.json with example request bodies. Return prioritized findings and concrete fixes.",
            AgentType.OpsMonitor => "Summarize execution health, usage signals, latency considerations, and operational risks from the previous outputs.",
            _ => "Complete the assigned agent role using the supplied context."
        };
    }

    private static string GetObjective(AgentType agentType)
    {
        return agentType switch
        {
            AgentType.Planner => "Produce the MVP execution plan for this task.",
            AgentType.Research => "Produce research notes and relevant context.",
            AgentType.Coder => "Produce the technical output requested by the user.",
            AgentType.Reviewer => "Review the previous outputs and suggest corrections.",
            AgentType.OpsMonitor => "Produce a short operational summary for this run.",
            _ => "Produce the requested agent output."
        };
    }

    private static string TrimForPrompt(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength), "\n[truncated]");
    }
}
