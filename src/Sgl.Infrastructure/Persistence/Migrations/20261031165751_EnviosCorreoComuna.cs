using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnviosCorreoComuna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "RevisadaEn",
                table: "Carpetas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EnviosCorreoComuna",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Comuna = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Destinatarios = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Asunto = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Cuerpo = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Autor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EnviadoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EsPrueba = table.Column<bool>(type: "INTEGER", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Nota = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PantallazoArchivo = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PantallazoEn = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnviosCorreoComuna", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlantillasCorreo",
                columns: table => new
                {
                    Clave = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Asunto = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Cuerpo = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlantillasCorreo", x => x.Clave);
                });

            migrationBuilder.CreateTable(
                name: "EnviosCorreoComunaCarpetas",
                columns: table => new
                {
                    EnvioId = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    CarpetaId = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnviosCorreoComunaCarpetas", x => new { x.EnvioId, x.CarpetaId });
                    table.ForeignKey(
                        name: "FK_EnviosCorreoComunaCarpetas_Carpetas_CarpetaId",
                        column: x => x.CarpetaId,
                        principalTable: "Carpetas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnviosCorreoComunaCarpetas_EnviosCorreoComuna_EnvioId",
                        column: x => x.EnvioId,
                        principalTable: "EnviosCorreoComuna",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnviosCorreoComuna_Estado",
                table: "EnviosCorreoComuna",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_EnviosCorreoComuna_Tipo_EnviadoEn",
                table: "EnviosCorreoComuna",
                columns: new[] { "Tipo", "EnviadoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_EnviosCorreoComunaCarpetas_CarpetaId",
                table: "EnviosCorreoComunaCarpetas",
                column: "CarpetaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnviosCorreoComunaCarpetas");

            migrationBuilder.DropTable(
                name: "PlantillasCorreo");

            migrationBuilder.DropTable(
                name: "EnviosCorreoComuna");

            migrationBuilder.DropColumn(
                name: "RevisadaEn",
                table: "Carpetas");
        }
    }
}
