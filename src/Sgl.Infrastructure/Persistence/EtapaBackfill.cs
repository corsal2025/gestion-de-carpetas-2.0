using Microsoft.EntityFrameworkCore;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>
/// One-time, idempotent fill of the phase-3 dates for carpetas created before they existed. Only system-recorded
/// facts are used (the audit trail, FechaSubida typed or imported); nothing is invented:
/// <list type="bullet">
/// <item><c>FechaEstadoActual</c>: last audited change of state; else the first audit entry (when it entered the
/// system); else the registration time.</item>
/// <item><c>FechaCierre</c>: for final decisions, the audited decision date; for closing states, the audited move
/// into that state; otherwise the upload date already stored. Left empty when none of those exists.</item>
/// </list>
/// </summary>
public static class EtapaBackfill
{
    private const int TamanoLote = 500;

    /// <summary>Carpetas still without a stage date (stored as the minimum DateTime by the migration).</summary>
    public static Task<int> PendientesAsync(SglDbContext db, CancellationToken ct = default) =>
        db.Carpetas.IgnoreQueryFilters().CountAsync(c => c.FechaEstadoActual == DateTime.MinValue, ct);

    /// <returns>Number of carpetas filled.</returns>
    public static async Task<int> EjecutarAsync(SglDbContext db, DateTime ahoraUtc, CancellationToken ct = default)
    {
        var estado = nameof(Carpeta.Estado);
        var decision = nameof(Carpeta.Decision);

        var ultimoEstado = await db.Historial.AsNoTracking()
            .Where(h => h.Campo == estado)
            .GroupBy(h => h.CarpetaId)
            .Select(g => new { g.Key, Fecha = g.Max(h => h.Fecha) })
            .ToDictionaryAsync(x => x.Key, x => x.Fecha, ct);
        var primerMovimiento = await db.Historial.AsNoTracking()
            .GroupBy(h => h.CarpetaId)
            .Select(g => new { g.Key, Fecha = g.Min(h => h.Fecha) })
            .ToDictionaryAsync(x => x.Key, x => x.Fecha, ct);
        var otorgado = nameof(Domain.Decision.Otorgado);
        var denegado = nameof(Domain.Decision.Denegado);
        var fechaDecision = await db.Historial.AsNoTracking()
            .Where(h => h.Campo == decision && (h.Nuevo == otorgado || h.Nuevo == denegado))
            .GroupBy(h => h.CarpetaId)
            .Select(g => new { g.Key, Fecha = g.Max(h => h.Fecha) })
            .ToDictionaryAsync(x => x.Key, x => x.Fecha, ct);

        var total = 0;
        while (true)
        {
            var lote = await db.Carpetas.IgnoreQueryFilters()
                .Where(c => c.FechaEstadoActual == DateTime.MinValue)
                .OrderBy(c => c.Id)
                .Take(TamanoLote)
                .ToListAsync(ct);
            if (lote.Count == 0)
            {
                break;
            }

            foreach (var c in lote)
            {
                var etapa = ultimoEstado.TryGetValue(c.Id, out var ue) ? ue
                    : primerMovimiento.TryGetValue(c.Id, out var pm) ? pm
                    : c.FechaRegistro;
                // Last resort so a row can never stay pending (and loop): the moment the backfill ran.
                if (etapa == DateTime.MinValue) etapa = ahoraUtc;
                db.Entry(c).Property(nameof(Carpeta.FechaEstadoActual)).CurrentValue = etapa;

                if (c.FechaCierre is null)
                {
                    DateOnly? cierre = null;
                    if (c.Decision != Domain.Decision.Pendiente)
                    {
                        cierre = fechaDecision.TryGetValue(c.Id, out var fd) ? HoraChile.Fecha(fd) : c.FechaSubida;
                    }
                    else if (Carpeta.EsEstadoCierre(c.Estado))
                    {
                        cierre = ultimoEstado.TryGetValue(c.Id, out var fe) ? HoraChile.Fecha(fe) : c.FechaSubida;
                    }

                    db.Entry(c).Property(nameof(Carpeta.FechaCierre)).CurrentValue = cierre;
                }
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total += lote.Count;
        }

        return total;
    }
}
