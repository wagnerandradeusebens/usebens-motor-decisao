using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotorDecisao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "source_configs",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    CacheTtlHours = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_configs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_source_configs_SourceName",
                schema: "usebens_motor_decisao",
                table: "source_configs",
                column: "SourceName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_configs",
                schema: "usebens_motor_decisao");
        }
    }
}
