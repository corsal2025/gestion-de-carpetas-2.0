using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndiceCarpetasAbiertasPorCiudadano : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carpetas_CiudadanoId",
                table: "Carpetas");

            migrationBuilder.DropIndex(
                name: "IX_Carpetas_Decision",
                table: "Carpetas");

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_CiudadanoId_Decision",
                table: "Carpetas",
                columns: new[] { "CiudadanoId", "Decision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carpetas_CiudadanoId_Decision",
                table: "Carpetas");

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_CiudadanoId",
                table: "Carpetas",
                column: "CiudadanoId");

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_Decision",
                table: "Carpetas",
                column: "Decision");
        }
    }
}
