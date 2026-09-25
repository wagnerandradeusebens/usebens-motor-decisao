using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotorDecisao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RewriteSourceCacheAsProductResponse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O cache é descartável e mudou de granularidade (uma linha por
            // fonte/produto/dado → uma por fonte/produto/chave, com o produto
            // inteiro no payload). Limpa as linhas antigas para o novo índice único
            // (sem Datum) não colidir; a próxima consulta reconstrói o cache.
            migrationBuilder.Sql(@"DELETE FROM usebens_motor_decisao.source_cache;");

            migrationBuilder.DropIndex(
                name: "IX_source_cache_Source_Product_Datum_BusinessKey",
                schema: "usebens_motor_decisao",
                table: "source_cache");

            migrationBuilder.DropColumn(
                name: "Datum",
                schema: "usebens_motor_decisao",
                table: "source_cache");

            migrationBuilder.DropColumn(
                name: "Value",
                schema: "usebens_motor_decisao",
                table: "source_cache");

            migrationBuilder.DropColumn(
                name: "ValueType",
                schema: "usebens_motor_decisao",
                table: "source_cache");

            migrationBuilder.AddColumn<string>(
                name: "Payload",
                schema: "usebens_motor_decisao",
                table: "source_cache",
                type: "character varying(16000)",
                maxLength: 16000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_source_cache_Source_Product_BusinessKey",
                schema: "usebens_motor_decisao",
                table: "source_cache",
                columns: new[] { "Source", "Product", "BusinessKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_source_cache_Source_Product_BusinessKey",
                schema: "usebens_motor_decisao",
                table: "source_cache");

            migrationBuilder.DropColumn(
                name: "Payload",
                schema: "usebens_motor_decisao",
                table: "source_cache");

            migrationBuilder.AddColumn<string>(
                name: "Datum",
                schema: "usebens_motor_decisao",
                table: "source_cache",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Value",
                schema: "usebens_motor_decisao",
                table: "source_cache",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ValueType",
                schema: "usebens_motor_decisao",
                table: "source_cache",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_source_cache_Source_Product_Datum_BusinessKey",
                schema: "usebens_motor_decisao",
                table: "source_cache",
                columns: new[] { "Source", "Product", "Datum", "BusinessKey" },
                unique: true);
        }
    }
}
