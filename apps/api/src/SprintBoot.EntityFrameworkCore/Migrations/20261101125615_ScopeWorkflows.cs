using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class ScopeWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                schema: "workflows",
                table: "workflows",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                schema: "workflows",
                table: "statuses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflows_ProjectId",
                schema: "workflows",
                table: "workflows",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_statuses_ProjectId",
                schema: "workflows",
                table: "statuses",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workflows_ProjectId",
                schema: "workflows",
                table: "workflows");

            migrationBuilder.DropIndex(
                name: "IX_statuses_ProjectId",
                schema: "workflows",
                table: "statuses");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "workflows",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "workflows",
                table: "statuses");
        }
    }
}
