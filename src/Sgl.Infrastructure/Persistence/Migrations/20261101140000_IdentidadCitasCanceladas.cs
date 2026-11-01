using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sgl.Infrastructure.Persistence.Migrations;

[DbContext(typeof(SglDbContext))]
[Migration("20261101140000_IdentidadCitasCanceladas")]
public sealed class IdentidadCitasCanceladas : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_CitasCanceladas_FechaTomaCita_FechaCita_HoraCita_Sede_Tramite", table: "CitasCanceladas");
        migrationBuilder.AddColumn<string>(name: "IdentidadAgenda", table: "CitasCanceladas", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.CreateIndex(name: "IX_CitasCanceladas_IdentidadAgenda", table: "CitasCanceladas", column: "IdentidadAgenda", unique: true);
        migrationBuilder.CreateIndex(name: "IX_CitasCanceladas_FechaTomaCita_FechaCita_HoraCita_Sede_Tramite", table: "CitasCanceladas", columns: new[] { "FechaTomaCita", "FechaCita", "HoraCita", "Sede", "Tramite" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // New records may share the old key. Reverting the old uniqueness would discard valid statistics.
        throw new NotSupportedException("No se revierte una migración que preserva cancelaciones distintas con el mismo horario. Restaure un respaldo anterior.");
    }
}
