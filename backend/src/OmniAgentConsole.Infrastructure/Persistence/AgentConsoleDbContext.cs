using Microsoft.EntityFrameworkCore;
using OmniAgentConsole.Domain.Entities;
using OmniAgentConsole.Domain.Enums;

namespace OmniAgentConsole.Infrastructure.Persistence;

public sealed class AgentConsoleDbContext : DbContext
{
    public AgentConsoleDbContext(DbContextOptions<AgentConsoleDbContext> options)
        : base(options)
    {
    }

    public DbSet<TaskRun> TaskRuns => Set<TaskRun>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();
    public DbSet<ModelProviderSetting> ModelProviderSettings => Set<ModelProviderSetting>();
    public DbSet<ModelDefinition> ModelDefinitions => Set<ModelDefinition>();
    public DbSet<ModelCallLog> ModelCallLogs => Set<ModelCallLog>();
    public DbSet<UsageMetric> UsageMetrics => Set<UsageMetric>();
    public DbSet<ConsoleEvent> ConsoleEvents => Set<ConsoleEvent>();
    public DbSet<AgentExecutionStep> AgentExecutionSteps => Set<AgentExecutionStep>();
    public DbSet<ApiCredential> ApiCredentials => Set<ApiCredential>();
    public DbSet<SkillDefinition> SkillDefinitions => Set<SkillDefinition>();
    public DbSet<AgentGroup> AgentGroups => Set<AgentGroup>();
    public DbSet<AgentGroupMember> AgentGroupMembers => Set<AgentGroupMember>();
    public DbSet<PanelSession> PanelSessions => Set<PanelSession>();
    public DbSet<PanelTurn> PanelTurns => Set<PanelTurn>();
    public DbSet<PanelConsoleEvent> PanelConsoleEvents => Set<PanelConsoleEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskRun>(entity =>
        {
            entity.ToTable("task_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(240);
            entity.Property(x => x.InputPrompt).IsRequired();
            entity.Property(x => x.InputContextJson).HasColumnType("jsonb");
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.Property(x => x.OwnerSessionId).HasMaxLength(64);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.OwnerSessionId);
        });

        modelBuilder.Entity<AgentRun>(entity =>
        {
            entity.ToTable("agent_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AgentName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.AgentType).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ConfigSnapshotJson).HasColumnType("jsonb");
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.HasOne(x => x.TaskRun).WithMany(x => x.AgentRuns).HasForeignKey(x => x.TaskRunId);
            entity.HasOne(x => x.AgentDefinition).WithMany().HasForeignKey(x => x.AgentDefinitionId);
            entity.HasIndex(x => new { x.TaskRunId, x.ExecutionOrder });
        });

        modelBuilder.Entity<AgentDefinition>(entity =>
        {
            entity.ToTable("agent_definitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.DefaultModel).HasMaxLength(160);
            entity.Property(x => x.FallbackModels).HasMaxLength(500);
            entity.Property(x => x.SystemPrompt).IsRequired();
            entity.Property(x => x.Temperature).HasPrecision(4, 2);
            entity.Property(x => x.Provider).HasConversion<string>().HasMaxLength(64).HasDefaultValue(ProviderType.OmniAgent);
            entity.Property(x => x.CustomApiUrl).HasMaxLength(500);
            entity.Property(x => x.CustomApiKey).HasMaxLength(500);
            entity.HasIndex(x => x.Type);
            entity.HasOne(x => x.ApiCredential)
                .WithMany()
                .HasForeignKey(x => x.ApiCredentialId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasData(DefaultAgentDefinitions());
        });

        modelBuilder.Entity<ApiCredential>(entity =>
        {
            entity.ToTable("api_credentials");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            entity.Property(x => x.BaseUrl).HasMaxLength(500);
            entity.Property(x => x.ApiKey).HasMaxLength(500);
            entity.Property(x => x.ApiKeySecretPath).HasMaxLength(320);
            entity.Property(x => x.ApiKeySecretKey).HasMaxLength(64);
            entity.Property(x => x.KeyLastFour).HasMaxLength(8);
            entity.Property(x => x.IsDefault).HasDefaultValue(false);
        });

        modelBuilder.Entity<SkillDefinition>(entity =>
        {
            entity.ToTable("skill_definitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Instructions).IsRequired();
            entity.Property(x => x.Keywords).HasMaxLength(500);
            entity.Property(x => x.Enabled).HasDefaultValue(true);
            entity.Property(x => x.SortOrder).HasDefaultValue(0);
        });

        modelBuilder.Entity<ModelProviderSetting>(entity =>
        {
            entity.ToTable("model_provider_settings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.BaseUrl).HasMaxLength(500);
            entity.Property(x => x.ApiKeySecretName).HasMaxLength(160);
            entity.Property(x => x.DefaultModel).HasMaxLength(160);
            entity.HasIndex(x => x.Provider).IsUnique();
            entity.HasData(DefaultProviderSettings());
        });

        modelBuilder.Entity<ModelDefinition>(entity =>
        {
            entity.ToTable("model_definitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Model).HasMaxLength(160).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(240);
            entity.HasIndex(x => new { x.Provider, x.Model }).IsUnique();
        });

        modelBuilder.Entity<ModelCallLog>(entity =>
        {
            entity.ToTable("model_call_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Model).HasMaxLength(160).IsRequired();
            entity.Property(x => x.RequestType).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.PromptHash).HasMaxLength(128);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ErrorCode).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.Property(x => x.EstimatedCost).HasPrecision(18, 8);
            entity.Property(x => x.RawMetadataJson).HasColumnType("jsonb");
            entity.HasOne(x => x.TaskRun).WithMany(x => x.ModelCallLogs).HasForeignKey(x => x.TaskRunId);
            entity.HasOne(x => x.AgentRun).WithMany(x => x.ModelCallLogs).HasForeignKey(x => x.AgentRunId);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => new { x.Provider, x.Model });
        });

        modelBuilder.Entity<ConsoleEvent>(entity =>
        {
            entity.ToTable("console_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Message).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb");
            entity.HasOne(x => x.TaskRun).WithMany(x => x.ConsoleEvents).HasForeignKey(x => x.TaskRunId);
            entity.HasOne(x => x.AgentRun).WithMany(x => x.ConsoleEvents).HasForeignKey(x => x.AgentRunId);
            entity.HasIndex(x => new { x.TaskRunId, x.CreatedAt });
        });

        modelBuilder.Entity<AgentExecutionStep>(entity =>
        {
            entity.ToTable("agent_execution_steps");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.StepName).HasMaxLength(160);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Message).HasMaxLength(4000);
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb");
            entity.HasOne(x => x.TaskRun).WithMany().HasForeignKey(x => x.TaskRunId);
            entity.HasOne(x => x.AgentRun).WithMany(x => x.ExecutionSteps).HasForeignKey(x => x.AgentRunId);
        });

        modelBuilder.Entity<UsageMetric>(entity =>
        {
            entity.ToTable("usage_metrics");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.AgentType).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Model).HasMaxLength(160);
            entity.Property(x => x.EstimatedCost).HasPrecision(18, 8);
            entity.HasIndex(x => new { x.PeriodStart, x.PeriodEnd });
        });

        modelBuilder.Entity<AgentGroup>(entity =>
        {
            entity.ToTable("agent_groups");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<AgentGroupMember>(entity =>
        {
            entity.ToTable("agent_group_members");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.SystemPrompt).IsRequired();
            entity.Property(x => x.DefaultModel).HasMaxLength(160).IsRequired();
            entity.Property(x => x.FallbackModels).HasMaxLength(500);
            entity.Property(x => x.Provider).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Temperature).HasPrecision(4, 2);
            entity.Property(x => x.Enabled).HasDefaultValue(true);
            entity.Property(x => x.SortOrder).HasDefaultValue(0);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(PanelMemberRole.Commentator);
            entity.Property(x => x.Stance).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(PanelStance.Neutral);
            entity.Property(x => x.StanceLabel).HasMaxLength(240);
            entity.HasOne(x => x.Group)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ApiCredential)
                .WithMany()
                .HasForeignKey(x => x.ApiCredentialId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.GroupId, x.SortOrder });
        });

        modelBuilder.Entity<PanelSession>(entity =>
        {
            entity.ToTable("panel_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(240);
            entity.Property(x => x.Topic).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.OwnerSessionId).HasMaxLength(64);
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.HasOne(x => x.Group)
                .WithMany(x => x.PanelSessions)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.OwnerSessionId);
        });

        modelBuilder.Entity<PanelTurn>(entity =>
        {
            entity.ToTable("panel_turns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MemberDisplayName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ModelUsed).HasMaxLength(160);
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.HasOne(x => x.Session)
                .WithMany(x => x.Turns)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Member)
                .WithMany()
                .HasForeignKey(x => x.MemberId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.SessionId, x.TurnOrder });
        });

        modelBuilder.Entity<PanelConsoleEvent>(entity =>
        {
            entity.ToTable("panel_console_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Message).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb");
            entity.HasOne(x => x.PanelSession)
                .WithMany(x => x.ConsoleEvents)
                .HasForeignKey(x => x.PanelSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.PanelTurn)
                .WithMany()
                .HasForeignKey(x => x.PanelTurnId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.PanelSessionId, x.CreatedAt });
        });
    }

    private static IEnumerable<AgentDefinition> DefaultAgentDefinitions()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        const string model = "meta/llama-3.1-8b-instruct";

        return new[]
        {
            new AgentDefinition
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Name = "Planner Agent",
                Type = AgentType.Planner,
                Description = "Analyzes the user request and creates the execution plan.",
                DefaultModel = model,
                SystemPrompt = "You are the planner agent. Break the task into structured steps and select agents.",
                CreatedAt = createdAt
            },
            new AgentDefinition
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Name = "Research Agent",
                Type = AgentType.Research,
                Description = "Analyzes provided context and extracts useful facts.",
                DefaultModel = model,
                SystemPrompt = "You are the research agent. Work only with supplied context in MVP mode.",
                CreatedAt = createdAt
            },
            new AgentDefinition
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                Name = "Coder Agent",
                Type = AgentType.Coder,
                Description = "Produces code and implementation guidance.",
                DefaultModel = model,
                SystemPrompt = "You are the Coder Agent. Your job is to output full, complete, and production-ready source code files. Do not output incomplete code snippets, do not output explanations, and do not output placeholder text. Each code block you produce must represent a single, full file. At the very beginning of each code block, you MUST include a comment indicating the target relative file path, formatted exactly as follows:\n- For Go: // filepath: path/to/file.go\n- For JavaScript/TypeScript: // filepath: path/to/file.js\n- For Python: # filepath: path/to/file.py\n- For HTML: <!-- filepath: path/to/file.html -->\n- For CSS: /* filepath: path/to/file.css */\n- For JSON: \"filepath\": \"path/to/file.json\" (must be inside the main object, e.g. as the first key)\n- For Markdown: <!-- filepath: path/to/file.md -->\nIf you are generating a project, you MUST always include a comprehensive README.md file as one of the code blocks.\nIf no specific version is mentioned in the prompt, you MUST default to using the latest stable production versions of all frameworks, libraries, databases, and runtimes (e.g., Node.js, .NET, Java, Go, Python, Redis, RabbitMQ, MongoDB, PostgreSQL, etc.).\nWrite complete, runnable code files that fulfill the request.",
                CreatedAt = createdAt
            },
            new AgentDefinition
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000004"),
                Name = "Reviewer Agent",
                Type = AgentType.Reviewer,
                Description = "Reviews quality, safety, consistency, and missing steps.",
                DefaultModel = model,
                SystemPrompt = "You are the reviewer agent. Find issues and provide concrete corrections.",
                CreatedAt = createdAt
            },
            new AgentDefinition
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000005"),
                Name = "Ops Monitor Agent",
                Type = AgentType.OpsMonitor,
                Description = "Tracks usage, latency, failures, and task runtime metrics.",
                DefaultModel = model,
                SystemPrompt = "You are the ops monitor agent. Summarize execution metrics and anomalies.",
                CreatedAt = createdAt
            }
        };
    }

    private static IEnumerable<ModelProviderSetting> DefaultProviderSettings()
    {
        return new[]
        {
            new ModelProviderSetting
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                Provider = ProviderType.OmniAgent,
                BaseUrl = "https://integrate.api.nvidia.com/v1",
                ApiKeySecretName = "OMNIAGENT_API_KEY",
                DefaultModel = "meta/llama-3.1-8b-instruct",
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            }
        };
    }
}
