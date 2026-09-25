using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotorDecisao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalVariables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "global_variables",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Expression = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_global_variables", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_global_variables_Key",
                schema: "usebens_motor_decisao",
                table: "global_variables",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "global_variables",
                schema: "usebens_motor_decisao");
        }
    }
}
