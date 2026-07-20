using System;

namespace OmniAgentConsole.Domain.Entities;

/// <summary>
/// A reusable instruction pack (project convention, stack template, quality bar)
/// that can be attached to a task and injected into every agent's prompt.
/// </summary>
public sealed class SkillDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // e.g. "Backend", "Frontend", "Security", "Packaging", "Quality", "Data"
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty; // comma-separated; drives prompt-based auto-suggestion
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
