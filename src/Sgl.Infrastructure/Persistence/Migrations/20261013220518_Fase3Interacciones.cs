using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fase3Interacciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaCierre",
                table: "Carpetas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEstadoActual",
                table: "Carpetas",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaImpresion",
                table: "Carpetas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IntegridadCorridas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Origen = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Problemas = table.Column<int>(type: "INTEGER", nullable: false),
                    Fallo = table.Column<bool>(type: "INTEGER", nullable: false),
                    DuracionMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Detalle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegridadCorridas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntegridadCorridas_Fecha",
                table: "IntegridadCorridas",
                column: "Fecha");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegridadCorridas");

            migrationBuilder.DropColumn(
                name: "FechaCierre",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "FechaEstadoActual",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "FechaImpresion",
                table: "Carpetas");
        }
    }
}
