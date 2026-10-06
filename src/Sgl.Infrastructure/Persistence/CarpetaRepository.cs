using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

public sealed class CarpetaRepository(SglDbContext db) : ICarpetaRepository
{
    public async Task<Carpeta?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var carpeta = await db.Carpetas
            .Include(c => c.Ciudadano)
            .Include(c => c.Historial)
            .SingleOrDefaultAsync(c => c.Id == id, ct);

        if (carpeta is null)
        {
            var idStr = id.ToString();
            carpeta = await db.Carpetas
                .Include(c => c.Ciudadano)
                .Include(c => c.Historial)
                .FirstOrDefaultAsync(c => EF.Functions.Like(c.Id.ToString(), idStr), ct);
        }

        return carpeta;
    }

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

        if (search.TipoTramite is { } tipo)
        {
            query = query.Where(c => c.TipoTramite == tipo);
        }

        if (search.CajaArchivo is { } caja)
        {
            query = query.Where(c => c.CajaArchivo == caja);
        }

        if (search.SinCaja is true)
        {
            query = query.Where(c => c.CajaArchivo == null || c.CajaArchivo == "");
        }
        else if (search.SinCaja is false)
        {
            query = query.Where(c => c.CajaArchivo != null && c.CajaArchivo != "");
        }

        if (search.Comuna is { } comuna)
        {
            query = query.Where(c => c.Comuna != null && c.Comuna.Contains(comuna));
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
                c.FechaSubida,
                c.FechaUltimaCarpeta,
                c.Comuna,
                c.IdoneidadMoral,
                c.TipoTramite,
                c.CajaArchivo,
                c.Estado,
                c.Decision,
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new CarpetaResumenDto(
                r.Id,
                Rut.TryParse(r.Rut, out var parsedRut) ? parsedRut!.Formatted : r.Rut,
                $"{r.Nombre} {r.Apellido}".Trim(),
                r.Sede,
                r.FechaCitacion,
                r.FechaSubida,
                r.FechaUltimaCarpeta,
                r.Comuna,
                r.IdoneidadMoral,
                r.TipoTramite,
                r.CajaArchivo,
                r.Estado,
                r.Decision))
            .ToList();
        return (items, total);
    }

    public async Task<EstadisticasGlobalesDto> GetEstadisticasAsync(CancellationToken ct = default)
    {
        var raw = await db.Carpetas
            .AsNoTracking()
            .Select(c => new
            {
                c.Sede,
                c.Decision,
                c.IdoneidadMoral,
                c.TipoTramite,
            })
            .ToListAsync(ct);

        var sedesMetrics = new List<SedeMetricDto>();
        var sedes = new[] { Sede.AvArgentina, Sede.Placilla, Sede.MercadoPuerto };

        foreach (var s in sedes)
        {
            var items = raw.Where(x => x.Sede == s).ToList();
            var total = items.Count;
            var otorgadas = items.Count(x => x.Decision == Decision.Otorgado);
            var denegadas = items.Count(x => x.Decision == Decision.Denegado);
            var alertadas = items.Count(x => !string.IsNullOrEmpty(x.IdoneidadMoral));
            var subidasConaset = items.Count(x => x.TipoTramite != null && x.TipoTramite.Contains("CONASET", StringComparison.OrdinalIgnoreCase));
            var primeraLic = items.Count(x => x.TipoTramite != null && x.TipoTramite.Contains("1°", StringComparison.OrdinalIgnoreCase));
            var cambioDom = items.Count(x => x.TipoTramite != null && x.TipoTramite.Contains("DOMICILIO", StringComparison.OrdinalIgnoreCase));

            string nombre = s switch
            {
                Sede.AvArgentina => "Av. Argentina",
                Sede.Placilla => "Placilla",
                Sede.MercadoPuerto => "Mercado Puerto",
                _ => s.ToString(),
            };

            sedesMetrics.Add(new SedeMetricDto(
                s,
                nombre,
                total,
                otorgadas,
                denegadas,
                alertadas,
                subidasConaset,
                primeraLic,
                cambioDom));
        }

        var totalGlobal = raw.Count;
        var totalOtorgadas = raw.Count(x => x.Decision == Decision.Otorgado);
        var totalDenegadas = raw.Count(x => x.Decision == Decision.Denegado);
        var totalAlertadas = raw.Count(x => !string.IsNullOrEmpty(x.IdoneidadMoral));

        return new EstadisticasGlobalesDto(
            totalGlobal,
            totalOtorgadas,
            totalDenegadas,
            totalAlertadas,
            sedesMetrics);
    }

    public async Task<IReadOnlyList<CajaResumenDto>> GetCajasAsync(CancellationToken ct = default)
    {
        var raw = await db.Carpetas
            .AsNoTracking()
            .Where(c => c.CajaArchivo != null && c.CajaArchivo != "")
            .GroupBy(c => c.CajaArchivo!)
            .Select(g => new { Caja = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return raw
            .OrderBy(c => c.Caja)
            .Select(c => new CajaResumenDto(c.Caja, c.Count))
            .ToList();
    }

    public async Task<IReadOnlyList<CarpetaResumenDto>> GetCarpetasByCajaAsync(string caja, CancellationToken ct = default)
    {
        var raw = await db.Carpetas
            .AsNoTracking()
            .Where(c => c.CajaArchivo == caja)
            .OrderBy(c => c.FechaSubida ?? c.FechaCitacion)
            .ThenBy(c => c.Ciudadano.Apellido)
            .Select(c => new
            {
                c.Id,
                Rut = c.Ciudadano.Rut.Formatted,
                c.Ciudadano.Nombre,
                c.Ciudadano.Apellido,
                c.Sede,
                c.FechaCitacion,
                c.FechaSubida,
                c.FechaUltimaCarpeta,
                c.Comuna,
                c.IdoneidadMoral,
                c.TipoTramite,
                c.CajaArchivo,
                c.Estado,
                c.Decision,
            })
            .ToListAsync(ct);

        return raw.Select(c => new CarpetaResumenDto(
            c.Id,
            c.Rut,
            $"{c.Nombre} {c.Apellido}".Trim(),
            c.Sede,
            c.FechaCitacion,
            c.FechaSubida,
            c.FechaUltimaCarpeta,
            c.Comuna,
            c.IdoneidadMoral,
            c.TipoTramite,
            c.CajaArchivo,
            c.Estado,
            c.Decision)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetTiposTramiteAsync(CancellationToken ct = default)
    {
        return await db.Carpetas
            .AsNoTracking()
            .Where(c => c.TipoTramite != null && c.TipoTramite != "")
            .Select(c => c.TipoTramite!)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
