using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

public sealed class CarpetaRepository(SglDbContext db) : ICarpetaRepository
{
    public Task<Carpeta?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Carpetas
            .Include(c => c.Ciudadano)
            .Include(c => c.Historial)
            .SingleOrDefaultAsync(c => c.Id == id, ct);

    public Task<Ciudadano?> FindCiudadanoAsync(Rut rut, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rut);
        var value = rut.Value;
        return db.Ciudadanos.SingleOrDefaultAsync(c => c.Rut.Value == value, ct);
    }

    public async Task AddAsync(Carpeta carpeta, CancellationToken ct = default) =>
        await db.Carpetas.AddAsync(carpeta, ct);

    public async Task<(IReadOnlyList<CarpetaResumenDto> Items, int Total)> SearchAsync(
        CarpetaSearch search, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = db.Carpetas.AsNoTracking();

        if (search.RutFragment is { } rut)
        {
            query = query.Where(c => c.Ciudadano.Rut.Value.Contains(rut));
        }

        if (search.NameFragment is { } name)
        {
            query = query.Where(c => c.Ciudadano.NombreBusqueda.Contains(name));
        }

        if (search.Sede is { } sede)
        {
            query = query.Where(c => c.Sede == sede);
        }

        if (search.Estado is { } estado)
        {
            query = query.Where(c => c.Estado == estado);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(c => c.FechaCitacion)
            .ThenBy(c => c.Ciudadano.Apellido)
            .Skip(search.Skip)
            .Take(search.Take)
            .Select(c => new
            {
                c.Id,
                Rut = c.Ciudadano.Rut.Value,
                c.Ciudadano.Nombre,
                c.Ciudadano.Apellido,
                c.Sede,
                c.FechaCitacion,
                c.Estado,
                c.Decision,
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new CarpetaResumenDto(
                r.Id, Rut.Parse(r.Rut).Formatted, $"{r.Nombre} {r.Apellido}", r.Sede, r.FechaCitacion, r.Estado, r.Decision))
            .ToList();
        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
