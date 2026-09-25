using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotorDecisao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "source_cache",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Product = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Datum = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BusinessKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ValueType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_cache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_source_cache_Source_Product_Datum_BusinessKey",
                schema: "usebens_motor_decisao",
                table: "source_cache",
                columns: new[] { "Source", "Product", "Datum", "BusinessKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_cache",
                schema: "usebens_motor_decisao");
        }
    }
}
