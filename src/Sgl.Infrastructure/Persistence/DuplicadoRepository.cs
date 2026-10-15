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

        // Shared predicate with the trays (DuplicadosPendientes) so list and trays never disagree.
        var query =
            from g in DuplicadosPendientes.Grupos(db)
            join ci in db.Ciudadanos.AsNoTracking() on g.CiudadanoId equals ci.Id
            select new { g.CiudadanoId, g.Cantidad, g.PrimeraCita, g.UltimaCita, g.MismaSede, Rut = ci.Rut.Value, ci.Nombre, ci.Apellido, ci.NombreBusqueda };

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
        if (search.Take is 0)
        {
            return ([], total);
        }

        var ordered = query
            .OrderBy(x => x.Apellido)
            .ThenBy(x => x.Nombre)
            .ThenBy(x => x.CiudadanoId)
            .Skip(search.Skip);
        var limited = search.Take is { } take ? ordered.Take(take) : ordered;
        var rows = await limited
            .Select(x => new { Id = x.CiudadanoId, x.Rut, x.Nombre, x.Apellido, Abiertas = x.Cantidad, x.PrimeraCita, x.UltimaCita, x.MismaSede })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new DuplicadoGrupoDto(
                r.Id,
                Rut.TryParse(r.Rut, out var parsed) ? parsed!.Formatted : r.Rut,
                $"{r.Nombre} {r.Apellido}".Trim(),
                r.Abiertas,
                DuplicadoMotivo.Describir(Rut.TryParse(r.Rut, out _), r.PrimeraCita, r.UltimaCita, r.MismaSede)))
            .ToList();
        return (items, total);
    }

    public Task<IReadOnlyList<CarpetaAbiertaDto>> GetCarpetasAbiertasAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        CarpetasAbiertasQuery.ForCiudadanoAsync(db, ciudadanoId, ct);

    /// <summary>One round-trip: open carpetas plus their latest movement and pending-search start from the audit trail.</summary>
    public async Task<IReadOnlyList<CarpetaDuplicadoDetalleDto>> GetDetalleCarpetasAbiertasAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        await db.Carpetas
            .AsNoTracking()
            .Where(c => c.CiudadanoId == ciudadanoId && c.Decision == Decision.Pendiente)
            .OrderByDescending(c => c.FechaCitacion)
            .Select(c => new CarpetaDuplicadoDetalleDto(
                c.Id,
                c.Sede,
                c.FechaCitacion,
                c.FechaSubida,
                c.Estado,
                c.Decision,
                c.Clases,
                c.TipoTramite,
                c.FechaUltimaCarpeta,
                c.CajaArchivo,
                c.PendienteBusqueda,
                db.Historial
                    .Where(h => h.CarpetaId == c.Id && h.Campo == nameof(Carpeta.PendienteBusqueda) && h.Nuevo == "Si")
                    .Max(h => (DateTime?)h.Fecha),
                c.IdoneidadMoral,
                c.Comuna,
                db.Historial.Where(h => h.CarpetaId == c.Id).Max(h => (DateTime?)h.Fecha),
                db.Historial.Where(h => h.CarpetaId == c.Id)
                    .OrderByDescending(h => h.Fecha).ThenByDescending(h => h.Id)
                    .Select(h => h.Usuario).FirstOrDefault()))
            .ToListAsync(ct);

    public Task<RevisionDuplicado?> GetRevisionAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        db.RevisionesDuplicado.SingleOrDefaultAsync(r => r.CiudadanoId == ciudadanoId, ct);

    public async Task AddRevisionAsync(RevisionDuplicado revision, CancellationToken ct = default) =>
        await db.RevisionesDuplicado.AddAsync(revision, ct);

    public void DescartarCambios() => db.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
