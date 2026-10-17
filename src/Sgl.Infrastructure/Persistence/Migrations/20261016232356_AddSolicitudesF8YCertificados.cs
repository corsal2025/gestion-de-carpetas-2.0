using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSolicitudesF8YCertificados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Configuracion",
                columns: table => new
                {
                    Clave = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Valor = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuracion", x => x.Clave);
                });

            migrationBuilder.CreateTable(
                name: "SolicitudesF8",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    CarpetaId = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    EstadoOrigen = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SolicitadoPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SolicitadoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ResueltoPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ResueltoEn = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Observacion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesF8", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SolicitudesF8_Carpetas_CarpetaId",
                        column: x => x.CarpetaId,
                        principalTable: "Carpetas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SolicitudesCertificado",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    CarpetaId = table.Column<Guid>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Origen = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SolicitudF8Id = table.Column<Guid>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    SolicitadoPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SolicitadoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FirmadoPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FirmadoEn = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NumeroOficio = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ParametrosJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    EnviadoADirectorEn = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Observacion = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesCertificado", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SolicitudesCertificado_Carpetas_CarpetaId",
                        column: x => x.CarpetaId,
                        principalTable: "Carpetas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SolicitudesCertificado_SolicitudesF8_SolicitudF8Id",
                        column: x => x.SolicitudF8Id,
                        principalTable: "SolicitudesF8",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesCertificado_CarpetaId_Tipo",
                table: "SolicitudesCertificado",
                columns: new[] { "CarpetaId", "Tipo" },
                unique: true,
                filter: "[Estado] = 'PorFirmar'");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesCertificado_Estado_SolicitadoEn",
                table: "SolicitudesCertificado",
                columns: new[] { "Estado", "SolicitadoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesCertificado_SolicitudF8Id",
                table: "SolicitudesCertificado",
                column: "SolicitudF8Id");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesF8_CarpetaId",
                table: "SolicitudesF8",
                column: "CarpetaId",
                unique: true,
                filter: "[Estado] = 'Solicitado'");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesF8_Estado_SolicitadoEn",
                table: "SolicitudesF8",
                columns: new[] { "Estado", "SolicitadoEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Configuracion");

            migrationBuilder.DropTable(
                name: "SolicitudesCertificado");

            migrationBuilder.DropTable(
                name: "SolicitudesF8");
        }
    }
}
