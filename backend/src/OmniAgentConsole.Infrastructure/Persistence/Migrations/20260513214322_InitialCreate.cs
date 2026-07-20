using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OmniAgentConsole.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultModel = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    MaxTokens = table.Column<int>(type: "integer", nullable: false),
                    Temperature = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "model_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    SupportsChat = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsEmbeddings = table.Column<bool>(type: "boolean", nullable: false),
                    ContextWindow = table.Column<int>(type: "integer", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "model_provider_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ApiKeySecretName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultModel = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_provider_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "task_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    InputPrompt = table.Column<string>(type: "text", nullable: false),
                    InputContextJson = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalInputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalLatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "usage_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    AgentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TotalRequests = table.Column<int>(type: "integer", nullable: false),
                    SuccessfulRequests = table.Column<int>(type: "integer", nullable: false),
                    FailedRequests = table.Column<int>(type: "integer", nullable: false),
                    TotalInputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalLatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_metrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AgentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Input = table.Column<string>(type: "text", nullable: true),
                    Output = table.Column<string>(type: "text", nullable: true),
                    ConfigSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    ExecutionOrder = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_runs_agent_definitions_AgentDefinitionId",
                        column: x => x.AgentDefinitionId,
                        principalTable: "agent_definitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_agent_runs_task_runs_TaskRunId",
                        column: x => x.TaskRunId,
                        principalTable: "task_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_execution_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_execution_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_execution_steps_agent_runs_AgentRunId",
                        column: x => x.AgentRunId,
                        principalTable: "agent_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_execution_steps_task_runs_TaskRunId",
                        column: x => x.TaskRunId,
                        principalTable: "task_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "console_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_console_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_console_events_agent_runs_AgentRunId",
                        column: x => x.AgentRunId,
                        principalTable: "agent_runs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_console_events_task_runs_TaskRunId",
                        column: x => x.TaskRunId,
                        principalTable: "task_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "model_call_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    RawMetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_call_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_call_logs_agent_runs_AgentRunId",
                        column: x => x.AgentRunId,
                        principalTable: "agent_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_model_call_logs_task_runs_TaskRunId",
                        column: x => x.TaskRunId,
                        principalTable: "task_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "agent_definitions",
                columns: new[] { "Id", "CreatedAt", "DefaultModel", "Description", "Enabled", "MaxTokens", "Name", "RetryCount", "SystemPrompt", "Temperature", "TimeoutSeconds", "Type", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "meta/llama-3.1-8b-instruct", "Analyzes the user request and creates the execution plan.", true, 4096, "Planner Agent", 2, "You are the planner agent. Break the task into structured steps and select agents.", 0.2m, 120, "Planner", null },
                    { new Guid("10000000-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "meta/llama-3.1-8b-instruct", "Analyzes provided context and extracts useful facts.", true, 4096, "Research Agent", 2, "You are the research agent. Work only with supplied context in MVP mode.", 0.2m, 120, "Research", null },
                    { new Guid("10000000-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "meta/llama-3.1-8b-instruct", "Produces code and implementation guidance.", true, 4096, "Coder Agent", 2, "You are the coder agent. Produce concise, reviewable technical output.", 0.2m, 120, "Coder", null },
                    { new Guid("10000000-0000-0000-0000-000000000004"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "meta/llama-3.1-8b-instruct", "Reviews quality, safety, consistency, and missing steps.", true, 4096, "Reviewer Agent", 2, "You are the reviewer agent. Find issues and provide concrete corrections.", 0.2m, 120, "Reviewer", null },
                    { new Guid("10000000-0000-0000-0000-000000000005"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "meta/llama-3.1-8b-instruct", "Tracks usage, latency, failures, and task runtime metrics.", true, 4096, "Ops Monitor Agent", 2, "You are the ops monitor agent. Summarize execution metrics and anomalies.", 0.2m, 120, "OpsMonitor", null }
                });

            migrationBuilder.InsertData(
                table: "model_provider_settings",
                columns: new[] { "Id", "ApiKeySecretName", "BaseUrl", "CreatedAt", "DefaultModel", "Enabled", "Provider", "RetryCount", "TimeoutSeconds", "UpdatedAt" },
                values: new object[] { new Guid("20000000-0000-0000-0000-000000000001"), "OMNIAGENT_API_KEY", "https://integrate.api.omniagent.com/v1", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "meta/llama-3.1-8b-instruct", true, "OmniAgent", 2, 120, null });

            migrationBuilder.CreateIndex(
                name: "IX_agent_definitions_Type",
                table: "agent_definitions",
                column: "Type",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_execution_steps_AgentRunId",
                table: "agent_execution_steps",
                column: "AgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_execution_steps_TaskRunId",
                table: "agent_execution_steps",
                column: "TaskRunId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_AgentDefinitionId",
                table: "agent_runs",
                column: "AgentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_TaskRunId_ExecutionOrder",
                table: "agent_runs",
                columns: new[] { "TaskRunId", "ExecutionOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_console_events_AgentRunId",
                table: "console_events",
                column: "AgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_console_events_TaskRunId_CreatedAt",
                table: "console_events",
                columns: new[] { "TaskRunId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_model_call_logs_AgentRunId",
                table: "model_call_logs",
                column: "AgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_model_call_logs_CreatedAt",
                table: "model_call_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_model_call_logs_Provider_Model",
                table: "model_call_logs",
                columns: new[] { "Provider", "Model" });

            migrationBuilder.CreateIndex(
                name: "IX_model_call_logs_TaskRunId",
                table: "model_call_logs",
                column: "TaskRunId");

            migrationBuilder.CreateIndex(
                name: "IX_model_definitions_Provider_Model",
                table: "model_definitions",
                columns: new[] { "Provider", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_provider_settings_Provider",
                table: "model_provider_settings",
                column: "Provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_runs_CreatedAt",
                table: "task_runs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_task_runs_Status",
                table: "task_runs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_usage_metrics_PeriodStart_PeriodEnd",
                table: "usage_metrics",
                columns: new[] { "PeriodStart", "PeriodEnd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_execution_steps");

            migrationBuilder.DropTable(
                name: "console_events");

            migrationBuilder.DropTable(
                name: "model_call_logs");

            migrationBuilder.DropTable(
                name: "model_definitions");

            migrationBuilder.DropTable(
                name: "model_provider_settings");

            migrationBuilder.DropTable(
                name: "usage_metrics");

            migrationBuilder.DropTable(
                name: "agent_runs");

            migrationBuilder.DropTable(
                name: "agent_definitions");

            migrationBuilder.DropTable(
                name: "task_runs");
        }
    }
}
