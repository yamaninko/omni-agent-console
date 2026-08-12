using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAgentConsole.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PanelMemberRoleAndStance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "agent_group_members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Commentator");

            migrationBuilder.AddColumn<string>(
                name: "Stance",
                table: "agent_group_members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Neutral");

            migrationBuilder.AddColumn<string>(
                name: "StanceLabel",
                table: "agent_group_members",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "agent_group_members");

            migrationBuilder.DropColumn(
                name: "Stance",
                table: "agent_group_members");

            migrationBuilder.DropColumn(
                name: "StanceLabel",
                table: "agent_group_members");
        }
    }
}
