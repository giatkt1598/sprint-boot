using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreationTime",
                schema: "workflows",
                table: "statuses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatorId",
                schema: "workflows",
                table: "statuses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModificationTime",
                schema: "workflows",
                table: "statuses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastModifierId",
                schema: "workflows",
                table: "statuses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "workflows",
                table: "statuses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "project_workflow_mappings",
                schema: "workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_workflow_mappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transition_rules",
                schema: "workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    RuleKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transition_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transitions",
                schema: "workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FromStatusId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToStatusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_migrations",
                schema: "workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeJson = table.Column<string>(type: "text", nullable: false),
                    ToVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MappingJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Processed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_migrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_steps",
                schema: "workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_steps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_versions",
                schema: "workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    InitialStatusId = table.Column<Guid>(type: "uuid", nullable: false),
                    LayoutJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflows",
                schema: "workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_project_workflow_mappings_ProjectId_IssueTypeId",
                schema: "workflows",
                table: "project_workflow_mappings",
                columns: new[] { "ProjectId", "IssueTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transition_rules_TransitionId",
                schema: "workflows",
                table: "transition_rules",
                column: "TransitionId");

            migrationBuilder.CreateIndex(
                name: "IX_transitions_VersionId",
                schema: "workflows",
                table: "transitions",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_State_NextAttemptAt",
                schema: "workflows",
                table: "webhook_deliveries",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_migrations_State",
                schema: "workflows",
                table: "workflow_migrations",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_steps_VersionId_StatusId",
                schema: "workflows",
                table: "workflow_steps",
                columns: new[] { "VersionId", "StatusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_versions_WorkflowId_VersionNumber",
                schema: "workflows",
                table: "workflow_versions",
                columns: new[] { "WorkflowId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_workflow_mappings",
                schema: "workflows");

            migrationBuilder.DropTable(
                name: "transition_rules",
                schema: "workflows");

            migrationBuilder.DropTable(
                name: "transitions",
                schema: "workflows");

            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "workflows");

            migrationBuilder.DropTable(
                name: "workflow_migrations",
                schema: "workflows");

            migrationBuilder.DropTable(
                name: "workflow_steps",
                schema: "workflows");

            migrationBuilder.DropTable(
                name: "workflow_versions",
                schema: "workflows");

            migrationBuilder.DropTable(
                name: "workflows",
                schema: "workflows");

            migrationBuilder.DropColumn(
                name: "CreationTime",
                schema: "workflows",
                table: "statuses");

            migrationBuilder.DropColumn(
                name: "CreatorId",
                schema: "workflows",
                table: "statuses");

            migrationBuilder.DropColumn(
                name: "LastModificationTime",
                schema: "workflows",
                table: "statuses");

            migrationBuilder.DropColumn(
                name: "LastModifierId",
                schema: "workflows",
                table: "statuses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "workflows",
                table: "statuses");
        }
    }
}
