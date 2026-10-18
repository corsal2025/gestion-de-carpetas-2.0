using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SincronizacionCorreoCambioDomicilio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorreosCambioDomicilio",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Rut = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    CarpetaId = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    ProcesadoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorreosCambioDomicilio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CorreosCambioDomicilio_Carpetas_CarpetaId",
                        column: x => x.CarpetaId,
                        principalTable: "Carpetas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorreosCambioDomicilio_CarpetaId",
                table: "CorreosCambioDomicilio",
                column: "CarpetaId");

            migrationBuilder.CreateIndex(
                name: "IX_CorreosCambioDomicilio_MessageId_Rut",
                table: "CorreosCambioDomicilio",
                columns: new[] { "MessageId", "Rut" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorreosCambioDomicilio");
        }
    }
}
