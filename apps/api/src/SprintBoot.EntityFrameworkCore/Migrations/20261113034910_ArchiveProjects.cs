using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                schema: "projects",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                schema: "projects",
                table: "projects");
        }
    }
}
