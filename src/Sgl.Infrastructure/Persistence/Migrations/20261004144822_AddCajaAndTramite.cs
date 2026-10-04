using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCajaAndTramite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CajaArchivo",
                table: "Carpetas",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FechaUltimaCarpeta",
                table: "Carpetas",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoTramite",
                table: "Carpetas",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_CajaArchivo",
                table: "Carpetas",
                column: "CajaArchivo");

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_TipoTramite",
                table: "Carpetas",
                column: "TipoTramite");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carpetas_CajaArchivo",
                table: "Carpetas");

            migrationBuilder.DropIndex(
                name: "IX_Carpetas_TipoTramite",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "CajaArchivo",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "FechaUltimaCarpeta",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "TipoTramite",
                table: "Carpetas");
        }
    }
}
