using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniAgentConsole.Infrastructure.Persistence;

#nullable disable

namespace OmniAgentConsole.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AgentConsoleDbContext))]
    [Migration("20260720110000_CredentialSecretRefs")]
    public partial class CredentialSecretRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent raw SQL so patched / partially-migrated DBs still apply cleanly.
            migrationBuilder.Sql(
                """
                ALTER TABLE api_credentials ALTER COLUMN "ApiKey" DROP NOT NULL;
                ALTER TABLE api_credentials ADD COLUMN IF NOT EXISTS "ApiKeySecretPath" character varying(320);
                ALTER TABLE api_credentials ADD COLUMN IF NOT EXISTS "ApiKeySecretKey" character varying(64);
                ALTER TABLE api_credentials ADD COLUMN IF NOT EXISTS "KeyLastFour" character varying(8);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE api_credentials SET "ApiKey" = COALESCE("ApiKey", '') WHERE "ApiKey" IS NULL;
                ALTER TABLE api_credentials DROP COLUMN IF EXISTS "ApiKeySecretPath";
                ALTER TABLE api_credentials DROP COLUMN IF EXISTS "ApiKeySecretKey";
                ALTER TABLE api_credentials DROP COLUMN IF EXISTS "KeyLastFour";
                ALTER TABLE api_credentials ALTER COLUMN "ApiKey" SET NOT NULL;
                """);
        }
    }
}
