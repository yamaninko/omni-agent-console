using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniAgentConsole.Infrastructure.Persistence;

#nullable disable

namespace OmniAgentConsole.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AgentConsoleDbContext))]
    [Migration("20260812120000_PanelSessionVotesJson")]
    public partial class PanelSessionVotesJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE panel_sessions
                ADD COLUMN IF NOT EXISTS "VotesJson" jsonb NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE panel_sessions
                DROP COLUMN IF EXISTS "VotesJson";
                """);
        }
    }
}
