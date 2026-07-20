using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OmniAgentConsole.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` tooling; bypasses the application host (and its
/// Redis/RabbitMQ wiring) so migrations can be scaffolded without live services.
/// The connection string is never opened by `migrations add`.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AgentConsoleDbContext>
{
    public AgentConsoleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AgentConsoleDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=omniagent_console;Username=postgres;Password=postgres")
            .Options;

        return new AgentConsoleDbContext(options);
    }
}
