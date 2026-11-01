using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "agile");

            migrationBuilder.AlterColumn<string>(
                name: "Rank",
                schema: "issues",
                table: "issues",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateTable(
                name: "board_column_statuses",
                schema: "agile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ColumnId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatusId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_board_column_statuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "board_columns",
                schema: "agile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    MinLimit = table.Column<int>(type: "integer", nullable: true),
                    MaxLimit = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_board_columns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "board_entries",
                schema: "agile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_board_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "boards",
                schema: "agile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    FilterConfig = table.Column<string>(type: "text", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sprint_issues",
                schema: "agile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SprintId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sprint_issues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sprints",
                schema: "agile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Goal = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sprints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_issues_ProjectId_Rank",
                schema: "issues",
                table: "issues",
                columns: new[] { "ProjectId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_board_column_statuses_ColumnId_StatusId",
                schema: "agile",
                table: "board_column_statuses",
                columns: new[] { "ColumnId", "StatusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_board_columns_BoardId",
                schema: "agile",
                table: "board_columns",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_board_entries_BoardId_IssueId",
                schema: "agile",
                table: "board_entries",
                columns: new[] { "BoardId", "IssueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_boards_ProjectId",
                schema: "agile",
                table: "boards",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sprint_issues_IssueId",
                schema: "agile",
                table: "sprint_issues",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_sprint_issues_SprintId",
                schema: "agile",
                table: "sprint_issues",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_sprints_one_active",
                schema: "agile",
                table: "sprints",
                column: "BoardId",
                unique: true,
                filter: "\"State\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "board_column_statuses",
                schema: "agile");

            migrationBuilder.DropTable(
                name: "board_columns",
                schema: "agile");

            migrationBuilder.DropTable(
                name: "board_entries",
                schema: "agile");

            migrationBuilder.DropTable(
                name: "boards",
                schema: "agile");

            migrationBuilder.DropTable(
                name: "sprint_issues",
                schema: "agile");

            migrationBuilder.DropTable(
                name: "sprints",
                schema: "agile");

            migrationBuilder.DropIndex(
                name: "IX_issues_ProjectId_Rank",
                schema: "issues",
                table: "issues");

            migrationBuilder.AlterColumn<string>(
                name: "Rank",
                schema: "issues",
                table: "issues",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldCollation: "C");
        }
    }
}
