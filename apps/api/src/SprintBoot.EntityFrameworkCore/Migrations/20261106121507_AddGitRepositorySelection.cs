using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddGitRepositorySelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSelected",
                schema: "git",
                table: "repositories",
                type: "boolean",
                nullable: false,
                // Existing repositories stay in use, as they were before spaces could leave any out.
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "RepositoryAccess",
                schema: "git",
                table: "connections",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSelected",
                schema: "git",
                table: "repositories");

            migrationBuilder.DropColumn(
                name: "RepositoryAccess",
                schema: "git",
                table: "connections");
        }
    }
}
