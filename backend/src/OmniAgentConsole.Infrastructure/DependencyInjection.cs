using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAgentConsole.Application.Configuration;
using OmniAgentConsole.Application.Providers;
using OmniAgentConsole.Application.Runtime;
using OmniAgentConsole.Application.Realtime;
using OmniAgentConsole.Application.Secrets;
using OmniAgentConsole.Infrastructure.Persistence;
using OmniAgentConsole.Infrastructure.Providers.Common;
using OmniAgentConsole.Infrastructure.Providers.OmniAgent;
using OmniAgentConsole.Infrastructure.Runtime;
using OmniAgentConsole.Infrastructure.Secrets;

namespace OmniAgentConsole.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OmniAgentProviderOptions>(configuration.GetSection(OmniAgentProviderOptions.SectionName));
        services.Configure<VaultOptions>(configuration.GetSection(VaultOptions.SectionName));
        services.Configure<TaskQueueOptions>(configuration.GetSection(TaskQueueOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=omniagent_console;Username=postgres;Password=postgres";

        services.AddDbContext<AgentConsoleDbContext>(options =>
            options.UseNpgsql(connectionString));

        var isWorker = configuration.GetValue<bool>("IsWorker", false);
        var redisConnection = configuration.GetConnectionString("Redis");
        var redisAvailable = false;
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
            try
            {
                var connectionMultiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection);
                services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(connectionMultiplexer);
                services.AddScoped<IConsoleEventPublisher, RedisConsoleEventPublisher>();
                services.AddSingleton<ITaskCancellationBroadcast, RedisTaskCancellationBroadcast>();
                redisAvailable = true;
            }
            catch
            {
                services.AddScoped<IConsoleEventPublisher, NullConsoleEventPublisher>();
            }
        }
        else
        {
            if (isWorker)
            {
                services.AddScoped<IConsoleEventPublisher, NullConsoleEventPublisher>();
            }
        }

        // Cross-process cancel is Redis-backed; without Redis the in-memory
        // registry still covers the single-process (no-RabbitMQ) topology.
        if (!redisAvailable)
        {
            services.AddSingleton<ITaskCancellationBroadcast, NullTaskCancellationBroadcast>();
        }

        services.AddSingleton<IModelRouter, StaticModelRouter>();
        services.AddSingleton<ITokenUsageExtractor, DefaultTokenUsageExtractor>();
        services.AddHttpClient<IModelProvider, OmniAgentModelProvider>();
        services.AddHttpClient<IProviderHealthCheck, OmniAgentProviderHealthCheck>();
        services.AddScoped<IConsoleEventService, ConsoleEventService>();
        services.AddScoped<ModelChainExecutor>();
        services.AddScoped<CoderToolLoopRunner>();
        services.AddScoped<IAgentOrchestratorService, AgentOrchestratorService>();
        var taskQueueMode = configuration.GetValue<string>($"{TaskQueueOptions.SectionName}:Mode");
        if (string.Equals(taskQueueMode, "RabbitMq", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ITaskRunQueue, RabbitMqTaskRunQueue>();
        }
        else
        {
            services.AddSingleton<ITaskRunQueue, InMemoryTaskRunQueue>();
        }

        services.AddSingleton<ITaskCancellationRegistry, InMemoryTaskCancellationRegistry>();

        if (isWorker || !string.Equals(taskQueueMode, "RabbitMq", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHostedService<TaskRunBackgroundService>();

            // Only the process that executes tasks holds cancellation tokens,
            // so only it needs to listen for cross-process cancel messages.
            if (redisAvailable)
            {
                services.AddHostedService<RedisTaskCancelSubscriber>();
            }
        }

        services.AddScoped<IProviderSecretResolver, ProviderSecretResolver>();

        var vaultEnabled = configuration.GetValue<bool>($"{VaultOptions.SectionName}:Enabled");
        if (vaultEnabled)
        {
            services.AddHttpClient<ISecretStore, VaultSecretStore>();
        }
        else
        {
            services.AddScoped<ISecretStore, EnvironmentSecretStore>();
        }

        return services;
    }
}
