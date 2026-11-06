using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_link_types",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outward = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Inward = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_link_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "issue_links",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceIssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetIssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_links1", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_issue_links_LinkTypeId",
                schema: "issues",
                table: "issue_links",
                column: "LinkTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_issue_links_SourceIssueId_TargetIssueId_LinkTypeId",
                schema: "issues",
                table: "issue_links",
                columns: new[] { "SourceIssueId", "TargetIssueId", "LinkTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issue_links_TargetIssueId",
                schema: "issues",
                table: "issue_links",
                column: "TargetIssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_link_types",
                schema: "issues");

            migrationBuilder.DropTable(
                name: "issue_links",
                schema: "issues");
        }
    }
}
