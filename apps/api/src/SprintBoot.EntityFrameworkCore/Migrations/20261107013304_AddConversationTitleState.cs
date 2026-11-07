using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationTitleState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TitleState",
                schema: "assistant",
                table: "conversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TitleState",
                schema: "assistant",
                table: "conversations",
                column: "TitleState");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversations_TitleState",
                schema: "assistant",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "TitleState",
                schema: "assistant",
                table: "conversations");
        }
    }
}
