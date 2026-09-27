using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ciudadanos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Rut = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Apellido = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NombreBusqueda = table.Column<string>(type: "TEXT", maxLength: 201, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ciudadanos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Carpetas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CiudadanoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sede = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FechaCitacion = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FechaSubida = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IdoneidadMoral = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carpetas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Carpetas_Ciudadanos_CiudadanoId",
                        column: x => x.CiudadanoId,
                        principalTable: "Ciudadanos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HistorialCambios",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CarpetaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Usuario = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Campo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Anterior = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    Nuevo = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistorialCambios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistorialCambios_Carpetas_CarpetaId",
                        column: x => x.CarpetaId,
                        principalTable: "Carpetas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_CiudadanoId",
                table: "Carpetas",
                column: "CiudadanoId");

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_Estado",
                table: "Carpetas",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_Sede",
                table: "Carpetas",
                column: "Sede");

            migrationBuilder.CreateIndex(
                name: "IX_Ciudadanos_NombreBusqueda",
                table: "Ciudadanos",
                column: "NombreBusqueda");

            migrationBuilder.CreateIndex(
                name: "IX_Ciudadanos_Rut",
                table: "Ciudadanos",
                column: "Rut",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistorialCambios_CarpetaId",
                table: "HistorialCambios",
                column: "CarpetaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistorialCambios");

            migrationBuilder.DropTable(
                name: "Carpetas");

            migrationBuilder.DropTable(
                name: "Ciudadanos");
        }
    }
}
