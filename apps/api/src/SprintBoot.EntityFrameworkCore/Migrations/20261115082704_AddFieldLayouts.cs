using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_field_layouts",
                schema: "tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<List<string>>(type: "text[]", nullable: false),
                    Hidden = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_field_layouts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_project_field_layouts_ProjectId",
                schema: "tickets",
                table: "project_field_layouts",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_field_layouts",
                schema: "tickets");
        }
    }
}
