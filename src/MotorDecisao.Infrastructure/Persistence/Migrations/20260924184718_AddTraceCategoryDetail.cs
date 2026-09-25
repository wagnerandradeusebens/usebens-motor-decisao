using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotorDecisao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTraceCategoryDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "usebens_motor_decisao",
                table: "execution_traces",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Fluxo");

            migrationBuilder.AddColumn<string>(
                name: "Detail",
                schema: "usebens_motor_decisao",
                table: "execution_traces",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                schema: "usebens_motor_decisao",
                table: "execution_traces");

            migrationBuilder.DropColumn(
                name: "Detail",
                schema: "usebens_motor_decisao",
                table: "execution_traces");
        }
    }
}
