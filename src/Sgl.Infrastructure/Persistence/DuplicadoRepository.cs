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
        var abiertas = db.Carpetas.Where(c => c.Decision == Decision.Pendiente);

        var query = db.Ciudadanos
            .AsNoTracking()
            .Where(ci => abiertas.Count(c => c.CiudadanoId == ci.Id) > 1)
            .Where(ci => !db.RevisionesDuplicado.Any(r =>
                r.CiudadanoId == ci.Id
                && !abiertas.Any(c => c.CiudadanoId == ci.Id && c.FechaRegistro > r.RevisadoEn)));

        if (search.Sede is { } sede)
        {
            query = query.Where(ci => abiertas.Any(c => c.CiudadanoId == ci.Id && c.Sede == sede));
        }

        if (search.RutFragment is { } rut)
        {
            query = query.Where(ci => ci.Rut.Value.Contains(rut));
        }

        if (search.NameFragment is { } name)
        {
            query = query.Where(ci => ci.NombreBusqueda.Contains(name));
        }

        var total = await query.CountAsync(ct);
        if (search.Take <= 0)
        {
            return ([], total);
        }

        var rows = await query
            .OrderBy(ci => ci.Apellido)
            .ThenBy(ci => ci.Nombre)
            .Skip(search.Skip)
            .Take(search.Take)
            .Select(ci => new
            {
                ci.Id,
                Rut = ci.Rut.Value,
                ci.Nombre,
                ci.Apellido,
                Abiertas = abiertas.Count(c => c.CiudadanoId == ci.Id),
            })
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
