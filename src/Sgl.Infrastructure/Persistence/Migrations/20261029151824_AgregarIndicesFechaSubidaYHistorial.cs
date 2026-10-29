using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarIndicesFechaSubidaYHistorial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_HistorialCambios_Campo_CarpetaId_Fecha",
                table: "HistorialCambios",
                columns: new[] { "Campo", "CarpetaId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_FechaSubida",
                table: "Carpetas",
                column: "FechaSubida");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HistorialCambios_Campo_CarpetaId_Fecha",
                table: "HistorialCambios");

            migrationBuilder.DropIndex(
                name: "IX_Carpetas_FechaSubida",
                table: "Carpetas");
        }
    }
}
