using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class ScopeGitConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Owner",
                schema: "git",
                table: "connections",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                schema: "git",
                table: "connections",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_connections_ProjectId",
                schema: "git",
                table: "connections",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_connections_ProjectId",
                schema: "git",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "Owner",
                schema: "git",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "git",
                table: "connections");
        }
    }
}
