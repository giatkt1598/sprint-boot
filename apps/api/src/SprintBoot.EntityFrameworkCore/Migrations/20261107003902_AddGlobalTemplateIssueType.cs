using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalTemplateIssueType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IssueTypeKey",
                schema: "issues",
                table: "global_issue_templates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Existing templates take the type spaces already use them for, and Task when none does.
            migrationBuilder.Sql("""
                UPDATE issues.global_issue_templates AS g
                SET "IssueTypeKey" = COALESCE(
                    (SELECT t."Icon"
                     FROM issues.issue_templates AS s
                     JOIN issues.issue_types AS t ON t."Id" = s."IssueTypeId"
                     WHERE s."GlobalTemplateId" = g."Id"
                     GROUP BY t."Icon"
                     ORDER BY COUNT(*) DESC, t."Icon"
                     LIMIT 1),
                    'task');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssueTypeKey",
                schema: "issues",
                table: "global_issue_templates");
        }
    }
}
