using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalIssueTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GlobalTemplateId",
                schema: "issues",
                table: "issue_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                schema: "issues",
                table: "issue_templates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "global_issue_templates",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_global_issue_templates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_issue_templates_GlobalTemplateId",
                schema: "issues",
                table: "issue_templates",
                column: "GlobalTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "global_issue_templates",
                schema: "issues");

            migrationBuilder.DropIndex(
                name: "IX_issue_templates_GlobalTemplateId",
                schema: "issues",
                table: "issue_templates");

            migrationBuilder.DropColumn(
                name: "GlobalTemplateId",
                schema: "issues",
                table: "issue_templates");

            migrationBuilder.DropColumn(
                name: "Mode",
                schema: "issues",
                table: "issue_templates");
        }
    }
}
