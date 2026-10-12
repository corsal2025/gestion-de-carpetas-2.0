using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepararDatosImportados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-off, idempotent repair of DETALLE-import artefacts (FechaSubida invented on import day,
            // comuna read from the ATENCION column, "VECINO" surname placeholder). Backup taken beforehand.
            if (!migrationBuilder.IsSqlite()) return;

            migrationBuilder.Sql(@"UPDATE Carpetas SET FechaSubida = NULL
WHERE FechaSubida IS NOT NULL AND FechaSubida = substr(FechaRegistro, 1, 10) AND Estado IN ('SubidaConaset','SubidaConF8','CambioDomSubidoConaset','CambioDomSubidoCorreo','SubidaConOficio')
  AND EXISTS (SELECT 1 FROM HistorialCambios h WHERE h.CarpetaId = Carpetas.Id AND h.Usuario = 'Sistema Importador' AND h.Campo = 'Importacion')
  AND NOT EXISTS (SELECT 1 FROM HistorialCambios h WHERE h.CarpetaId = Carpetas.Id AND h.Campo = 'FechaSubida');");
            migrationBuilder.Sql(@"UPDATE Carpetas SET Comuna = NULL
WHERE Comuna IS NOT NULL
  AND (Estado NOT IN ('CambioDomicilio','CambioDomicilioSolicitado','CambioDomSubidoConaset','CambioDomSubidoCorreo') OR upper(trim(Comuna)) IN ('AV. ARGENTINA','PLACILLA','MERC. PUERTO','MERCADO PUERTO') OR upper(Comuna) LIKE 'SI,%' OR Comuna GLOB '*[0-9]*')
  AND NOT EXISTS (SELECT 1 FROM HistorialCambios h WHERE h.CarpetaId = Carpetas.Id AND h.Campo = 'Comuna');");
            migrationBuilder.Sql(@"UPDATE Ciudadanos SET NombreBusqueda = substr(NombreBusqueda, 1, length(NombreBusqueda) - 7) WHERE Apellido = 'VECINO' AND NombreBusqueda LIKE '% vecino';");
            migrationBuilder.Sql(@"UPDATE Ciudadanos SET Nombre = rtrim(rtrim(rtrim(rtrim(Nombre, replace(Nombre, ' ', ''))), replace(rtrim(rtrim(Nombre, replace(Nombre, ' ', ''))), ' ', ''))), Apellido = substr(rtrim(rtrim(Nombre, replace(Nombre, ' ', ''))), length(rtrim(rtrim(rtrim(Nombre, replace(Nombre, ' ', ''))), replace(rtrim(rtrim(Nombre, replace(Nombre, ' ', ''))), ' ', ''))) + 1) || ' ' || substr(Nombre, length(rtrim(Nombre, replace(Nombre, ' ', ''))) + 1) WHERE Apellido = 'VECINO' AND (length(Nombre) - length(replace(Nombre, ' ', '')) + 1) >= 3;");
            migrationBuilder.Sql(@"UPDATE Ciudadanos SET Nombre = rtrim(rtrim(Nombre, replace(Nombre, ' ', ''))), Apellido = substr(Nombre, length(rtrim(Nombre, replace(Nombre, ' ', ''))) + 1) WHERE Apellido = 'VECINO' AND (length(Nombre) - length(replace(Nombre, ' ', '')) + 1) = 2;");
            migrationBuilder.Sql(@"UPDATE Ciudadanos SET Apellido = 'SIN APELLIDO' WHERE Apellido = 'VECINO';");
            migrationBuilder.Sql(@"UPDATE Ciudadanos SET Nombre = 'SIN NOMBRE' WHERE trim(Nombre) = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible data repair; restore from the pre-repair backup if needed.
        }
    }
}
