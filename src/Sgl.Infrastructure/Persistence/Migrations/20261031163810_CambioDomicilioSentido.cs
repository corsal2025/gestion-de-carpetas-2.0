using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CambioDomicilioSentido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SentidoCambioDomicilio",
                table: "Carpetas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_SentidoCambioDomicilio_Estado",
                table: "Carpetas",
                columns: new[] { "SentidoCambioDomicilio", "Estado" });

            // Backfill only fills the new column (2 = Entrante, 1 = Saliente); no row is added, removed or moved of estado.
            // A carpeta born from the mail sync (it has a row in CorreosCambioDomicilio) is entrante whatever its estado now is;
            // any other carpeta still in the change-of-address family is saliente (the estados are stored by name).
            migrationBuilder.Sql(
                "UPDATE \"Carpetas\" SET \"SentidoCambioDomicilio\" = 2 " +
                "WHERE \"Id\" IN (SELECT \"CarpetaId\" FROM \"CorreosCambioDomicilio\");");
            migrationBuilder.Sql(
                "UPDATE \"Carpetas\" SET \"SentidoCambioDomicilio\" = 1 " +
                "WHERE \"SentidoCambioDomicilio\" IS NULL " +
                "AND \"Estado\" IN ('CambioDomicilio', 'CambioDomicilioSolicitado', 'CambioDomSubidoConaset', 'CambioDomSubidoCorreo');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carpetas_SentidoCambioDomicilio_Estado",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "SentidoCambioDomicilio",
                table: "Carpetas");
        }
    }
}
