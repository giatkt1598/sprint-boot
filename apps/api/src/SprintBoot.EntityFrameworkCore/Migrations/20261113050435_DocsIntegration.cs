using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class DocsIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ticket_links1",
                schema: "tickets",
                table: "ticket_links");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ticket_links",
                schema: "git",
                table: "ticket_links");

            migrationBuilder.DropPrimaryKey(
                name: "PK_connections",
                schema: "git",
                table: "connections");

            migrationBuilder.EnsureSchema(
                name: "docs");

            migrationBuilder.RenameIndex(
                name: "IX_ticket_links_ProjectId",
                schema: "git",
                table: "ticket_links",
                newName: "IX_ticket_links_ProjectId1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ticket_links2",
                schema: "tickets",
                table: "ticket_links",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ticket_links1",
                schema: "git",
                table: "ticket_links",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_connections1",
                schema: "git",
                table: "connections",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "connections",
                schema: "docs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "text", nullable: true),
                    BridgeSecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Problem = table.Column<int>(type: "integer", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DocmostUser = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "project_spaces",
                schema: "docs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocmostSpaceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_spaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_links",
                schema: "docs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SpaceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_links", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_project_spaces_DocmostSpaceId",
                schema: "docs",
                table: "project_spaces",
                column: "DocmostSpaceId");

            migrationBuilder.CreateIndex(
                name: "IX_project_spaces_ProjectId",
                schema: "docs",
                table: "project_spaces",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_links_PageId",
                schema: "docs",
                table: "ticket_links",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_links_ProjectId",
                schema: "docs",
                table: "ticket_links",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_links_TicketId_PageId_Source",
                schema: "docs",
                table: "ticket_links",
                columns: new[] { "TicketId", "PageId", "Source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connections",
                schema: "docs");

            migrationBuilder.DropTable(
                name: "project_spaces",
                schema: "docs");

            migrationBuilder.DropTable(
                name: "ticket_links",
                schema: "docs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ticket_links2",
                schema: "tickets",
                table: "ticket_links");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ticket_links1",
                schema: "git",
                table: "ticket_links");

            migrationBuilder.DropPrimaryKey(
                name: "PK_connections1",
                schema: "git",
                table: "connections");

            migrationBuilder.RenameIndex(
                name: "IX_ticket_links_ProjectId1",
                schema: "git",
                table: "ticket_links",
                newName: "IX_ticket_links_ProjectId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ticket_links1",
                schema: "tickets",
                table: "ticket_links",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ticket_links",
                schema: "git",
                table: "ticket_links",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_connections",
                schema: "git",
                table: "connections",
                column: "Id");
        }
    }
}
