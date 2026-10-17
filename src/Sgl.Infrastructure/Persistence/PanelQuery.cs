using Microsoft.EntityFrameworkCore;
using Sgl.Application.Panel;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>
/// Home panel read model. Every aggregation runs in SQL; the soft-delete filter keeps cancelled carpetas out,
/// except for the explicit "Anuladas" counters.
/// </summary>
public sealed class PanelQuery(SglDbContext db) : IPanelQuery
{
    private static readonly string[] CamposEntrada =
        ["Agenda", "Registro", nameof(Carpeta.Estado), nameof(Carpeta.CajaArchivo)];

    public async Task<IReadOnlyList<GrupoBandejaDto>> GruposAsync(CancellationToken ct = default)
    {
        // One GROUP BY over the columns that decide the tray; the tray itself is derived in memory.
        var filas = await db.Carpetas.AsNoTracking()
            .GroupBy(c => new
            {
                c.Estado,
                c.Decision,
                EnCajaReal = c.CajaArchivo != null && c.CajaArchivo.Trim() != "" && c.CajaArchivo != Carpeta.ColaDeCaja,
                c.FechaCitacion,
                Pedida = c.FechaPeticion != null,
            })
            .Select(g => new { g.Key.Estado, g.Key.Decision, g.Key.EnCajaReal, g.Key.FechaCitacion, g.Key.Pedida, N = g.Count() })
            .ToListAsync(ct);

        return filas.Select(f => new GrupoBandejaDto(f.Estado, f.Decision, f.EnCajaReal, f.FechaCitacion, f.N, f.Pedida)).ToList();
    }

    public async Task<IReadOnlyList<MovimientoPanelDto>> MovimientosDesdeAsync(DateTime desdeUtc, CancellationToken ct = default) =>
        await db.Historial.AsNoTracking()
            .Where(h => h.Fecha >= desdeUtc && CamposEntrada.Contains(h.Campo))
            .Where(h => db.Carpetas.Any(c => c.Id == h.CarpetaId))
            .Select(h => new MovimientoPanelDto(h.Fecha, h.Campo, h.Anterior, h.Nuevo))
            .ToListAsync(ct);

    public Task<int> PendientesPorClasificarAsync(CancellationToken ct = default) =>
        db.Carpetas.AsNoTracking().CountAsync(c => c.Estado == EstadoCarpeta.PorClasificar, ct);

    public Task<int> AlertasDuplicadoAsync(CancellationToken ct = default)
    {
        var abiertas = db.Carpetas.AsNoTracking().Where(Carpeta.AbiertaSinArchivarExpresion);
        return db.Carpetas.AsNoTracking()
            .Where(c => c.Estado == EstadoCarpeta.PorClasificar)
            .CountAsync(c => abiertas.Any(o => o.CiudadanoId == c.CiudadanoId && o.Id != c.Id), ct);
    }

    public Task<int> AnuladasAsync(CancellationToken ct = default) =>
        db.Carpetas.IgnoreQueryFilters().AsNoTracking().CountAsync(c => c.Anulada, ct);

    public async Task<IReadOnlyList<DateTime>> FechasAnulacionDesdeAsync(DateTime desdeUtc, CancellationToken ct = default) =>
        await db.Carpetas.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Anulada && c.AnuladaEn >= desdeUtc)
            .Select(c => c.AnuladaEn!.Value)
            .ToListAsync(ct);
}
