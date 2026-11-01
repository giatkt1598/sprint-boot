using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectsWorkflowsIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "issues");

            migrationBuilder.EnsureSchema(
                name: "projects");

            migrationBuilder.EnsureSchema(
                name: "workflows");

            migrationBuilder.CreateTable(
                name: "attachments",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "comment_reactions",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comment_reactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    BodyText = table.Column<string>(type: "text", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "issue_activities",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<int>(type: "integer", nullable: false),
                    Field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OldValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_activities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "issue_labels",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    LabelId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_labels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "issue_types",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Icon = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Hierarchy = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "issues",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    TypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DescriptionText = table.Column<string>(type: "text", nullable: false),
                    StatusId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PriorityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Resolution = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssigneeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssigneeType = table.Column<int>(type: "integer", nullable: true),
                    ReporterId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    StoryPoints = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Rank = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false)
                        .Annotation("Npgsql:TsVectorConfig", "simple")
                        .Annotation("Npgsql:TsVectorProperties", new[] { "Title", "DescriptionText" }),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "labels",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_labels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "priorities",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_priorities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "project_members",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "project_role_permissions",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_role_permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "project_roles",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                schema: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultBoardType = table.Column<int>(type: "integer", nullable: false),
                    NextIssueNumber = table.Column<int>(type: "integer", nullable: false),
                    BranchNameTemplate = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "statuses",
                schema: "workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "work_logs",
                schema: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attachments_IssueId",
                schema: "issues",
                table: "attachments",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_comment_reactions_CommentId_UserId_Emoji",
                schema: "issues",
                table: "comment_reactions",
                columns: new[] { "CommentId", "UserId", "Emoji" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comments_IssueId",
                schema: "issues",
                table: "comments",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_issue_activities_IssueId_CreatedAt",
                schema: "issues",
                table: "issue_activities",
                columns: new[] { "IssueId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_issue_labels_IssueId_LabelId",
                schema: "issues",
                table: "issue_labels",
                columns: new[] { "IssueId", "LabelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issue_labels_LabelId",
                schema: "issues",
                table: "issue_labels",
                column: "LabelId");

            migrationBuilder.CreateIndex(
                name: "IX_issue_types_ProjectId_Name",
                schema: "issues",
                table: "issue_types",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issues_ParentId",
                schema: "issues",
                table: "issues",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_issues_ProjectId_LastModificationTime",
                schema: "issues",
                table: "issues",
                columns: new[] { "ProjectId", "LastModificationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_issues_ProjectId_Number",
                schema: "issues",
                table: "issues",
                columns: new[] { "ProjectId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issues_SearchVector",
                schema: "issues",
                table: "issues",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_labels_ProjectId_Name",
                schema: "issues",
                table: "labels",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_members_ProjectId_UserId",
                schema: "projects",
                table: "project_members",
                columns: new[] { "ProjectId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_members_UserId",
                schema: "projects",
                table: "project_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_project_role_permissions_RoleId_PermissionKey",
                schema: "projects",
                table: "project_role_permissions",
                columns: new[] { "RoleId", "PermissionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_roles_ProjectId_Name",
                schema: "projects",
                table: "project_roles",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_Key",
                schema: "projects",
                table: "projects",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_logs_IssueId",
                schema: "issues",
                table: "work_logs",
                column: "IssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments",
                schema: "issues");

            migrationBuilder.DropTable(
                name: "comment_reactions",
                schema: "issues");

            migrationBuilder.DropTable(
                name: "comments",
                schema: "issues");

            migrationBuilder.DropTable(
                name: "issue_activities",
                schema: "issues");

            migrationBuilder.DropTable(
                name: "issue_labels",
                schema: "issues");

            migrationBuilder.DropTable(
                name: "issue_types",
                schema: "issues");

            migrationBuilder.DropTable(
                name: "issues",
                schema: "issues");

            migrationBuilder.DropTable(
                name: "labels",
                schema: "issues");

            migrationBuilder.DropTable(
                name: "priorities",
                schema: "issues");

            migrationBuilder.DropTable(
                name: "project_members",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "project_role_permissions",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "project_roles",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "projects");

            migrationBuilder.DropTable(
                name: "statuses",
                schema: "workflows");

            migrationBuilder.DropTable(
                name: "work_logs",
                schema: "issues");
        }
    }
}
