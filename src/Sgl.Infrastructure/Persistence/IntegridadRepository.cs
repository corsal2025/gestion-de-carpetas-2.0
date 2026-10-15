using Microsoft.EntityFrameworkCore;
using Sgl.Application.Integridad;
using Sgl.Application.Panel;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>
/// Gathers the watchdog input from the same pipelines the screens use (tray predicates, grouped counters, home
/// panel), so a divergence between them is detected instead of assumed away.
/// </summary>
public sealed class IntegridadRepository(SglDbContext db, IPanelQuery panel) : IIntegridadRepository
{
    public async Task<EntradaIntegridad> ObtenerEntradaAsync(CancellationToken ct = default)
    {
        var carpetas = await db.Carpetas.IgnoreQueryFilters().AsNoTracking()
            .Select(c => new CarpetaIntegridad(
                c.Id, c.CiudadanoId, c.Ciudadano.Rut.Value, c.Estado, c.Decision, c.CajaArchivo, c.Clases, c.Anulada,
                c.FechaCitacion, c.FechaSubida, c.FechaCierre, c.FechaRegistro))
            .ToListAsync(ct);

        // Trays as the module screens see them: one predicate per tray over the (soft-delete filtered) set.
        var visibles = new Dictionary<Guid, List<Bandeja>>();
        foreach (var bandeja in Enum.GetValues<Bandeja>())
        {
            var ids = await db.Carpetas.AsNoTracking().Where(Carpeta.EnBandeja(bandeja)).Select(c => c.Id).ToListAsync(ct);
            foreach (var id in ids)
            {
                if (!visibles.TryGetValue(id, out var lista))
                {
                    lista = [];
                    visibles[id] = lista;
                }
                lista.Add(bandeja);
            }
        }

        var grupos = await db.Carpetas.AsNoTracking()
            .GroupBy(Carpeta.BandejaExpresion)
            .Select(g => new { g.Key, Total = g.Count() })
            .ToListAsync(ct);
        var conteo = Enum.GetValues<Bandeja>().ToDictionary(b => b, b => grupos.FirstOrDefault(g => g.Key == b)?.Total ?? 0);
        var total = await db.Carpetas.AsNoTracking().CountAsync(ct);

        var panelGrupos = await panel.GruposAsync(ct);
        var conteoPanel = Enum.GetValues<Bandeja>()
            .ToDictionary(b => b, b => panelGrupos.Where(g => g.Bandeja == b).Sum(g => g.Cantidad));

        var revisiones = await db.RevisionesDuplicado.AsNoTracking()
            .ToDictionaryAsync(r => r.CiudadanoId, r => r.RevisadoEn, ct);

        return new EntradaIntegridad(
            carpetas,
            visibles.ToDictionary(p => p.Key, p => (IReadOnlyList<Bandeja>)p.Value),
            conteo,
            conteoPanel,
            total,
            revisiones);
    }

    public async Task GuardarCorridaAsync(IntegridadCorrida corrida, CancellationToken ct = default)
    {
        db.IntegridadCorridas.Add(corrida);
        await db.SaveChangesAsync(ct);
    }

    public Task<IntegridadCorrida?> UltimaCorridaAsync(CancellationToken ct = default) =>
        db.IntegridadCorridas.AsNoTracking().OrderByDescending(c => c.Fecha).ThenByDescending(c => c.Id).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<IntegridadCorrida>> UltimasCorridasAsync(int cantidad, CancellationToken ct = default) =>
        await db.IntegridadCorridas.AsNoTracking().OrderByDescending(c => c.Fecha).ThenByDescending(c => c.Id)
            .Take(Math.Max(1, cantidad)).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, ProblemaIntegridadDto>> IdentificarAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ProblemaIntegridadDto>();
        }

        var filas = new List<ProblemaIntegridadDto>();
        // Chunked so a large number of affected carpetas never exceeds SQLite's parameter limit.
        foreach (var lote in ids.Chunk(400))
        {
            var parte = await db.Carpetas.IgnoreQueryFilters().AsNoTracking()
                .Where(c => lote.Contains(c.Id))
                .Select(c => new { c.Id, Rut = c.Ciudadano.Rut.Value, c.Ciudadano.Nombre, c.Ciudadano.Apellido, c.Sede, c.Estado })
                .ToListAsync(ct);
            filas.AddRange(parte.Select(c => new ProblemaIntegridadDto(
                ReglaIntegridad.UnaSolaBandeja, string.Empty, c.Id,
                Rut.TryParse(c.Rut, out var rut) ? rut!.Formatted : c.Rut,
                $"{c.Nombre} {c.Apellido}".Trim(), c.Sede, c.Estado)));
        }

        return filas.ToDictionary(f => f.CarpetaId!.Value);
    }
}
