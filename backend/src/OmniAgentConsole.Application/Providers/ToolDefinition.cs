namespace OmniAgentConsole.Application.Providers;

/// <summary>
/// A function tool advertised to the model. <paramref name="ParametersJsonSchema"/>
/// is the raw JSON Schema for the arguments object, serialized as-is into the
/// OpenAI-compatible "function.parameters" field.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, string ParametersJsonSchema);
