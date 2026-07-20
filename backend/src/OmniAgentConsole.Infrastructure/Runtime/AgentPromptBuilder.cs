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

        // Always-on packaging contract so Workspace "Project run" works even when
        // the model ignores optional skills (seen with Angular/React marketing sites).
        systemPromptParts.Add(BuildMandatoryPackagingBlock());

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
            AgentType.Coder => "Build the project directly in the workspace using the provided filesystem tools. Call write_file once per file with the COMPLETE file content. Before finishing you MUST have written at least: README.md, Dockerfile, docker-compose.yml, and .dockerignore at the deployable project root. Compose service name must be app; ports \"${HOST_PORT:-18080}:<containerPort>\"; healthcheck on GET /health. For Angular/React/Vite SPAs use multi-stage Dockerfile (node build → nginx:alpine) exposing 80 with a /health location. For APIs expose the app port (e.g. 8000). Prefer named Docker volumes over host bind mounts. You cannot execute code or use a shell. When every file is written (including Docker files), reply with a short plain-text summary only. Fallback if tools unavailable: fenced blocks with // filepath: comments.",
            AgentType.Reviewer => "Review previous outputs for correctness, security, consistency, missing steps, and architectural fit. CRITICAL if Dockerfile or docker-compose.yml is missing, incomplete, or would not allow `docker compose up` — list that as a must-fix finding. Also verify GET /health, and when API docs skill is applied: Swagger UI + /openapi.json with example bodies. Return prioritized findings and concrete fixes.",
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
            AgentType.Coder => "Produce the full application the user requested, including Dockerfile + docker-compose.yml so it can be started from Workspace Project run.",
            AgentType.Reviewer => "Review the previous outputs and suggest corrections. Flag missing Docker packaging as CRITICAL.",
            AgentType.OpsMonitor => "Produce a short operational summary for this run.",
            _ => "Produce the requested agent output."
        };
    }

    /// <summary>
    /// Hard requirement for every generated app (API or web) so OmniAgent Workspace
    /// one-click docker run works without relying on the user selecting a skill.
    /// </summary>
    public static string BuildMandatoryPackagingBlock() =>
        """
        ## MANDATORY Docker packaging (non-negotiable)

        Every application you generate MUST include these files at the project root
        (or each independently deployable unit's root):

        1. **Dockerfile**
           - API: multi-stage when possible; run the HTTP server; EXPOSE the app port; HEALTHCHECK hits GET /health.
           - Angular / React / Vite / static SPA: stage 1 `node` build, stage 2 `nginx:alpine`, copy dist into `/usr/share/nginx/html`, provide `nginx.conf` with `try_files` for SPA routing and `location = /health { return 200 'ok'; add_header Content-Type text/plain; }`, EXPOSE 80.
           - **Never COPY package-lock.json unless you also write_file it.** Prefer:
             `COPY package.json package-lock.json* ./`
             then `RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi`
             or simply `COPY package.json ./` + `RUN npm install` if no lockfile.
           - Same for yarn.lock / pnpm-lock.yaml — only COPY files that exist in the workspace.
        2. **docker-compose.yml**
           - Service name MUST be `app` (do not set a fixed container_name that conflicts across restarts).
           - Ports: `"${HOST_PORT:-18080}:80"` for nginx/SPA or `"${HOST_PORT:-18080}:<apiPort>"` for APIs.
           - healthcheck on /health.
           - Prefer **named volumes** for persistent data — do NOT bind-mount host paths like `./data:/data` (breaks Workspace runner via Docker socket).
           - Do not put obsolete `version:` key.
        3. **.dockerignore** — do not exclude files the Dockerfile COPYs (e.g. do not blanket-ignore `*.md` if you COPY README.md; do not ignore package.json).
        4. **README.md** — document `docker compose up -d --build` and the health URL.

        Before your final summary, call list_files and confirm Dockerfile and docker-compose.yml exist and that every COPY source in the Dockerfile is present on disk. If anything is missing, write it before finishing.
        """;

    private static string TrimForPrompt(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength), "\n[truncated]");
    }
}
