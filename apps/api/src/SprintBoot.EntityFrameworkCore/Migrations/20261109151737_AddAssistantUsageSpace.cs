using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantUsageSpace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                schema: "assistant",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                schema: "assistant",
                table: "conversation_actions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                schema: "assistant",
                table: "conversation_actions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TicketsCreated",
                schema: "assistant",
                table: "conversation_actions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_ProjectId",
                schema: "assistant",
                table: "conversations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_actions_ProjectId_CreatedAt",
                schema: "assistant",
                table: "conversation_actions",
                columns: new[] { "ProjectId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversations_ProjectId",
                schema: "assistant",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversation_actions_ProjectId_CreatedAt",
                schema: "assistant",
                table: "conversation_actions");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "assistant",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "assistant",
                table: "conversation_actions");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "assistant",
                table: "conversation_actions");

            migrationBuilder.DropColumn(
                name: "TicketsCreated",
                schema: "assistant",
                table: "conversation_actions");
        }
    }
}
