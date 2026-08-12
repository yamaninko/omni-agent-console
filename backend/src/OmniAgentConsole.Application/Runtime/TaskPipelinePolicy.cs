using System;
using System.Collections.Generic;
using System.Linq;
using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Application.Runtime;

/// <summary>
/// Resolves which Studio agent types run for a task from a pipeline key
/// stored in task InputContextJson (e.g. "full", "coder", "plan-code-review").
/// </summary>
public static class TaskPipelinePolicy
{
    public const string Full = "full";
    public const string CoderOnly = "coder";
    public const string PlanCodeReview = "plan-code-review";

    public static readonly AgentType[] FullSequence =
    [
        AgentType.Planner,
        AgentType.Research,
        AgentType.Coder,
        AgentType.Reviewer,
        AgentType.OpsMonitor
    ];

    public static IReadOnlyList<AgentType> Resolve(string? pipelineKey)
    {
        var key = string.IsNullOrWhiteSpace(pipelineKey)
            ? Full
            : pipelineKey.Trim().ToLowerInvariant();

        return key switch
        {
            CoderOnly => [AgentType.Coder],
            PlanCodeReview => [AgentType.Planner, AgentType.Coder, AgentType.Reviewer],
            _ => FullSequence
        };
    }

    public static bool IsKnown(string? pipelineKey)
    {
        if (string.IsNullOrWhiteSpace(pipelineKey)) return true;
        var key = pipelineKey.Trim().ToLowerInvariant();
        return key is Full or CoderOnly or PlanCodeReview;
    }

    public static string Normalize(string? pipelineKey)
    {
        if (string.IsNullOrWhiteSpace(pipelineKey)) return Full;
        var key = pipelineKey.Trim().ToLowerInvariant();
        return IsKnown(key) ? key : Full;
    }
}
