using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddGitIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_webhook_deliveries",
                schema: "workflows",
                table: "webhook_deliveries");

            migrationBuilder.EnsureSchema(
                name: "git");

            migrationBuilder.AddPrimaryKey(
                name: "PK_webhook_deliveries1",
                schema: "workflows",
                table: "webhook_deliveries",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "git",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ci_statuses",
                schema: "git",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ci_statuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "commits",
                schema: "git",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "connections",
                schema: "git",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderType = table.Column<int>(type: "integer", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AuthType = table.Column<int>(type: "integer", nullable: false),
                    EncryptedCredentials = table.Column<string>(type: "text", nullable: true),
                    InstallationId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Problem = table.Column<int>(type: "integer", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                name: "issue_links",
                schema: "git",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pull_requests",
                schema: "git",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    HeadSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pull_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "repositories",
                schema: "git",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FullName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DefaultBranch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    WebUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IsPrivate = table.Column<bool>(type: "boolean", nullable: false),
                    IsAccessible = table.Column<bool>(type: "boolean", nullable: false),
                    EncryptedWebhookSecret = table.Column<string>(type: "text", nullable: true),
                    ExternalHookId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    WebhookState = table.Column<int>(type: "integer", nullable: false),
                    LastEventAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repositories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "git",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_branches_RepositoryId_Name",
                schema: "git",
                table: "branches",
                columns: new[] { "RepositoryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ci_statuses_RepositoryId_Sha_Name",
                schema: "git",
                table: "ci_statuses",
                columns: new[] { "RepositoryId", "Sha", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_commits_RepositoryId_Sha",
                schema: "git",
                table: "commits",
                columns: new[] { "RepositoryId", "Sha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issue_links_IssueId_TargetType_TargetId",
                schema: "git",
                table: "issue_links",
                columns: new[] { "IssueId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issue_links_ProjectId",
                schema: "git",
                table: "issue_links",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_pull_requests_RepositoryId_Number",
                schema: "git",
                table: "pull_requests",
                columns: new[] { "RepositoryId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_repositories_ConnectionId_ExternalId",
                schema: "git",
                table: "repositories",
                columns: new[] { "ConnectionId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_Provider_DeliveryId",
                schema: "git",
                table: "webhook_deliveries",
                columns: new[] { "Provider", "DeliveryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branches",
                schema: "git");

            migrationBuilder.DropTable(
                name: "ci_statuses",
                schema: "git");

            migrationBuilder.DropTable(
                name: "commits",
                schema: "git");

            migrationBuilder.DropTable(
                name: "connections",
                schema: "git");

            migrationBuilder.DropTable(
                name: "issue_links",
                schema: "git");

            migrationBuilder.DropTable(
                name: "pull_requests",
                schema: "git");

            migrationBuilder.DropTable(
                name: "repositories",
                schema: "git");

            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "git");

            migrationBuilder.DropPrimaryKey(
                name: "PK_webhook_deliveries1",
                schema: "workflows",
                table: "webhook_deliveries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_webhook_deliveries",
                schema: "workflows",
                table: "webhook_deliveries",
                column: "Id");
        }
    }
}
