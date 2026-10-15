using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

public sealed class CarpetaRepository(SglDbContext db) : ICarpetaRepository
{
    public async Task<Carpeta?> GetAsync(Guid id, CancellationToken ct = default)
    {
        // Ignores the soft-delete filter: restoring or inspecting a cancelled carpeta must find it.
        var carpeta = await db.Carpetas
            .IgnoreQueryFilters()
            .Include(c => c.Ciudadano)
            .Include(c => c.Historial)
            .SingleOrDefaultAsync(c => c.Id == id, ct);

        if (carpeta is null)
        {
            var idStr = id.ToString();
            carpeta = await db.Carpetas
                .IgnoreQueryFilters()
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

    public Task<IReadOnlyList<CarpetaAbiertaDto>> GetCarpetasAbiertasAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        CarpetasAbiertasQuery.ForCiudadanoAsync(db, ciudadanoId, ct);

    public async Task AddAsync(Carpeta carpeta, CancellationToken ct = default) =>
        await db.Carpetas.AddAsync(carpeta, ct);

    public async Task<(IReadOnlyList<CarpetaResumenDto> Items, int Total)> SearchAsync(
        CarpetaSearch search, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = Filtrar(search);
        if (search.Bandeja is { } bandeja)
        {
            query = query.Where(Carpeta.EnBandeja(bandeja)).Where(FueraDeDuplicados());
        }

        if (search.Ids is { } ids)
        {
            // By primary key: no count, no OFFSET; the caller already knows the order.
            query = query.Where(c => ids.Contains(c.Id));
        }

        var total = search.Ids is { } known ? known.Count : await query.CountAsync(ct);
        if (search.Take is 0 && search.Ids is null)
        {
            return ([], total);
        }

        IQueryable<Carpeta> limited = query;
        if (search.Ids is null)
        {
            var paged = Ordenar(query, search.Bandeja).Skip(search.Skip);
            limited = search.Take is { } take ? paged.Take(take) : paged;
        }

        var rows = await limited
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
                c.PendienteBusqueda,
                c.FechaRegistro,
                c.HoraCita,
                c.TramiteAgenda,
                c.FechaEstadoActual,
                c.FechaCierre,
                c.FechaImpresion,
                OtrasAbiertas = search.Take == null && search.Ids == null
                    ? 0
                    : db.Carpetas.Count(o => o.CiudadanoId == c.CiudadanoId && o.Id != c.Id && o.Decision == Decision.Pendiente),
            })
            .ToListAsync(ct);

        var conRango = search.Take is not null || search.Ids is not null;
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
                r.Decision,
                r.PendienteBusqueda)
            {
                FechaRegistro = r.FechaRegistro,
                HoraCita = r.HoraCita,
                TramiteAgenda = r.TramiteAgenda,
                FechaEstadoActual = r.FechaEstadoActual,
                FechaCierre = r.FechaCierre,
                FechaImpresion = r.FechaImpresion,
                OtrasAbiertas = conRango ? r.OtrasAbiertas : null,
            })
            .ToList();
        if (search.Ids is { } orden)
        {
            var posicion = orden.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            items = items.OrderBy(i => posicion.GetValueOrDefault(i.Id)).ToList();
        }

        return (items, total);
    }

    // Id as final tie-breaker keeps the order total, so consecutive ranges never repeat or skip rows.
    private static IOrderedQueryable<Carpeta> Ordenar(IQueryable<Carpeta> query, Bandeja? bandeja) =>
        (bandeja == Bandeja.PorClasificar
            ? query.OrderByDescending(c => c.FechaCitacion).ThenBy(c => c.HoraCita).ThenBy(c => c.Ciudadano.Apellido)
            : query.OrderByDescending(c => c.FechaCitacion).ThenBy(c => c.Ciudadano.Apellido))
        .ThenBy(c => c.Id);

    public async Task<IReadOnlyList<Guid>> SearchIdsAsync(CarpetaSearch search, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = Filtrar(search);
        if (search.Bandeja is { } bandeja)
        {
            query = query.Where(Carpeta.EnBandeja(bandeja)).Where(FueraDeDuplicados());
        }

        return await Ordenar(query, search.Bandeja).Select(c => c.Id).ToListAsync(ct);
    }

    public async Task<long> ObtenerVersionDatosAsync(CancellationToken ct = default) =>
        await db.Historial.AsNoTracking().MaxAsync(h => (long?)h.Id, ct) ?? 0;

    public async Task<IReadOnlyDictionary<Bandeja, int>> CountByBandejaAsync(CarpetaSearch search, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var grupos = await Filtrar(search)
            .Where(FueraDeDuplicados())
            .GroupBy(Carpeta.BandejaExpresion)
            .Select(g => new { g.Key, Total = g.Count() })
            .ToListAsync(ct);

        return Enum.GetValues<Bandeja>().ToDictionary(b => b, b => grupos.FirstOrDefault(g => g.Key == b)?.Total ?? 0);
    }

    public async Task<IReadOnlyList<MovimientoDto>> GetMovimientosAsync(IReadOnlyCollection<Guid> carpetaIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(carpetaIds);
        if (carpetaIds.Count == 0)
        {
            return [];
        }

        return await db.Historial
            .AsNoTracking()
            .Where(h => carpetaIds.Contains(h.CarpetaId))
            .Select(h => new MovimientoDto(h.CarpetaId, h.Fecha, h.Usuario, h.Campo, h.Nuevo))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, TramitePrevioDto>> GetTramitesPreviosAsync(
        IReadOnlyCollection<Guid> carpetaIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(carpetaIds);
        if (carpetaIds.Count == 0)
        {
            return new Dictionary<Guid, TramitePrevioDto>();
        }

        var pares = await db.Carpetas.AsNoTracking()
            .Where(c => carpetaIds.Contains(c.Id))
            .Select(c => new { c.Id, c.CiudadanoId })
            .ToListAsync(ct);
        var ciudadanoIds = pares.Select(p => p.CiudadanoId).Distinct().ToList();
        var otras = await db.Carpetas.AsNoTracking()
            .Where(c => ciudadanoIds.Contains(c.CiudadanoId))
            .Select(c => new { c.Id, c.CiudadanoId, c.Sede, c.FechaCitacion })
            .ToListAsync(ct);

        var resultado = new Dictionary<Guid, TramitePrevioDto>();
        foreach (var p in pares)
        {
            var previas = otras.Where(o => o.CiudadanoId == p.CiudadanoId && o.Id != p.Id).ToList();
            if (previas.Count == 0) continue;
            var ultima = previas.OrderByDescending(o => o.FechaCitacion).First();
            resultado[p.Id] = new TramitePrevioDto(previas.Count, ultima.Sede, ultima.FechaCitacion);
        }
        return resultado;
    }

    public async Task<IReadOnlyDictionary<Guid, AlertaDuplicadoDto>> GetAlertasDuplicadoAsync(
        IReadOnlyCollection<Guid> carpetaIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(carpetaIds);
        if (carpetaIds.Count == 0)
        {
            return new Dictionary<Guid, AlertaDuplicadoDto>();
        }

        var pares = await db.Carpetas.AsNoTracking()
            .Where(c => carpetaIds.Contains(c.Id))
            .Select(c => new { c.Id, c.CiudadanoId })
            .ToListAsync(ct);
        var ciudadanoIds = pares.Select(p => p.CiudadanoId).Distinct().ToList();
        var abiertas = await db.Carpetas.AsNoTracking()
            .Where(c => ciudadanoIds.Contains(c.CiudadanoId))
            .Where(Carpeta.AbiertaSinArchivarExpresion)
            .Select(c => new { c.Id, c.CiudadanoId, c.FechaCitacion, c.Sede, c.Estado, c.Clases })
            .ToListAsync(ct);

        var resultado = new Dictionary<Guid, AlertaDuplicadoDto>();
        foreach (var p in pares)
        {
            var otras = abiertas
                .Where(o => o.CiudadanoId == p.CiudadanoId && o.Id != p.Id)
                .OrderByDescending(o => o.FechaCitacion)
                .ToList();
            if (otras.Count > 0)
            {
                var otra = otras[0];
                resultado[p.Id] = new AlertaDuplicadoDto(otra.Id, otra.FechaCitacion, otra.Sede, otra.Estado)
                {
                    Total = otras.Count,
                    Todas = otras.Select(o => new CarpetaAbiertaDto(o.Id, o.Sede, o.FechaCitacion, o.Estado, o.Clases)).ToList(),
                };
            }
        }
        return resultado;
    }

    public async Task<IReadOnlyList<CarpetaAbiertaDto>> GetCarpetasAbiertasSinArchivarAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        await db.Carpetas.AsNoTracking()
            .Where(c => c.CiudadanoId == ciudadanoId)
            .Where(Carpeta.AbiertaSinArchivarExpresion)
            .OrderByDescending(c => c.FechaCitacion)
            .Select(c => new CarpetaAbiertaDto(c.Id, c.Sede, c.FechaCitacion, c.Estado, c.Clases))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CarpetaAnuladaDto>> GetAnuladasAsync(string? rutFragment, string? nameFragment, CancellationToken ct = default)
    {
        var query = db.Carpetas.IgnoreQueryFilters().AsNoTracking().Where(c => c.Anulada);
        if (rutFragment is { } rut)
        {
            query = query.Where(c => c.Ciudadano.Rut.Value.Contains(rut));
        }
        if (nameFragment is { } name)
        {
            query = query.Where(c => c.Ciudadano.NombreBusqueda.Contains(name));
        }

        var rows = await query
            .OrderByDescending(c => c.AnuladaEn)
            .ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                Rut = c.Ciudadano.Rut.Value,
                c.Ciudadano.Nombre,
                c.Ciudadano.Apellido,
                c.Sede,
                c.FechaCitacion,
                c.Estado,
                c.MotivoAnulacion,
                c.AnuladaEn,
                c.AnuladaPor,
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new CarpetaAnuladaDto(
                r.Id,
                Rut.TryParse(r.Rut, out var parsed) ? parsed!.Formatted : r.Rut,
                $"{r.Nombre} {r.Apellido}".Trim(),
                r.Sede,
                r.FechaCitacion,
                r.Estado,
                r.MotivoAnulacion ?? "",
                r.AnuladaEn ?? DateTime.MinValue,
                r.AnuladaPor ?? ""))
            .ToList();
    }

    // Possible duplicates pending review live only in their own module, never in a tray or its counter.
    private Expression<Func<Carpeta, bool>> FueraDeDuplicados()
    {
        var pendientes = DuplicadosPendientes.CiudadanoIds(db);
        return c => !pendientes.Contains(c.CiudadanoId);
    }

    private IQueryable<Carpeta> Filtrar(CarpetaSearch search)
    {
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

        if (search.Desde is { } desde)
        {
            query = query.Where(c => c.FechaCitacion >= desde);
        }

        if (search.Hasta is { } hasta)
        {
            query = query.Where(c => c.FechaCitacion <= hasta);
        }

        return query;
    }

    /// <summary>Per-sede totals computed with one GROUP BY in SQL (never materializes the carpetas).</summary>
    public async Task<EstadisticasGlobalesDto> GetEstadisticasAsync(CancellationToken ct = default)
    {
        var filas = await db.Carpetas
            .AsNoTracking()
            .GroupBy(c => c.Sede)
            .Select(g => new
            {
                Sede = g.Key,
                Total = g.Count(),
                Otorgadas = g.Count(c => c.Decision == Decision.Otorgado),
                Denegadas = g.Count(c => c.Decision == Decision.Denegado),
                Alertadas = g.Count(c => c.IdoneidadMoral != null && c.IdoneidadMoral != ""),
                Conaset = g.Count(c => c.TipoTramite != null && EF.Functions.Like(c.TipoTramite, "%CONASET%")),
                Primera = g.Count(c => c.TipoTramite != null && EF.Functions.Like(c.TipoTramite, "%1°%")),
                Domicilio = g.Count(c => c.TipoTramite != null && EF.Functions.Like(c.TipoTramite, "%DOMICILIO%")),
            })
            .ToListAsync(ct);

        var sedes = new[] { Sede.AvArgentina, Sede.Placilla, Sede.MercadoPuerto };
        var metricas = sedes
            .Select(s =>
            {
                var f = filas.FirstOrDefault(x => x.Sede == s);
                var nombre = s switch
                {
                    Sede.AvArgentina => "Av. Argentina",
                    Sede.Placilla => "Placilla",
                    Sede.MercadoPuerto => "Mercado Puerto",
                    _ => s.ToString(),
                };
                return new SedeMetricDto(s, nombre, f?.Total ?? 0, f?.Otorgadas ?? 0, f?.Denegadas ?? 0, f?.Alertadas ?? 0,
                    f?.Conaset ?? 0, f?.Primera ?? 0, f?.Domicilio ?? 0);
            })
            .ToList();

        return new EstadisticasGlobalesDto(
            filas.Sum(f => f.Total),
            filas.Sum(f => f.Otorgadas),
            filas.Sum(f => f.Denegadas),
            filas.Sum(f => f.Alertadas),
            metricas);
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
                c.PendienteBusqueda,
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
            c.Decision,
            c.PendienteBusqueda)).ToList();
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

    public void DescartarCambios() => db.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
