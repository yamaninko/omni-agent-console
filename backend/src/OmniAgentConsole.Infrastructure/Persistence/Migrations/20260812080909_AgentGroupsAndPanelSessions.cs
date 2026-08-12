using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAgentConsole.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentGroupsAndPanelSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_group_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    DefaultModel = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    FallbackModels = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApiCredentialId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaxTokens = table.Column<int>(type: "integer", nullable: false),
                    Temperature = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_group_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_group_members_agent_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "agent_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_group_members_api_credentials_ApiCredentialId",
                        column: x => x.ApiCredentialId,
                        principalTable: "api_credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "panel_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Topic = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CurrentMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    FloorDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalLatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    TotalInputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panel_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_panel_sessions_agent_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "agent_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "panel_turns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberDisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TurnOrder = table.Column<int>(type: "integer", nullable: false),
                    Output = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModelUsed = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panel_turns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_panel_turns_agent_group_members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "agent_group_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_panel_turns_panel_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "panel_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "panel_console_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PanelSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PanelTurnId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panel_console_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_panel_console_events_panel_sessions_PanelSessionId",
                        column: x => x.PanelSessionId,
                        principalTable: "panel_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_panel_console_events_panel_turns_PanelTurnId",
                        column: x => x.PanelTurnId,
                        principalTable: "panel_turns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_group_members_ApiCredentialId",
                table: "agent_group_members",
                column: "ApiCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_group_members_GroupId_SortOrder",
                table: "agent_group_members",
                columns: new[] { "GroupId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_groups_CreatedAt",
                table: "agent_groups",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_panel_console_events_PanelSessionId_CreatedAt",
                table: "panel_console_events",
                columns: new[] { "PanelSessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_panel_console_events_PanelTurnId",
                table: "panel_console_events",
                column: "PanelTurnId");

            migrationBuilder.CreateIndex(
                name: "IX_panel_sessions_CreatedAt",
                table: "panel_sessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_panel_sessions_GroupId",
                table: "panel_sessions",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_panel_sessions_OwnerSessionId",
                table: "panel_sessions",
                column: "OwnerSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_panel_sessions_Status",
                table: "panel_sessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_panel_turns_MemberId",
                table: "panel_turns",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_panel_turns_SessionId_TurnOrder",
                table: "panel_turns",
                columns: new[] { "SessionId", "TurnOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "panel_console_events");

            migrationBuilder.DropTable(
                name: "panel_turns");

            migrationBuilder.DropTable(
                name: "agent_group_members");

            migrationBuilder.DropTable(
                name: "panel_sessions");

            migrationBuilder.DropTable(
                name: "agent_groups");
        }
    }
}
