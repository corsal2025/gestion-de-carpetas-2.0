using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

public sealed class DuplicadoRepository(SglDbContext db) : IDuplicadoRepository
{
    /// <summary>
    /// Citizens with more than one open carpeta, excluding those reviewed after their latest open
    /// carpeta was registered. Filtering, counting and paging run in the database.
    /// </summary>
    public async Task<(IReadOnlyList<DuplicadoGrupoDto> Items, int Total)> SearchAsync(
        DuplicadoSearch search, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var abiertas = db.Carpetas.AsNoTracking().Where(c => c.Decision == Decision.Pendiente);

        // Single GROUP BY CiudadanoId HAVING COUNT(*) > 1 over open carpetas; per-citizen lookups
        // afterwards use IX_Carpetas_CiudadanoId_Decision.
        var grupos = abiertas
            .GroupBy(c => c.CiudadanoId)
            .Where(g => g.Count() > 1)
            .Select(g => new { CiudadanoId = g.Key, Cantidad = g.Count(), UltimoRegistro = g.Max(c => c.FechaRegistro) });

        var query =
            from g in grupos
            join ci in db.Ciudadanos.AsNoTracking() on g.CiudadanoId equals ci.Id
            where !db.RevisionesDuplicado.Any(r => r.CiudadanoId == g.CiudadanoId && r.RevisadoEn >= g.UltimoRegistro)
            select new { g.CiudadanoId, g.Cantidad, Rut = ci.Rut.Value, ci.Nombre, ci.Apellido, ci.NombreBusqueda };

        if (search.Sede is { } sede)
        {
            query = query.Where(x => abiertas.Any(c => c.CiudadanoId == x.CiudadanoId && c.Sede == sede));
        }

        if (search.RutFragment is { } rut)
        {
            query = query.Where(x => x.Rut.Contains(rut));
        }

        if (search.NameFragment is { } name)
        {
            query = query.Where(x => x.NombreBusqueda.Contains(name));
        }

        var total = await query.CountAsync(ct);
        if (search.Take <= 0)
        {
            return ([], total);
        }

        var rows = await query
            .OrderBy(x => x.Apellido)
            .ThenBy(x => x.Nombre)
            .Skip(search.Skip)
            .Take(search.Take)
            .Select(x => new { Id = x.CiudadanoId, x.Rut, x.Nombre, x.Apellido, Abiertas = x.Cantidad })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new DuplicadoGrupoDto(
                r.Id,
                Rut.TryParse(r.Rut, out var parsed) ? parsed!.Formatted : r.Rut,
                $"{r.Nombre} {r.Apellido}".Trim(),
                r.Abiertas))
            .ToList();
        return (items, total);
    }

    public Task<IReadOnlyList<CarpetaAbiertaDto>> GetCarpetasAbiertasAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        CarpetasAbiertasQuery.ForCiudadanoAsync(db, ciudadanoId, ct);

    public Task<RevisionDuplicado?> GetRevisionAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        db.RevisionesDuplicado.SingleOrDefaultAsync(r => r.CiudadanoId == ciudadanoId, ct);

    public async Task AddRevisionAsync(RevisionDuplicado revision, CancellationToken ct = default) =>
        await db.RevisionesDuplicado.AddAsync(revision, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
