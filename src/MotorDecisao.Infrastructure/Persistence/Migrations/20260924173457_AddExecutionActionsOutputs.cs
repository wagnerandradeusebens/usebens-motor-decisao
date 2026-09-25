using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotorDecisao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionActionsOutputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Justifications",
                schema: "usebens_motor_decisao",
                table: "decision_executions",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "Limit",
                schema: "usebens_motor_decisao",
                table: "decision_executions",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Outputs",
                schema: "usebens_motor_decisao",
                table: "decision_executions",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Justifications",
                schema: "usebens_motor_decisao",
                table: "decision_executions");

            migrationBuilder.DropColumn(
                name: "Limit",
                schema: "usebens_motor_decisao",
                table: "decision_executions");

            migrationBuilder.DropColumn(
                name: "Outputs",
                schema: "usebens_motor_decisao",
                table: "decision_executions");
        }
    }
}
