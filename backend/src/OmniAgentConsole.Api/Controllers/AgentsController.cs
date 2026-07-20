using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Application.Agents;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using OmniAgentConsole.Infrastructure.Persistence;

namespace OmniAgentConsole.Api.Controllers;

[ApiController]
[Route("api/agents")]
public sealed class AgentsController : ControllerBase
{
    private readonly AgentConsoleDbContext dbContext;
    private readonly IHttpClientFactory httpClientFactory;

    public AgentsController(AgentConsoleDbContext dbContext, IHttpClientFactory httpClientFactory)
    {
        this.dbContext = dbContext;
        this.httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentDefinitionDto>>> List(CancellationToken cancellationToken)
    {
        var definitions = await dbContext.AgentDefinitions
            .AsNoTracking()
            .OrderBy(x => x.Type)
            .ToListAsync(cancellationToken);

        return Ok(definitions.Select(ToDto).ToList());
    }

    [HttpPut("{agentDefinitionId:guid}")]
    public async Task<ActionResult<AgentDefinitionDto>> Update(
        Guid agentDefinitionId,
        UpdateAgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.AgentDefinitions
            .FirstOrDefaultAsync(x => x.Id == agentDefinitionId, cancellationToken);

        if (definition is null)
        {
            return NotFound();
        }

        var validationError = Validate(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        definition.Enabled = request.Enabled;
        definition.DefaultModel = request.DefaultModel.Trim();
        definition.FallbackModels = string.IsNullOrWhiteSpace(request.FallbackModels) ? null : request.FallbackModels.Trim();
        definition.SystemPrompt = request.SystemPrompt.Trim();
        definition.MaxTokens = request.MaxTokens;
        definition.Temperature = request.Temperature;
        definition.TimeoutSeconds = request.TimeoutSeconds;
        definition.RetryCount = request.RetryCount;
        definition.Provider = request.Provider;
        definition.CustomApiUrl = request.CustomApiUrl?.Trim();

        // Empty custom API key on update means "keep the stored key" — the raw key is
        // never returned to the client, so the edit form cannot round-trip it.
        if (!string.IsNullOrWhiteSpace(request.CustomApiKey))
        {
            definition.CustomApiKey = request.CustomApiKey.Trim();
        }

        definition.ApiCredentialId = request.ApiCredentialId;
        if (!string.IsNullOrWhiteSpace(request.Name)) definition.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.Description)) definition.Description = request.Description.Trim();
        if (request.Type.HasValue) definition.Type = request.Type.Value;
        definition.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(definition));
    }

    [HttpGet("runtime")]
    public async Task<IActionResult> RuntimeAgents(CancellationToken cancellationToken)
    {
        var activeAgents = await dbContext.AgentDefinitions
            .AsNoTracking()
            .Where(x => x.Enabled)
            .OrderBy(x => x.Type)
            .ToListAsync(cancellationToken);

        return Ok(activeAgents.Select(agent => new
        {
            agent.Name,
            agent.Type,
            agent.Description,
            Model = agent.DefaultModel
        }));
    }

    [HttpGet("models")]
    public async Task<ActionResult<IReadOnlyList<ModelDefinitionDto>>> ListModels(CancellationToken cancellationToken)
    {
        var models = await dbContext.ModelDefinitions
            .AsNoTracking()
            .Where(x => x.Enabled)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return Ok(models.Select(x => new ModelDefinitionDto(x.Id, x.Model, x.DisplayName, x.ContextWindow)).ToList());
    }

    [HttpPost("models")]
    public async Task<ActionResult<ModelDefinitionDto>> AddModel(
        [FromBody] CreateModelDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Model) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return BadRequest("Model key and Display Name are required.");
        }

        var model = new ModelDefinition
        {
            Provider = ProviderType.OmniAgent,
            Model = request.Model.Trim(),
            DisplayName = request.DisplayName.Trim(),
            ContextWindow = request.ContextWindow,
            SupportsChat = true,
            SupportsEmbeddings = false,
            Enabled = true
        };

        dbContext.ModelDefinitions.Add(model);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new ModelDefinitionDto(model.Id, model.Model, model.DisplayName, model.ContextWindow));
    }

    /// <summary>
    /// Lists the models the configured provider endpoint exposes (OpenAI-compatible
    /// GET /models — for NVIDIA this is the full build.nvidia.com API catalog).
    /// </summary>
    [HttpGet("models/available")]
    public async Task<IActionResult> ListAvailableModels(CancellationToken cancellationToken)
    {
        List<ProviderModel> providerModels;
        try
        {
            providerModels = await FetchProviderModelsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return StatusCode(502, $"Could not fetch models from provider: {ex.Message}");
        }

        var registered = await dbContext.ModelDefinitions
            .AsNoTracking()
            .Select(x => x.Model)
            .ToListAsync(cancellationToken);
        var registeredSet = registered.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Ok(providerModels
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(m => new { id = m.Id, ownedBy = m.OwnedBy, registered = registeredSet.Contains(m.Id) })
            .ToList());
    }

    /// <summary>
    /// Imports every provider model that is not yet in the registry.
    /// </summary>
    [HttpPost("models/sync")]
    public async Task<IActionResult> SyncModelsFromProvider(CancellationToken cancellationToken)
    {
        List<ProviderModel> providerModels;
        try
        {
            providerModels = await FetchProviderModelsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return StatusCode(502, $"Could not fetch models from provider: {ex.Message}");
        }

        var registered = await dbContext.ModelDefinitions
            .Select(x => x.Model)
            .ToListAsync(cancellationToken);
        var registeredSet = registered.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var imported = 0;
        foreach (var providerModel in providerModels)
        {
            if (string.IsNullOrWhiteSpace(providerModel.Id) || registeredSet.Contains(providerModel.Id))
            {
                continue;
            }

            dbContext.ModelDefinitions.Add(new ModelDefinition
            {
                Provider = ProviderType.OmniAgent,
                Model = providerModel.Id,
                DisplayName = ToModelDisplayName(providerModel.Id),
                ContextWindow = 0, // the /models endpoint does not report context windows
                SupportsChat = !IsNonChatModel(providerModel.Id),
                SupportsEmbeddings = providerModel.Id.Contains("embed", StringComparison.OrdinalIgnoreCase),
                Enabled = true
            });
            registeredSet.Add(providerModel.Id);
            imported++;
        }

        if (imported > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { imported, totalAvailable = providerModels.Count });
    }

    private async Task<List<ProviderModel>> FetchProviderModelsAsync(CancellationToken cancellationToken)
    {
        var credential = await dbContext.ApiCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsDefault, cancellationToken);

        var baseUrl = credential?.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "https://integrate.api.nvidia.com/v1";
        }

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/models");
        var apiKey = credential?.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey)
            && !(apiKey.StartsWith("YOUR_", StringComparison.Ordinal) && apiKey.EndsWith("_HERE", StringComparison.Ordinal)))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var models = new List<ProviderModel>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var element in data.EnumerateArray())
            {
                if (element.TryGetProperty("id", out var idProp) && idProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var ownedBy = element.TryGetProperty("owned_by", out var ownedProp) && ownedProp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? ownedProp.GetString() ?? string.Empty
                        : string.Empty;
                    models.Add(new ProviderModel(idProp.GetString()!, ownedBy));
                }
            }
        }

        return models;
    }

    private static string ToModelDisplayName(string modelId)
    {
        var name = modelId.Contains('/') ? modelId[(modelId.IndexOf('/') + 1)..] : modelId;
        name = name.Replace('-', ' ').Replace('_', ' ');
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }

    private static bool IsNonChatModel(string modelId) =>
        modelId.Contains("embed", StringComparison.OrdinalIgnoreCase)
        || modelId.Contains("rerank", StringComparison.OrdinalIgnoreCase)
        || modelId.Contains("clip", StringComparison.OrdinalIgnoreCase)
        || modelId.Contains("-ocr", StringComparison.OrdinalIgnoreCase)
        || modelId.Contains("paddleocr", StringComparison.OrdinalIgnoreCase);

    private sealed record ProviderModel(string Id, string OwnedBy);

    [HttpDelete("models/{id:guid}")]
    public async Task<IActionResult> DeleteModel(Guid id, CancellationToken cancellationToken)
    {
        var model = await dbContext.ModelDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        dbContext.ModelDefinitions.Remove(model);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static string? Validate(UpdateAgentDefinitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DefaultModel))
        {
            return "Default model is required.";
        }

        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return "System prompt is required.";
        }

        if (request.MaxTokens is < 128 or > 200_000)
        {
            return "Max tokens must be between 128 and 200000.";
        }

        if (request.Temperature is < 0 or > 2)
        {
            return "Temperature must be between 0 and 2.";
        }

        if (request.TimeoutSeconds is < 5 or > 600)
        {
            return "Timeout must be between 5 and 600 seconds.";
        }

        if (request.RetryCount is < 0 or > 5)
        {
            return "Retry count must be between 0 and 5.";
        }

        return null;
    }

    [HttpPost]
    public async Task<ActionResult<AgentDefinitionDto>> Create(
        [FromBody] UpdateAgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Agent Name is required.");
        }

        var definition = new AgentDefinition
        {
            Name = request.Name.Trim(),
            Type = request.Type ?? AgentType.Coder,
            Description = (request.Description ?? "Custom Agent").Trim(),
            Enabled = request.Enabled,
            DefaultModel = request.DefaultModel.Trim(),
            FallbackModels = string.IsNullOrWhiteSpace(request.FallbackModels) ? null : request.FallbackModels.Trim(),
            SystemPrompt = request.SystemPrompt.Trim(),
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            TimeoutSeconds = request.TimeoutSeconds,
            RetryCount = request.RetryCount,
            Provider = request.Provider,
            CustomApiUrl = request.CustomApiUrl?.Trim(),
            CustomApiKey = request.CustomApiKey?.Trim(),
            ApiCredentialId = request.ApiCredentialId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.AgentDefinitions.Add(definition);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(definition));
    }

    [HttpDelete("{agentDefinitionId:guid}")]
    public async Task<IActionResult> Delete(
        Guid agentDefinitionId,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.AgentDefinitions
            .FirstOrDefaultAsync(x => x.Id == agentDefinitionId, cancellationToken);

        if (definition is null)
        {
            return NotFound();
        }

        dbContext.AgentDefinitions.Remove(definition);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static AgentDefinitionDto ToDto(AgentDefinition definition)
    {
        return new AgentDefinitionDto(
            definition.Id,
            definition.Name,
            definition.Type,
            definition.Description,
            definition.Enabled,
            definition.DefaultModel,
            definition.SystemPrompt,
            definition.MaxTokens,
            definition.Temperature,
            definition.TimeoutSeconds,
            definition.RetryCount,
            definition.CreatedAt,
            definition.UpdatedAt,
            definition.Provider,
            definition.CustomApiUrl,
            !string.IsNullOrWhiteSpace(definition.CustomApiKey),
            definition.ApiCredentialId,
            definition.FallbackModels);
    }
}
