using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClaseLicenciaYDuplicados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClasesLicencia",
                table: "Carpetas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaRegistro",
                table: "Carpetas",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill: existing carpetas take the date of their first audit entry (registration/import).
            migrationBuilder.Sql(
                "UPDATE Carpetas SET FechaRegistro = COALESCE(" +
                "(SELECT MIN(h.Fecha) FROM HistorialCambios h WHERE h.CarpetaId = Carpetas.Id), FechaRegistro);");

            migrationBuilder.CreateTable(
                name: "RevisionesDuplicado",
                columns: table => new
                {
                    CiudadanoId = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    RevisadoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevisadoPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisionesDuplicado", x => x.CiudadanoId);
                    table.ForeignKey(
                        name: "FK_RevisionesDuplicado_Ciudadanos_CiudadanoId",
                        column: x => x.CiudadanoId,
                        principalTable: "Ciudadanos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_Decision",
                table: "Carpetas",
                column: "Decision");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RevisionesDuplicado");

            migrationBuilder.DropIndex(
                name: "IX_Carpetas_Decision",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "ClasesLicencia",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "FechaRegistro",
                table: "Carpetas");
        }
    }
}
