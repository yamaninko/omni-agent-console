using OmniAgentConsole.Api.Hubs;
using OmniAgentConsole.Api.Realtime;
using OmniAgentConsole.Application.Realtime;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Infrastructure;
using OmniAgentConsole.Infrastructure.Persistence;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using OmniAgentConsole.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("StudioCors", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:4200" };

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<OmniAgentConsole.Application.Configuration.SharedLabOptions>(
    builder.Configuration.GetSection(OmniAgentConsole.Application.Configuration.SharedLabOptions.SectionName));

var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddHostedService<RedisConsoleEventSubscriber>();
}

if (builder.Services.All(x => x.ServiceType != typeof(IConsoleEventPublisher)))
{
    builder.Services.AddScoped<IConsoleEventPublisher, SignalRConsoleEventPublisher>();
}

var app = builder.Build();

// Fail-fast: a shared-lab deployment without an admin key must not start
// (see docs/ROADMAP.md §1 — SHARED_LAB contract).
OmniAgentConsole.Application.Runtime.SharedLabPolicy.ValidateStartup(
    app.Configuration.GetValue<bool>("SharedLab:Enabled"),
    app.Configuration["Console:ApiKey"] ?? Environment.GetEnvironmentVariable("CONSOLE_API_KEY"));

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AgentConsoleDbContext>();

    if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
    {
        await dbContext.Database.MigrateAsync();
    }

    await EnsureAgentCustomFieldsExistAsync(dbContext);
    await EnsureSkillDefinitionsExistAsync(dbContext);

    // Move any remaining real plaintext provider keys into Vault (no-op when
    // the secret store is not writable / environment-only lab mode).
    var credentialKeys = scope.ServiceProvider.GetRequiredService<OmniAgentConsole.Application.Secrets.IApiCredentialKeyResolver>();
    await credentialKeys.MigratePlaintextKeysAsync(CancellationToken.None);

    // Vault -dev loses secrets on container recreate. If OMNIAGENT_API_KEY is in the
    // process environment, seed Vault (+ default credential) so Panel/Studio work
    // without a manual Settings visit after every compose up.
    await BootstrapOmniAgentKeyFromEnvironmentAsync(scope.ServiceProvider, CancellationToken.None);

    var omniAgentOptions = scope.ServiceProvider.GetRequiredService<IOptions<OmniAgentProviderOptions>>().Value;
    await ReconcileSeededModelDefaultsAsync(dbContext, omniAgentOptions);

    // Startup recovery may only run when this API process is also the executor
    // (no RabbitMQ → in-memory queue). With a separate worker, a Running task
    // usually IS still running: marking it Failed here would race the worker,
    // and an actually-dead run is redelivered by the queue NACK anyway.
    var queueMode = app.Configuration.GetValue<string>("TaskQueue:Mode");
    if (!string.Equals(queueMode, "RabbitMq", StringComparison.OrdinalIgnoreCase))
    {
        await RecoverInterruptedTaskRunsAsync(dbContext);
    }
    await SeedModelDefinitionsAsync(dbContext);
    await SyncAgentSystemPromptsAsync(dbContext);
    await ApplyRecommendedModelChainsAsync(dbContext);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("StudioCors");

app.UseMiddleware<ApiKeyMiddleware>();

app.MapControllers();
app.MapHub<ConsoleHub>("/ws/consoleHub");
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "omniagent-console-api" }));

app.Run();

static async Task BootstrapOmniAgentKeyFromEnvironmentAsync(
    IServiceProvider services,
    CancellationToken cancellationToken)
{
    var options = services.GetRequiredService<IOptions<OmniAgentProviderOptions>>().Value;
    var envName = string.IsNullOrWhiteSpace(options.ApiKeyEnvironmentVariable)
        ? "OMNIAGENT_API_KEY"
        : options.ApiKeyEnvironmentVariable;
    var envKey = Environment.GetEnvironmentVariable(envName);
    if (string.IsNullOrWhiteSpace(envKey))
    {
        return;
    }

    var providerSecrets = services.GetRequiredService<OmniAgentConsole.Application.Secrets.IProviderSecretResolver>();
    if (await providerSecrets.HasOmniAgentApiKeyAsync(cancellationToken))
    {
        // Vault or env already visible to Has* — still re-seed Vault if store is empty but
        // Has* returned true only via env (dev). Always write env key into Vault when writable
        // so credential paths used by Panel stay warm after a Vault wipe.
    }

    var trimmed = envKey.Trim();
    await providerSecrets.SetOmniAgentApiKeyAsync(trimmed, cancellationToken);

    var db = services.GetRequiredService<AgentConsoleDbContext>();
    var credentialKeys = services.GetRequiredService<OmniAgentConsole.Application.Secrets.IApiCredentialKeyResolver>();
    var defaultCredential = await db.ApiCredentials
        .Where(c => c.IsDefault
            || c.Provider == "OmniAgent"
            || c.Provider == "NVIDIA"
            || c.Provider == "Nvidia")
        .OrderByDescending(c => c.IsDefault)
        .FirstOrDefaultAsync(cancellationToken);
    if (defaultCredential is not null)
    {
        await credentialKeys.PersistKeyAsync(defaultCredential, trimmed, cancellationToken);
        defaultCredential.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}

static async Task ReconcileSeededModelDefaultsAsync(AgentConsoleDbContext dbContext, OmniAgentProviderOptions omniAgentOptions)
{
    const string obsoleteModel = "omniagent/llama-3.1-nemotron-70b-instruct";

    var changed = false;
    var agents = await dbContext.AgentDefinitions
        .Where(agent => agent.DefaultModel == obsoleteModel)
        .ToListAsync();

    foreach (var agent in agents)
    {
        agent.DefaultModel = omniAgentOptions.DefaultModel;
        agent.UpdatedAt = DateTimeOffset.UtcNow;
        changed = true;
    }

    var providerSettings = await dbContext.ModelProviderSettings
        .Where(setting => setting.DefaultModel == obsoleteModel)
        .ToListAsync();

    foreach (var setting in providerSettings)
    {
        setting.DefaultModel = omniAgentOptions.DefaultModel;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        changed = true;
    }

    if (changed)
    {
        await dbContext.SaveChangesAsync();
    }
}

static async Task RecoverInterruptedTaskRunsAsync(AgentConsoleDbContext dbContext)
{
    var now = DateTimeOffset.UtcNow;
    var interruptedTasks = await dbContext.TaskRuns
        .Where(task => task.Status == TaskRunStatus.Running)
        .ToListAsync();

    if (interruptedTasks.Count == 0)
    {
        return;
    }

    var taskIds = interruptedTasks.Select(task => task.Id).ToList();
    var runningAgents = await dbContext.AgentRuns
        .Where(agent => taskIds.Contains(agent.TaskRunId) && agent.Status == AgentRunStatus.Running)
        .ToListAsync();
    var startedModelCalls = await dbContext.ModelCallLogs
        .Where(modelCall => taskIds.Contains(modelCall.TaskRunId) && modelCall.Status == ModelCallStatus.Started)
        .ToListAsync();

    foreach (var task in interruptedTasks)
    {
        task.Status = TaskRunStatus.Failed;
        task.CompletedAt = now;
        task.ErrorMessage = "Task was interrupted by application restart before completion.";
        if (task.StartedAt.HasValue)
        {
            task.TotalLatencyMs = Math.Max(task.TotalLatencyMs, (long)(now - task.StartedAt.Value).TotalMilliseconds);
        }

        dbContext.ConsoleEvents.Add(new ConsoleEvent
        {
            TaskRunId = task.Id,
            EventType = ConsoleEventType.TaskFailed,
            Message = "Task marked failed during startup recovery: previous execution was interrupted.",
            CreatedAt = now
        });
    }

    foreach (var agent in runningAgents)
    {
        agent.Status = AgentRunStatus.Failed;
        agent.CompletedAt = now;
        agent.ErrorMessage = "Agent execution was interrupted by application restart.";
        if (agent.StartedAt.HasValue)
        {
            agent.LatencyMs = Math.Max(agent.LatencyMs, (long)(now - agent.StartedAt.Value).TotalMilliseconds);
        }
    }

    foreach (var modelCall in startedModelCalls)
    {
        modelCall.Status = ModelCallStatus.Failed;
        modelCall.CompletedAt = now;
        modelCall.ErrorCode = ProviderErrorCode.UnknownError;
        modelCall.ErrorMessage = "Model call was interrupted by application restart.";
        modelCall.LatencyMs = Math.Max(modelCall.LatencyMs, (long)(now - modelCall.StartedAt).TotalMilliseconds);
    }

    await dbContext.SaveChangesAsync();
}

static async Task SeedModelDefinitionsAsync(AgentConsoleDbContext dbContext)
{
    var hasModels = await dbContext.ModelDefinitions.AnyAsync();
    if (!hasModels)
    {
        var models = new List<ModelDefinition>
        {
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "meta/llama-3.1-8b-instruct",
                DisplayName = "Llama 3.1 8B Instruct",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 131072,
                Enabled = true
            },
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "meta/llama-3.1-70b-instruct",
                DisplayName = "Llama 3.1 70B Instruct",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 131072,
                Enabled = true
            },
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "meta/llama-3.1-405b-instruct",
                DisplayName = "Llama 3.1 405B Instruct",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 131072,
                Enabled = true
            },
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "omniagent/nemotron-4-340b-instruct",
                DisplayName = "Nemotron-4 340B Instruct",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 4096,
                Enabled = true
            },
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "mistralai/mixtral-8x22b-instruct-v0.1",
                DisplayName = "Mixtral 8x22B Instruct",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 65536,
                Enabled = true
            },
            // Recommended agent chain models (benchmarked 2026-07 on NVIDIA NIM);
            // context windows unknown from /v1/models, editable in Settings.
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "openai/gpt-oss-120b",
                DisplayName = "GPT-OSS 120B",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 0,
                Enabled = true
            },
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "nvidia/nemotron-3-super-120b-a12b",
                DisplayName = "Nemotron 3 Super 120B A12B",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 0,
                Enabled = true
            },
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "qwen/qwen3.5-122b-a10b",
                DisplayName = "Qwen3.5 122B A10B",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 0,
                Enabled = true
            },
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "deepseek-ai/deepseek-v4-flash",
                DisplayName = "DeepSeek V4 Flash",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 0,
                Enabled = true
            },
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "stepfun-ai/step-3.7-flash",
                DisplayName = "Step 3.7 Flash",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 0,
                Enabled = true
            },
            new()
            {
                Provider = ProviderType.OmniAgent,
                Model = "minimaxai/minimax-m3",
                DisplayName = "MiniMax M3",
                SupportsChat = true,
                SupportsEmbeddings = false,
                ContextWindow = 0,
                Enabled = true
            }
        };

        dbContext.ModelDefinitions.AddRange(models);
        await dbContext.SaveChangesAsync();
    }
}

// One-time upgrade for fresh/legacy installs: agents still on the factory default
// model with no fallback chain get the recommended chain (benchmarked 2026-07 on
// NVIDIA NIM). Any user customization — different model or an existing chain —
// leaves the agent untouched, so this never overwrites manual choices.
static async Task ApplyRecommendedModelChainsAsync(AgentConsoleDbContext dbContext)
{
    const string factoryDefaultModel = "meta/llama-3.1-8b-instruct";

    var recommended = new Dictionary<AgentType, (string Model, string Fallbacks, int? TimeoutSeconds)>
    {
        [AgentType.Planner] = ("openai/gpt-oss-120b", "nvidia/nemotron-3-super-120b-a12b,meta/llama-3.1-8b-instruct", null),
        [AgentType.Research] = ("nvidia/nemotron-3-super-120b-a12b", "stepfun-ai/step-3.7-flash,meta/llama-3.1-8b-instruct", null),
        // qwen/qwen3.5-122b-a10b was removed from the NIM catalog (HTTP 410, 2026-07-20).
        [AgentType.Coder] = ("deepseek-ai/deepseek-v4-flash", "openai/gpt-oss-120b,nvidia/nemotron-3-super-120b-a12b", 300),
        [AgentType.Reviewer] = ("openai/gpt-oss-120b", "nvidia/nemotron-3-super-120b-a12b,deepseek-ai/deepseek-v4-flash", 180),
        [AgentType.OpsMonitor] = ("meta/llama-3.1-8b-instruct", "stepfun-ai/step-3.7-flash,minimaxai/minimax-m3", null)
    };

    try
    {
        var untouchedAgents = await dbContext.AgentDefinitions
            .Where(agent => agent.FallbackModels == null && agent.DefaultModel == factoryDefaultModel)
            .ToListAsync();

        var changed = false;
        foreach (var agent in untouchedAgents)
        {
            if (!recommended.TryGetValue(agent.Type, out var chain))
            {
                continue;
            }

            agent.DefaultModel = chain.Model;
            agent.FallbackModels = chain.Fallbacks;
            if (chain.TimeoutSeconds.HasValue)
            {
                agent.TimeoutSeconds = chain.TimeoutSeconds.Value;
            }

            agent.UpdatedAt = DateTimeOffset.UtcNow;
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync();
        }
    }
    catch { }
}

static async Task SyncAgentSystemPromptsAsync(AgentConsoleDbContext dbContext)
{
    var changed = false;

    // Planner Agent
    var planner = await dbContext.AgentDefinitions.FindAsync(Guid.Parse("10000000-0000-0000-0000-000000000001"));
    if (planner != null && planner.SystemPrompt != "You are the planner agent. Break the task into structured steps and select agents.")
    {
        planner.SystemPrompt = "You are the planner agent. Break the task into structured steps and select agents.";
        planner.UpdatedAt = DateTimeOffset.UtcNow;
        changed = true;
    }

    // Research Agent
    var research = await dbContext.AgentDefinitions.FindAsync(Guid.Parse("10000000-0000-0000-0000-000000000002"));
    if (research != null && research.SystemPrompt != "You are the research agent. Work only with supplied context in MVP mode.")
    {
        research.SystemPrompt = "You are the research agent. Work only with supplied context in MVP mode.";
        research.UpdatedAt = DateTimeOffset.UtcNow;
        changed = true;
    }

    // Coder Agent
    var coder = await dbContext.AgentDefinitions.FindAsync(Guid.Parse("10000000-0000-0000-0000-000000000003"));
    var coderDefaultPrompt =
        "You are the Coder Agent. You build complete, production-ready projects directly in the task workspace using your filesystem tools.\n\n" +
        "TOOLS:\n- write_file(path, content): create or overwrite one file with COMPLETE content.\n- read_file(path): re-read a file.\n- list_files(path?): list workspace files.\n\n" +
        "WORKFLOW:\n1. Decide the full file layout.\n2. Write source files with write_file.\n" +
        "3. ALWAYS write packaging files before finishing: Dockerfile, docker-compose.yml (service name app, ports \"${HOST_PORT:-18080}:PORT\", healthcheck GET /health), .dockerignore, README.md.\n" +
        "   - Angular/React/Vite SPA: multi-stage node build + nginx:alpine, EXPOSE 80, /health returns 200.\n" +
        "   - APIs: run the HTTP server, EXPOSE app port, HEALTHCHECK /health.\n" +
        "   - Prefer named volumes; never rely on host bind mounts like ./data:/data for Workspace runner.\n" +
        "4. Call list_files and confirm Dockerfile + docker-compose.yml exist, then give a short plain-text summary (no code in the summary).\n\n" +
        "RULES:\n- Relative paths only.\n- No shell/tests execution; no scratch scripts.\n- Do not finish without Docker packaging files.\n- Latest stable versions when unspecified.\n- Fallback if tools unavailable: fenced blocks with // filepath: comments.";
    if (coder != null && coder.SystemPrompt != coderDefaultPrompt)
    {
        coder.SystemPrompt = coderDefaultPrompt;
        coder.UpdatedAt = DateTimeOffset.UtcNow;
        changed = true;
    }

    // Reviewer Agent
    var reviewer = await dbContext.AgentDefinitions.FindAsync(Guid.Parse("10000000-0000-0000-0000-000000000004"));
    if (reviewer != null && reviewer.SystemPrompt != "You are the reviewer agent. Find issues and provide concrete corrections.")
    {
        reviewer.SystemPrompt = "You are the reviewer agent. Find issues and provide concrete corrections.";
        reviewer.UpdatedAt = DateTimeOffset.UtcNow;
        changed = true;
    }

    // Ops Monitor Agent
    var ops = await dbContext.AgentDefinitions.FindAsync(Guid.Parse("10000000-0000-0000-0000-000000000005"));
    if (ops != null && ops.SystemPrompt != "You are the ops monitor agent. Summarize execution metrics and anomalies.")
    {
        ops.SystemPrompt = "You are the ops monitor agent. Summarize execution metrics and anomalies.";
        ops.UpdatedAt = DateTimeOffset.UtcNow;
        changed = true;
    }

    if (changed)
    {
        await dbContext.SaveChangesAsync();
    }
}

static async Task EnsureSkillDefinitionsExistAsync(AgentConsoleDbContext dbContext)
{
    // Schema lives in EF migrations (CredentialsSkillsAndFallbacks); this method only seeds data.
    // Upsert by name: new seed skills are added on upgrade, existing rows are left
    // untouched except for backfilling empty keyword lists.
    try
    {
        var existing = await dbContext.SkillDefinitions.ToListAsync();
        var existingByName = existing.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var seed in BuildSeedSkills())
        {
            if (existingByName.TryGetValue(seed.Name, out var current))
            {
                if (string.IsNullOrWhiteSpace(current.Keywords) && !string.IsNullOrWhiteSpace(seed.Keywords))
                {
                    current.Keywords = seed.Keywords;
                    changed = true;
                }

                // Contract / discovery skills: keep seed text + keywords in sync so
                // Studio auto-suggest and Workspace run/test contracts stay current.
                var forceSync =
                    string.Equals(seed.Name, "Dockerized Service", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(seed.Name, "Swagger / OpenAPI", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(seed.Category, "Frontend", StringComparison.OrdinalIgnoreCase);

                if (forceSync
                    && (current.Instructions != seed.Instructions
                        || current.Keywords != seed.Keywords
                        || current.Description != seed.Description))
                {
                    current.Instructions = seed.Instructions;
                    current.Description = seed.Description;
                    current.Keywords = seed.Keywords;
                    changed = true;
                }
            }
            else
            {
                dbContext.SkillDefinitions.Add(seed);
                changed = true;
            }
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync();
        }
    }
    catch { }
}

static List<SkillDefinition> BuildSeedSkills()
{
    var order = 0;
    SkillDefinition Skill(string name, string category, string description, string keywords, string instructions) => new()
    {
        Name = name,
        Category = category,
        Description = description,
        Keywords = keywords,
        Instructions = instructions,
        SortOrder = order++
    };

    return
    [
        Skill("Node.js + Express + TypeScript API", "Backend",
            "Complete Express/TypeScript service skeleton with proper project layout.",
            "node,node.js,nodejs,express,typescript",
            "Produce a complete Node.js + Express + TypeScript project, never a single file. Required files, each as its own code block with a filepath comment: package.json (name, scripts: dev/build/start, all dependencies with versions), tsconfig.json, src/index.ts (bootstrap only), src/app.ts (Express app wiring), src/routes/*.ts, src/controllers/*.ts, src/middleware/*.ts, src/services/*.ts, .env.example listing every environment variable, and README.md. Use ES module-compatible TypeScript with strict mode. Keep business logic out of route files."),

        Skill("Go REST API", "Backend",
            "Idiomatic Go HTTP service with go.mod and cmd/internal layout.",
            "go,golang,gin,fiber,chi",
            "Produce a complete Go project: go.mod with module path and dependency versions, cmd/server/main.go (bootstrap only), internal/handlers, internal/services, internal/models, internal/config reading environment variables, and README.md. Use the standard net/http mux or a minimal router (chi/gin), context.Context on every handler chain, proper error wrapping, and graceful shutdown with signal handling."),

        Skill(".NET Web API", "Backend",
            "ASP.NET Core Web API with layered project structure.",
            ".net,dotnet,asp.net,aspnet,c#,csharp,web api",
            "Produce a complete ASP.NET Core Web API: .csproj with package references, Program.cs with DI wiring, Controllers/, Services/, Models/ (or a layered structure), appsettings.json + appsettings.Development.json, and README.md. Use dependency injection, async endpoints, and configuration binding; never hardcode connection strings."),

        Skill("Java Spring Boot API", "Backend",
            "Spring Boot REST service with Maven/Gradle build and layered packages.",
            "java,spring,spring boot,springboot,maven,gradle",
            "Produce a complete Spring Boot project: pom.xml (or build.gradle) with dependency versions, src/main/java package layout (controller, service, repository, model, config), application.yml with environment-variable placeholders, and README.md. Use constructor injection, @RestController with proper response entities, bean validation annotations on DTOs, and a global @ControllerAdvice exception handler."),

        Skill("Python FastAPI Service", "Backend",
            "FastAPI project with pydantic models and routers.",
            "fastapi,python,uvicorn,pydantic",
            "Produce a complete FastAPI project: requirements.txt (or pyproject.toml), app/main.py bootstrap, app/routers/*.py, app/schemas/*.py with pydantic models, app/services/*.py, .env.example, and README.md with uvicorn run instructions. Use type hints everywhere and dependency injection via Depends."),

        Skill("Angular Frontend", "Frontend",
            "Angular app with standalone components, routing, and services.",
            "angular,angularjs,angular 21",
            "Produce a complete Angular application: package.json, angular.json, standalone components under src/app/features/, routing, typed services under src/app/core/, environments, README.md. Use signals or RxJS consistently. ALSO REQUIRED for Workspace run: multi-stage Dockerfile (node build → nginx:alpine serving dist). In Dockerfile use `COPY package.json package-lock.json* ./` and `npm ci` only if lockfile exists else `npm install` — never COPY a missing package-lock.json. Include nginx.conf (SPA try_files + GET /health → 200), docker-compose.yml service app ports \"${HOST_PORT:-18080}:80\" + healthcheck, safe .dockerignore."),

        Skill("React Frontend", "Frontend",
            "React (Vite) app with component/hook structure and typed API layer.",
            "react,reactjs,react.js,vite,next.js,nextjs,jsx,tsx,spa,web sitesi,website,web site,web sayfa,web sayfasi,frontend,arayuz,arayüz,landing,landing page,pazarlama sitesi,kurumsal site,ui,web ui",
            "Produce a complete React + TypeScript Vite app: package.json, vite.config.ts, index.html, src/main.tsx, src/App.tsx, components/hooks/api layers, .env.example, README.md. Functional components only. ALSO REQUIRED: multi-stage Dockerfile (node → nginx:alpine). COPY package.json package-lock.json* ./ and use npm ci only when lockfile exists, else npm install. nginx.conf with SPA try_files + /health, docker-compose.yml service app ports \"${HOST_PORT:-18080}:80\" + healthcheck, safe .dockerignore."),

        Skill("Flutter App", "Frontend",
            "Flutter mobile app with widget/state-management structure.",
            "flutter,dart,mobil,mobile,android,ios,iphone,uygulama",
            "Produce a complete Flutter application: pubspec.yaml with dependency versions, lib/main.dart (bootstrap only), lib/screens/, lib/widgets/, lib/services/ for API access, lib/models/ with typed model classes, and README.md with flutter run instructions. Use a clear state-management approach (Provider or Riverpod), const constructors where possible, and keep business logic out of widgets."),

        Skill("JWT Authentication", "Security",
            "Register/login flow with bcrypt password hashing and JWT middleware.",
            "jwt,json web token,auth,authentication,login,giris,kayit,bcrypt,yetkilendirme,token",
            "Implement POST /auth/register and POST /auth/login. Hash passwords with bcrypt (salt rounds >= 10); never store or log plaintext passwords. Sign JWTs with a secret from the JWT_SECRET environment variable, set an expiry (e.g. 1h), and add an authentication middleware that verifies the Bearer token and rejects invalid/expired tokens with 401. Include at least one protected example route. Never hardcode secrets in source code."),

        Skill("Input Validation (Zod/Joi)", "Quality",
            "Schema-based request validation on every endpoint.",
            "validation,joi,zod,dogrulama,validasyon,input validation",
            "Define a validation schema (Zod or Joi) for every request body, params, and query the API accepts. Apply them through a reusable validation middleware. On failure respond 400 with a JSON error envelope listing field-level messages. Do not duplicate validation logic inside controllers."),

        Skill("Dockerized Service", "Packaging",
            "Production-grade Dockerfile and compose wiring — required for Workspace one-click run.",
            "docker,dockerize,dockerfile,docker-compose,container,containerize,compose",
            "ALWAYS include Dockerfile + docker-compose.yml + .dockerignore at the deployable root (even single-service). Compose service name MUST be app (avoid fixed container_name); ports \"${HOST_PORT:-18080}:<containerPort>\"; healthcheck GET /health; no obsolete compose version key. Prefer multi-stage builds, non-root, EXPOSE. Named volumes only — never ./data:/data host binds. SPA/Angular/React/Vite: node build → nginx:alpine, EXPOSE 80, /health. Node Dockerfiles: COPY package.json package-lock.json* ./ then `if [ -f package-lock.json ]; then npm ci; else npm install; fi` — NEVER require package-lock.json unless write_file created it. Same for yarn.lock/pnpm-lock. API: expose app port. Document compose up + health URL. list_files before finish; every COPY path must exist."),

        Skill("PostgreSQL + Migrations", "Data",
            "PostgreSQL schema with migration scripts and safe query practices.",
            "postgres,postgresql,psql,migration",
            "Use PostgreSQL as the datastore. Provide migration scripts (SQL files or the stack migration tool) that create every table with appropriate types, primary/foreign keys, and indexes. Access the database only with parameterized queries or an ORM - never string-concatenated SQL. Read the connection string from environment variables and wire a postgres service into docker-compose.yml with a named volume."),

        Skill("Relational Database + ORM", "Data",
            "Persistent storage through an ORM with repository layering.",
            "orm,prisma,typeorm,knex,sqlalchemy,entity framework,ef core,hibernate,jpa",
            "Persist data in a real relational database (PostgreSQL preferred) through an ORM or query builder (Prisma, TypeORM, Knex, EF Core, SQLAlchemy, Hibernate...). Provide the schema or migration files, a repository/data-access layer separated from business logic, connection settings via environment variables, and compose wiring for the database. Do not use in-memory arrays as the data store."),

        Skill("MongoDB", "Data",
            "MongoDB persistence with typed models and indexes.",
            "mongo,mongodb,mongoose,nosql",
            "Use MongoDB as the datastore with the stack standard driver or ODM (Mongoose for Node, PyMongo/Motor for Python, MongoDB.Driver for .NET). Define typed schemas/models, create indexes for every queried field, read the connection string from environment variables, handle connection errors with retry, and wire a mongo service into docker-compose.yml with a named volume."),

        Skill("Redis Caching", "Data",
            "Cache-aside pattern with TTLs and resilient connection handling.",
            "redis,cache,caching,onbellek,cache-aside",
            "Use Redis with the cache-aside pattern: read-through on cache miss, explicit TTL on every key (no unbounded keys), and cache invalidation on writes. Build the Redis connection from environment variables with a retry/backoff strategy so the service still starts (degraded, uncached) when Redis is down. Wire a redis service into docker-compose.yml and document the key naming scheme in README.md."),

        Skill("RabbitMQ Messaging", "Data",
            "Publisher/consumer wiring with acknowledgements and dead-lettering.",
            "rabbitmq,rabbit,amqp,queue,kuyruk,message broker,messaging",
            "Use RabbitMQ for asynchronous messaging: declare durable queues/exchanges, publish persistent messages, and consume with manual acknowledgement (ack on success, nack with requeue or dead-letter on failure). Configure the connection from environment variables with retry on startup, define a dead-letter queue for poison messages, and wire a rabbitmq service into docker-compose.yml."),

        Skill("Health Checks & Observability", "Quality",
            "Health endpoints, structured logging, and container probes.",
            "health check,healthcheck,health,liveness,readiness,observability,monitoring",
            "Expose GET /health (liveness) and, when the service has dependencies, GET /ready (readiness verifying database/cache/broker connectivity). Return machine-readable JSON status. Add structured logging (request logging + errors with context) and reference the health endpoint from the Dockerfile HEALTHCHECK and compose healthcheck blocks."),

        Skill("Swagger / OpenAPI", "Quality",
            "Interactive API docs (Swagger UI) and OpenAPI 3 schema for try-it-out samples.",
            "swagger,openapi,swagger ui,openapi.json,redoc,api docs,api dokumantasyon,swaggerui,swashbuckle,springdoc",
            "Expose interactive API documentation and a machine-readable OpenAPI 3 document so the OmniAgent Workspace API tester can load real routes and example bodies.\n\nMUST include:\n1) Swagger UI (or equivalent) at a well-known path: prefer GET /docs or GET /swagger (and /redoc optional).\n2) OpenAPI JSON at GET /openapi.json (or /swagger/v1/swagger.json) — the document MUST list every public endpoint with method, path, summary, requestBody examples for POST/PUT/PATCH, and response schemas.\n3) Stack-native library: FastAPI (built-in openapi_url + docs_url), Node (swagger-ui-express + openapi.yaml/json), .NET (Swashbuckle), Java (springdoc-openapi), Go (swaggo/swag or similar).\n4) At least one realistic example request body per write endpoint (e.g. create note) inside the OpenAPI examples.\n5) README section: how to open Swagger UI (http://localhost:$HOST_PORT/docs) and that /openapi.json is available for tools.\n\nDo not hardcode secrets in examples. Keep docs enabled in the default docker-compose run (dev/demo)."),

        Skill("Unit Tests", "Quality",
            "Test coverage for core logic with the stack standard framework.",
            "test,unit test,jest,vitest,pytest,xunit,junit,tdd",
            "Add unit tests with the stack standard framework (Jest/Vitest for Node, xUnit for .NET, pytest for Python, JUnit for Java). Cover the core business logic and at least one failure path per endpoint (invalid input, unauthorized). Provide the test script in the project manifest (e.g. npm test) and list test files with filepath comments like every other file."),

        Skill("REST API Conventions", "Quality",
            "Consistent status codes, error envelope, and resource naming.",
            "rest api,restful,rest",
            "Use consistent RESTful conventions: nouns for resource paths, correct status codes (200/201/204/400/401/404/409/500), a single JSON error envelope shape ({ error: { code, message, details? } }), and pagination parameters for list endpoints. Document each endpoint (method, path, request, response) in README.md."),

        Skill("Project README & Docs", "Quality",
            "README with setup, env vars, API reference, and examples.",
            "readme,dokumantasyon,documentation",
            "Always include a README.md covering: project purpose, prerequisites, setup and run steps (local + Docker when applicable), a table of every environment variable with description and example value, API endpoint reference, and at least two curl example calls. Keep it accurate to the generated code.")
    ];
}

static async Task EnsureAgentCustomFieldsExistAsync(AgentConsoleDbContext dbContext)
{
    // Schema lives in EF migrations (CredentialsSkillsAndFallbacks); this method only
    // seeds the default provider credentials and repairs the default-credential flag.
    try
    {
        var count = await dbContext.ApiCredentials.CountAsync();
        if (count == 0)
        {
            dbContext.ApiCredentials.AddRange(new[]
            {
                new ApiCredential
                {
                    Name = "NVIDIA NIM",
                    Provider = "OmniAgent",
                    BaseUrl = "https://integrate.api.nvidia.com/v1",
                    ApiKey = "YOUR_NVIDIA_API_KEY_HERE",
                    IsDefault = true
                },
                new ApiCredential
                {
                    Name = "ChatGPT / OpenAI",
                    Provider = "OpenAi",
                    BaseUrl = "https://api.openai.com/v1",
                    ApiKey = "YOUR_OPENAI_API_KEY_HERE",
                    IsDefault = false
                },
                new ApiCredential
                {
                    Name = "Gemini",
                    Provider = "Gemini",
                    BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
                    ApiKey = "YOUR_GEMINI_API_KEY_HERE",
                    IsDefault = false
                },
                new ApiCredential
                {
                    Name = "Claude / Anthropic",
                    Provider = "Anthropic",
                    BaseUrl = "https://api.anthropic.com/v1",
                    ApiKey = "YOUR_ANTHROPIC_API_KEY_HERE",
                    IsDefault = false
                },
                new ApiCredential
                {
                    Name = "DeepSeek",
                    Provider = "Custom",
                    BaseUrl = "https://api.deepseek.com/v1",
                    ApiKey = "YOUR_DEEPSEEK_API_KEY_HERE",
                    IsDefault = false
                },
                new ApiCredential
                {
                    Name = "Kimi",
                    Provider = "Custom",
                    BaseUrl = "https://api.moonshot.cn/v1",
                    ApiKey = "YOUR_KIMI_API_KEY_HERE",
                    IsDefault = false
                }
            });
            await dbContext.SaveChangesAsync();
        }
    }
    catch { }

    try
    {
        var hasDefault = await dbContext.ApiCredentials.AnyAsync(c => c.IsDefault);
        if (!hasDefault)
        {
            var nim = await dbContext.ApiCredentials.FirstOrDefaultAsync(c => c.Name == "NVIDIA NIM");
            if (nim != null)
            {
                nim.IsDefault = true;
                await dbContext.SaveChangesAsync();
            }
            else
            {
                var first = await dbContext.ApiCredentials.FirstOrDefaultAsync();
                if (first != null)
                {
                    first.IsDefault = true;
                    await dbContext.SaveChangesAsync();
                }
            }
        }
    }
    catch { }
}
