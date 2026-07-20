using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAgentConsole.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CredentialsSkillsAndFallbacks : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Formalizes schema that earlier releases applied through guarded startup SQL.
        /// Every operation is idempotent (IF NOT EXISTS / conditional) so the migration
        /// applies cleanly both to fresh databases and to databases already patched at
        /// runtime. Seed-data values are intentionally NOT touched here — runtime data
        /// (agent model chains, credentials, skills) must survive this migration.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_agent_definitions_Type";

                ALTER TABLE agent_definitions ADD COLUMN IF NOT EXISTS "ApiCredentialId" uuid;
                ALTER TABLE agent_definitions ADD COLUMN IF NOT EXISTS "CustomApiKey" character varying(500);
                ALTER TABLE agent_definitions ADD COLUMN IF NOT EXISTS "CustomApiUrl" character varying(500);
                ALTER TABLE agent_definitions ADD COLUMN IF NOT EXISTS "FallbackModels" character varying(500);
                ALTER TABLE agent_definitions ADD COLUMN IF NOT EXISTS "Provider" character varying(64) NOT NULL DEFAULT 'OmniAgent';

                CREATE TABLE IF NOT EXISTS api_credentials (
                    "Id" uuid NOT NULL,
                    "Name" character varying(200) NOT NULL,
                    "Provider" character varying(64) NOT NULL,
                    "BaseUrl" character varying(500),
                    "ApiKey" character varying(500) NOT NULL,
                    "IsDefault" boolean NOT NULL DEFAULT false,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone,
                    CONSTRAINT "PK_api_credentials" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS skill_definitions (
                    "Id" uuid NOT NULL,
                    "Name" character varying(200) NOT NULL,
                    "Category" character varying(64) NOT NULL,
                    "Description" character varying(1000) NOT NULL DEFAULT '',
                    "Instructions" text NOT NULL,
                    "Keywords" character varying(500) NOT NULL DEFAULT '',
                    "Enabled" boolean NOT NULL DEFAULT true,
                    "SortOrder" integer NOT NULL DEFAULT 0,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone,
                    CONSTRAINT "PK_skill_definitions" PRIMARY KEY ("Id")
                );

                -- Databases patched at runtime may predate the Keywords column or have a
                -- nullable Description; converge them to the current model.
                ALTER TABLE skill_definitions ADD COLUMN IF NOT EXISTS "Keywords" character varying(500) NOT NULL DEFAULT '';
                UPDATE skill_definitions SET "Description" = '' WHERE "Description" IS NULL;
                ALTER TABLE skill_definitions ALTER COLUMN "Description" SET NOT NULL;

                CREATE INDEX IF NOT EXISTS "IX_agent_definitions_ApiCredentialId" ON agent_definitions ("ApiCredentialId");
                CREATE INDEX IF NOT EXISTS "IX_agent_definitions_Type" ON agent_definitions ("Type");

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname IN ('FK_agent_definitions_api_credentials_ApiCredentialId', 'fk_agent_definitions_api_credentials')
                    ) THEN
                        ALTER TABLE agent_definitions
                        ADD CONSTRAINT "FK_agent_definitions_api_credentials_ApiCredentialId"
                        FOREIGN KEY ("ApiCredentialId") REFERENCES api_credentials ("Id")
                        ON DELETE SET NULL;
                    END IF;
                END $$;

                UPDATE model_provider_settings
                SET "BaseUrl" = 'https://integrate.api.nvidia.com/v1'
                WHERE "Id" = '20000000-0000-0000-0000-000000000001'
                  AND "BaseUrl" = 'https://integrate.api.omniagent.com/v1';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_definitions_api_credentials_ApiCredentialId",
                table: "agent_definitions");

            migrationBuilder.DropTable(
                name: "api_credentials");

            migrationBuilder.DropTable(
                name: "skill_definitions");

            migrationBuilder.DropIndex(
                name: "IX_agent_definitions_ApiCredentialId",
                table: "agent_definitions");

            migrationBuilder.DropIndex(
                name: "IX_agent_definitions_Type",
                table: "agent_definitions");

            migrationBuilder.DropColumn(
                name: "ApiCredentialId",
                table: "agent_definitions");

            migrationBuilder.DropColumn(
                name: "CustomApiKey",
                table: "agent_definitions");

            migrationBuilder.DropColumn(
                name: "CustomApiUrl",
                table: "agent_definitions");

            migrationBuilder.DropColumn(
                name: "FallbackModels",
                table: "agent_definitions");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "agent_definitions");

            migrationBuilder.UpdateData(
                table: "agent_definitions",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000003"),
                column: "SystemPrompt",
                value: "You are the coder agent. Produce concise, reviewable technical output.");

            migrationBuilder.UpdateData(
                table: "model_provider_settings",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"),
                column: "BaseUrl",
                value: "https://integrate.api.omniagent.com/v1");

            migrationBuilder.CreateIndex(
                name: "IX_agent_definitions_Type",
                table: "agent_definitions",
                column: "Type",
                unique: true);
        }
    }
}
