using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowStepOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                schema: "workflows",
                table: "workflow_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                schema: "workflows",
                table: "workflow_steps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing versions start in the order the shared status list already had.
            migrationBuilder.Sql("""
                UPDATE workflows.workflow_steps AS step
                SET "Order" = ranked.position
                FROM (
                    SELECT inner_step."Id", ROW_NUMBER() OVER (
                        PARTITION BY inner_step."VersionId" ORDER BY status."Order") - 1 AS position
                    FROM workflows.workflow_steps AS inner_step
                    JOIN workflows.statuses AS status ON status."Id" = inner_step."StatusId"
                ) AS ranked
                WHERE ranked."Id" = step."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedAt",
                schema: "workflows",
                table: "workflow_versions");

            migrationBuilder.DropColumn(
                name: "Order",
                schema: "workflows",
                table: "workflow_steps");
        }
    }
}
