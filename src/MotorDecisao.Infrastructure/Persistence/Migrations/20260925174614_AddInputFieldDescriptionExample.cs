using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotorDecisao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInputFieldDescriptionExample : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "usebens_motor_decisao",
                table: "input_fields",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Example",
                schema: "usebens_motor_decisao",
                table: "input_fields",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                schema: "usebens_motor_decisao",
                table: "input_fields");

            migrationBuilder.DropColumn(
                name: "Example",
                schema: "usebens_motor_decisao",
                table: "input_fields");
        }
    }
}
