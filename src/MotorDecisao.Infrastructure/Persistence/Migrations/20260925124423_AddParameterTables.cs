using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotorDecisao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParameterTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "global_parameter_tables",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ColumnsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RowsJson = table.Column<string>(type: "jsonb", nullable: false),
                    KeyColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MinColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MaxColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DefaultValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_global_parameter_tables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "parameter_tables",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ColumnsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RowsJson = table.Column<string>(type: "jsonb", nullable: false),
                    KeyColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MinColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MaxColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DefaultValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parameter_tables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parameter_tables_flow_versions_FlowVersionId",
                        column: x => x.FlowVersionId,
                        principalSchema: "usebens_motor_decisao",
                        principalTable: "flow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_global_parameter_tables_Name",
                schema: "usebens_motor_decisao",
                table: "global_parameter_tables",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parameter_tables_FlowVersionId_Name",
                schema: "usebens_motor_decisao",
                table: "parameter_tables",
                columns: new[] { "FlowVersionId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "global_parameter_tables",
                schema: "usebens_motor_decisao");

            migrationBuilder.DropTable(
                name: "parameter_tables",
                schema: "usebens_motor_decisao");
        }
    }
}
