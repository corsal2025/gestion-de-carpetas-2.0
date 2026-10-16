using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sgl.Infrastructure.Persistence;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Peticiones de cambio de domicilio, migradas tal cual desde PeticionCambioDomicilio (peticiones.db).
    /// El DDL es el original: las fechas se guardan como texto ISO, igual que en el sistema de origen.
    /// Se crea con SQL directo para no alterar ningún formato al copiar los datos.
    /// </summary>
    [DbContext(typeof(SglDbContext))]
    [Migration("20261016090000_AddPeticionCambioDomicilio")]
    public partial class AddPeticionCambioDomicilio : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS Peticion (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    NombreCompleto    TEXT NOT NULL,
    Rut               TEXT NOT NULL,
    Comuna            TEXT NOT NULL,
    Clases            TEXT,
    FechaSolicitud    TEXT,
    Origen            TEXT,
    RutInvalido       INTEGER NOT NULL DEFAULT 0,
    Estado            INTEGER NOT NULL DEFAULT 0,
    DetalleEstado     TEXT,
    CreadaEn          TEXT NOT NULL,
    EnviadaEn         TEXT,
    DestinatariosCorreo TEXT,
    Oficina           TEXT,
    OrdenImportacion  INTEGER NOT NULL DEFAULT 0,
    Marcada           INTEGER NOT NULL DEFAULT 0,
    EstadoCarpeta     TEXT NOT NULL DEFAULT 'CAMBIO DE DOMICILIO',
    SubidaEn          TEXT);");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS UX_Peticion_Rut_Comuna ON Peticion (Rut, Comuna);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS UX_Peticion_Rut_Comuna;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS Peticion;");
        }
    }
}
