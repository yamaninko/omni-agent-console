using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAgentConsole.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedLabTaskOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerSessionId",
                table: "task_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_runs_OwnerSessionId",
                table: "task_runs",
                column: "OwnerSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_task_runs_OwnerSessionId",
                table: "task_runs");

            migrationBuilder.DropColumn(
                name: "OwnerSessionId",
                table: "task_runs");
        }
    }
}
