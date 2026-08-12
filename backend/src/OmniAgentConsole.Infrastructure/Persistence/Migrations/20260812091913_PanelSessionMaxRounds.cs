using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAgentConsole.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PanelSessionMaxRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxRounds",
                table: "panel_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxRounds",
                table: "panel_sessions");
        }
    }
}
