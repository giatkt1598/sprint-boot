using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "custom_fields",
                schema: "tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    DefaultValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_custom_fields", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "custom_field_options",
                schema: "tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_field_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_field_options_custom_fields_FieldId",
                        column: x => x.FieldId,
                        principalSchema: "tickets",
                        principalTable: "custom_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_custom_fields",
                schema: "tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    TicketTypeIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_custom_fields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_custom_fields_custom_fields_FieldId",
                        column: x => x.FieldId,
                        principalSchema: "tickets",
                        principalTable: "custom_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_field_values",
                schema: "tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NumberValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoolValue = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_field_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_field_values_custom_field_options_OptionId",
                        column: x => x.OptionId,
                        principalSchema: "tickets",
                        principalTable: "custom_field_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ticket_field_values_custom_fields_FieldId",
                        column: x => x.FieldId,
                        principalSchema: "tickets",
                        principalTable: "custom_fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_field_values_tickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "tickets",
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_options_FieldId",
                schema: "tickets",
                table: "custom_field_options",
                column: "FieldId");

            migrationBuilder.CreateIndex(
                name: "IX_custom_fields_ProjectId_Key",
                schema: "tickets",
                table: "custom_fields",
                columns: new[] { "ProjectId", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_project_custom_fields_FieldId",
                schema: "tickets",
                table: "project_custom_fields",
                column: "FieldId");

            migrationBuilder.CreateIndex(
                name: "IX_project_custom_fields_ProjectId_FieldId",
                schema: "tickets",
                table: "project_custom_fields",
                columns: new[] { "ProjectId", "FieldId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_field_values_FieldId_DateValue",
                schema: "tickets",
                table: "ticket_field_values",
                columns: new[] { "FieldId", "DateValue" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_field_values_FieldId_NumberValue",
                schema: "tickets",
                table: "ticket_field_values",
                columns: new[] { "FieldId", "NumberValue" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_field_values_FieldId_OptionId",
                schema: "tickets",
                table: "ticket_field_values",
                columns: new[] { "FieldId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_field_values_FieldId_UserId",
                schema: "tickets",
                table: "ticket_field_values",
                columns: new[] { "FieldId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_field_values_OptionId",
                schema: "tickets",
                table: "ticket_field_values",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_field_values_TicketId_FieldId",
                schema: "tickets",
                table: "ticket_field_values",
                columns: new[] { "TicketId", "FieldId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_custom_fields",
                schema: "tickets");

            migrationBuilder.DropTable(
                name: "ticket_field_values",
                schema: "tickets");

            migrationBuilder.DropTable(
                name: "custom_field_options",
                schema: "tickets");

            migrationBuilder.DropTable(
                name: "custom_fields",
                schema: "tickets");
        }
    }
}
