using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AsistenciaDelDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EsExtra",
                table: "Carpetas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Asistencias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Fecha = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CarpetaId = table.Column<Guid>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    Rut = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Sede = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RegistradaEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Autor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Anulada = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnuladaPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AnuladaEn = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asistencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Asistencias_Carpetas_CarpetaId",
                        column: x => x.CarpetaId,
                        principalTable: "Carpetas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Asistencias_CarpetaId",
                table: "Asistencias",
                column: "CarpetaId");

            migrationBuilder.CreateIndex(
                name: "IX_Asistencias_Fecha_Tipo",
                table: "Asistencias",
                columns: new[] { "Fecha", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_Asistencias_Rut_Fecha",
                table: "Asistencias",
                columns: new[] { "Rut", "Fecha" },
                unique: true,
                filter: "\"Anulada\" = 0 AND \"Tipo\" <> 'PorAgregar'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Asistencias");

            migrationBuilder.DropColumn(
                name: "EsExtra",
                table: "Carpetas");
        }
    }
}
