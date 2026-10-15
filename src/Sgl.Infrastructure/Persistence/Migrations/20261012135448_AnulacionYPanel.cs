using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnulacionYPanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Anulada",
                table: "Carpetas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AnuladaEn",
                table: "Carpetas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnuladaPor",
                table: "Carpetas",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoAnulacion",
                table: "Carpetas",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistorialCambios_Fecha",
                table: "HistorialCambios",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_Anulada",
                table: "Carpetas",
                column: "Anulada");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HistorialCambios_Fecha",
                table: "HistorialCambios");

            migrationBuilder.DropIndex(
                name: "IX_Carpetas_Anulada",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "Anulada",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "AnuladaEn",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "AnuladaPor",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "MotivoAnulacion",
                table: "Carpetas");
        }
    }
}
