namespace OmniAgentConsole.Application.Tasks;

public sealed record CreateTaskRequest(string Prompt, string? Title = null, string? InputContextJson = null);
