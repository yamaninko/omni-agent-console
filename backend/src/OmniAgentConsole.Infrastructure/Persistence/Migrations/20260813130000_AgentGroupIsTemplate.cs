using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniAgentConsole.Infrastructure.Persistence;

#nullable disable

namespace OmniAgentConsole.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AgentConsoleDbContext))]
    [Migration("20260813130000_AgentGroupIsTemplate")]
    public partial class AgentGroupIsTemplate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE agent_groups
                ADD COLUMN IF NOT EXISTS "IsTemplate" boolean NOT NULL DEFAULT false;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE agent_groups DROP COLUMN IF EXISTS "IsTemplate";
                """);
        }
    }
}
