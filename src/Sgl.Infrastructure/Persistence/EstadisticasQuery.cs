using Microsoft.EntityFrameworkCore;
using Sgl.Application.Estadisticas;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>
/// Statistics read model. Counting is pushed to SQL (GROUP BY); only narrow scalar projections are
/// materialized when a metric needs per-row math (medians, audit pairing, text terms).
/// </summary>
public sealed class EstadisticasQuery(SglDbContext db) : IEstadisticasQuery
{
    private const string Importador = "Sistema Importador";
    private const int MesesSubidas = 12;
    private const int TopLentas = 10;
    private const int TopComunas = 15;
    private const int TopUso = 10;
    private const int TopTerminos = 15;
    private const int MaxImportaciones = 20;
    private static readonly DateOnly FechaMinimaValida = new(2000, 1, 1);

    private static readonly EstadoCarpeta[] CambioDomSolicitado = [EstadoCarpeta.CambioDomicilio, EstadoCarpeta.CambioDomicilioSolicitado];
    private static readonly EstadoCarpeta[] CambioDomSubido = [EstadoCarpeta.CambioDomSubidoConaset, EstadoCarpeta.CambioDomSubidoCorreo];

    public async Task<EstadisticasDto> ObtenerAsync(FiltroEstadisticas filtro, DateOnly hoy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var actuales = await DatosActualesAsync(filtro, hoy, ct);
        var busquedas = await BusquedasAsync(filtro, hoy, ct);
        var archivo = await ArchivoAsync(filtro, ct);
        var uso = await UsoAsync(filtro, ct);
        var kpis = await KpisAsync(filtro, hoy, actuales.CitacionSubida.Mediana, ct);
        return new EstadisticasDto(kpis, actuales, busquedas, archivo, uso);
    }

    // ---------------- Bases ----------------

    private IQueryable<Carpeta> PorSede(FiltroEstadisticas f)
    {
        var q = db.Carpetas.AsNoTracking();
        return f.Sede is { } sede ? q.Where(c => c.Sede == sede) : q;
    }

    private IQueryable<Carpeta> Carpetas(FiltroEstadisticas f)
    {
        var q = PorSede(f);
        if (f.Desde is { } desde) q = q.Where(c => c.FechaCitacion >= desde);
        if (f.Hasta is { } hasta) q = q.Where(c => c.FechaCitacion <= hasta);
        return q;
    }

    private IQueryable<HistorialCambio> Historial(FiltroEstadisticas f)
    {
        // db.Carpetas honors the soft-delete filter, so audit entries of cancelled carpetas are excluded too.
        var q = db.Historial.AsNoTracking();
        q = f.Sede is { } sede
            ? q.Where(h => db.Carpetas.Any(c => c.Id == h.CarpetaId && c.Sede == sede))
            : q.Where(h => db.Carpetas.Any(c => c.Id == h.CarpetaId));
        if (f.Desde is { } desde)
        {
            var d = desde.ToDateTime(TimeOnly.MinValue);
            q = q.Where(h => h.Fecha >= d);
        }

        if (f.Hasta is { } hasta)
        {
            var h2 = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue);
            q = q.Where(h => h.Fecha < h2);
        }

        return q;
    }

    // ---------------- KPIs ----------------

    private async Task<KpisDto> KpisAsync(FiltroEstadisticas f, DateOnly hoy, double? mediana, CancellationToken ct)
    {
        var inicioMes = new DateOnly(hoy.Year, hoy.Month, 1);
        var subidasMes = await PorSede(f).CountAsync(c => c.FechaSubida >= inicioMes && c.FechaSubida <= hoy, ct);

        var atrasadas = 0;
        foreach (var (bandeja, rojo) in f.UmbralRojo)
        {
            var corte = hoy.AddDays(-rojo);
            atrasadas += await PorSede(f).Where(Carpeta.EnBandeja(bandeja)).CountAsync(c => c.FechaCitacion <= corte, ct);
        }

        var abiertas = await PorSede(f).CountAsync(c => c.PendienteBusqueda, ct);
        return new KpisDto(subidasMes, atrasadas, abiertas, mediana);
    }

    // ---------------- A. Datos actuales ----------------

    private async Task<DatosActualesDto> DatosActualesAsync(FiltroEstadisticas f, DateOnly hoy, CancellationToken ct)
    {
        var subidas = await SubidasPorMesAsync(f, ct);

        var estados = await Carpetas(f)
            .GroupBy(c => new { c.Sede, c.Estado })
            .Select(g => new { g.Key.Sede, g.Key.Estado, N = g.Count() })
            .ToListAsync(ct);
        var estadoPorSede = estados
            .OrderBy(e => e.Sede).ThenByDescending(e => e.N)
            .Select(e => new ConteoSedeDto(e.Sede, e.Estado.ToString(), e.N))
            .ToList();

        var (tiempos, lentas) = await TiemposCitacionSubidaAsync(f, ct);

        var corte = hoy.AddDays(-f.DiasSinSubir);
        var sinSubir = await Carpetas(f)
            .Where(c => c.Decision == Decision.Pendiente && c.FechaSubida == null && c.FechaCitacion <= corte)
            .GroupBy(c => c.Sede)
            .Select(g => new { Sede = g.Key, N = g.Count() })
            .ToListAsync(ct);

        var (decisionSede, decisionMes) = await DecisionesAsync(f, ct);

        var cambios = estados
            .GroupBy(e => e.Sede)
            .Select(g => new CambioDomicilioSedeDto(
                g.Key,
                g.Where(e => CambioDomSolicitado.Contains(e.Estado)).Sum(e => e.N),
                g.Where(e => CambioDomSubido.Contains(e.Estado)).Sum(e => e.N)))
            .OrderBy(c => c.Sede)
            .ToList();

        var comunas = await Carpetas(f)
            .Where(c => c.Comuna != null && c.Comuna != "")
            .GroupBy(c => c.Comuna!)
            .Select(g => new { Comuna = g.Key, N = g.Count() })
            .OrderByDescending(x => x.N)
            .Take(TopComunas * 4)
            .ToListAsync(ct);
        comunas = comunas.Where(c => EstadisticasCalculo.EsEtiquetaSegura(c.Comuna)).Take(TopComunas).ToList();

        var personas = await PorSede(f)
            .Where(c => c.Decision == Decision.Pendiente)
            .GroupBy(c => c.CiudadanoId)
            .Where(g => g.Count() > 1)
            .CountAsync(ct);

        return new DatosActualesDto(
            subidas,
            estadoPorSede,
            tiempos,
            lentas,
            sinSubir.OrderBy(s => s.Sede).Select(s => new ConteoSedeDto(s.Sede, string.Empty, s.N)).ToList(),
            decisionSede,
            decisionMes,
            cambios,
            comunas.Select(c => new ConteoDto(c.Comuna, c.N)).ToList(),
            personas,
            f.DiasSinSubir);
    }

    private async Task<IReadOnlyList<SubidasMesDto>> SubidasPorMesAsync(FiltroEstadisticas f, CancellationToken ct)
    {
        var filas = await Carpetas(f)
            .Where(c => c.FechaSubida != null && c.FechaSubida >= FechaMinimaValida)
            .GroupBy(c => new { c.FechaSubida!.Value.Year, c.FechaSubida.Value.Month, c.Sede, c.Estado })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Sede, g.Key.Estado, N = g.Count() })
            .ToListAsync(ct);

        var meses = filas.Select(x => (x.Year, x.Month)).Distinct()
            .OrderByDescending(m => m.Year).ThenByDescending(m => m.Month)
            .Take(MesesSubidas).ToHashSet();

        return filas
            .Where(x => meses.Contains((x.Year, x.Month)))
            .GroupBy(x => (x.Year, x.Month, x.Sede))
            .Select(g =>
            {
                int Via(string via) => g.Where(x => EstadisticasCalculo.ViaDeSubida(x.Estado) == via).Sum(x => x.N);
                return new SubidasMesDto(g.Key.Year, g.Key.Month, g.Key.Sede,
                    Via(EstadisticasCalculo.ViaConaset), Via(EstadisticasCalculo.ViaF8), Via(EstadisticasCalculo.ViaOficio),
                    Via(EstadisticasCalculo.ViaCorreo), Via(EstadisticasCalculo.ViaOtra));
            })
            .OrderBy(s => s.Anio).ThenBy(s => s.Mes).ThenBy(s => s.Sede)
            .ToList();
    }

    private async Task<(TiemposDto, IReadOnlyList<CarpetaLentaDto>)> TiemposCitacionSubidaAsync(FiltroEstadisticas f, CancellationToken ct)
    {
        var pares = await Carpetas(f)
            .Where(c => c.FechaSubida != null && c.FechaSubida >= c.FechaCitacion)
            .Select(c => new { c.Id, c.Sede, c.FechaCitacion, FechaSubida = c.FechaSubida!.Value })
            .ToListAsync(ct);

        var conDias = pares.Select(p => (p, Dias: p.FechaSubida.DayNumber - p.FechaCitacion.DayNumber)).ToList();
        var tiempos = EstadisticasCalculo.Tiempos(conDias.Select(x => (double)x.Dias).ToList());
        var lentas = conDias
            .OrderByDescending(x => x.Dias)
            .Take(TopLentas)
            .Select(x => new CarpetaLentaDto(x.p.Id, x.p.Sede, x.p.FechaCitacion, x.p.FechaSubida, x.Dias))
            .ToList();
        return (tiempos, lentas);
    }

    /// <summary>Closing date = last audit change to Otorgado/Denegado; legacy rows fall back to FechaSubida.</summary>
    private async Task<(IReadOnlyList<DecisionSedeDto>, IReadOnlyList<DecisionMesDto>)> DecisionesAsync(FiltroEstadisticas f, CancellationToken ct)
    {
        var cerradas = await Carpetas(f)
            .Where(c => c.Decision != Decision.Pendiente)
            .Select(c => new { c.Id, c.Sede, c.Decision, c.FechaSubida })
            .ToListAsync(ct);

        var otorgado = nameof(Decision.Otorgado);
        var denegado = nameof(Decision.Denegado);
        var fechas = await db.Historial.AsNoTracking()
            .Where(h => h.Campo == nameof(Carpeta.Decision) && (h.Nuevo == otorgado || h.Nuevo == denegado))
            .GroupBy(h => h.CarpetaId)
            .Select(g => new { CarpetaId = g.Key, Fecha = g.Max(h => h.Fecha) })
            .ToDictionaryAsync(x => x.CarpetaId, x => x.Fecha, ct);

        var porSede = cerradas
            .GroupBy(c => c.Sede)
            .OrderBy(g => g.Key)
            .Select(g => new DecisionSedeDto(g.Key, g.Count(c => c.Decision == Decision.Otorgado), g.Count(c => c.Decision == Decision.Denegado)))
            .ToList();

        var porMes = cerradas
            .Select(c => (c.Decision, Fecha: fechas.TryGetValue(c.Id, out var fh) ? HoraChile.Fecha(fh) : c.FechaSubida))
            .Where(x => x.Fecha is { } d && d >= FechaMinimaValida)
            .GroupBy(x => (x.Fecha!.Value.Year, x.Fecha.Value.Month))
            .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
            .Take(MesesSubidas)
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new DecisionMesDto(g.Key.Year, g.Key.Month,
                g.Count(x => x.Decision == Decision.Otorgado), g.Count(x => x.Decision == Decision.Denegado)))
            .ToList();

        return (porSede, porMes);
    }

    // ---------------- B. Búsqueda ----------------

    private async Task<BusquedasDto> BusquedasAsync(FiltroEstadisticas f, DateOnly hoy, CancellationToken ct)
    {
        var campo = nameof(Carpeta.PendienteBusqueda);
        var marcas = await Historial(f)
            .Where(h => h.Campo == campo)
            .Select(h => new { h.CarpetaId, h.Fecha, h.Nuevo })
            .ToListAsync(ct);

        if (marcas.Count == 0)
        {
            return EstadisticasCalculo.AnalizarBusquedas([], [], new Dictionary<Guid, string>(), DateTime.UtcNow, f.DiasBusquedaAbierta);
        }

        var ids = marcas.Select(m => m.CarpetaId).Distinct().ToList();
        var noExisteTexto = nameof(EstadoCarpeta.NoExisteCarpeta);
        var noExiste = await db.Historial.AsNoTracking()
            .Where(h => h.Campo == nameof(Carpeta.Estado) && h.Nuevo == noExisteTexto && ids.Contains(h.CarpetaId))
            .Select(h => new MarcaNoExiste(h.CarpetaId, h.Fecha))
            .ToListAsync(ct);

        var sectores = (await db.Carpetas.AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .Select(c => new { c.Id, c.FechaUltimaCarpeta })
                .ToListAsync(ct))
            .ToDictionary(c => c.Id, c => SectorFisico.Calcular(c.FechaUltimaCarpeta));

        var ahora = DateTime.UtcNow;
        return EstadisticasCalculo.AnalizarBusquedas(
            marcas.Select(m => new MarcaBusqueda(m.CarpetaId, m.Fecha, m.Nuevo == "Si")),
            noExiste,
            sectores,
            ahora,
            f.DiasBusquedaAbierta);
    }

    // ---------------- C. Archivo / cajas ----------------

    private async Task<ArchivoDto> ArchivoAsync(FiltroEstadisticas f, CancellationToken ct)
    {
        var movimientos = await Historial(f)
            .Where(h => h.Campo == nameof(Carpeta.CajaArchivo))
            .Select(h => new MovimientoCaja(h.CarpetaId, h.Fecha, h.Anterior, h.Nuevo))
            .ToListAsync(ct);
        var (porMes, reabiertas) = EstadisticasCalculo.ArchivoPorMes(movimientos);

        var cola = Carpeta.ColaDeCaja;
        var ocupacion = await PorSede(f)
            .Where(c => c.CajaArchivo != null && c.CajaArchivo != "" && c.CajaArchivo != cola)
            .GroupBy(c => c.CajaArchivo)
            .Select(g => g.Count())
            .ToListAsync(ct);
        var enCola = await PorSede(f).CountAsync(c => c.CajaArchivo == cola, ct);

        return new ArchivoDto(
            porMes,
            ocupacion.Count,
            ocupacion.Count == 0 ? null : Math.Round(ocupacion.Average(), 1),
            reabiertas,
            enCola);
    }

    // ---------------- D. Uso del sistema ----------------

    private async Task<UsoSistemaDto> UsoAsync(FiltroEstadisticas f, CancellationToken ct)
    {
        var porUsuario = await Historial(f)
            .Where(h => h.Usuario != Importador)
            .GroupBy(h => h.Usuario)
            .Select(g => new { g.Key, N = g.Count() })
            .OrderByDescending(x => x.N)
            .Take(TopUso)
            .ToListAsync(ct);

        var porDia = await Historial(f)
            .Where(h => h.Usuario != Importador)
            .GroupBy(h => h.Fecha.Date)
            .Select(g => new { g.Key, N = g.Count() })
            .OrderByDescending(x => x.N)
            .Take(TopUso)
            .ToListAsync(ct);

        var registro = await Historial(f).CountAsync(h => h.Campo == "Registro", ct);
        var importacion = await Historial(f).CountAsync(h => h.Campo == "Importacion", ct);
        var (importaciones, inferidas) = await ImportacionesAsync(f, ct);

        var observaciones = await Carpetas(f)
            .Where(c => c.IdoneidadMoral != null && c.IdoneidadMoral != "")
            .Select(c => c.IdoneidadMoral)
            .ToListAsync(ct);

        var clases = await Carpetas(f)
            .GroupBy(c => new { c.Sede, ConClase = c.Clases != ClaseLicencia.Ninguna })
            .Select(g => new { g.Key.Sede, g.Key.ConClase, N = g.Count() })
            .ToListAsync(ct);

        return new UsoSistemaDto(
            porUsuario.Select(x => new ConteoDto(x.Key, x.N)).ToList(),
            porDia.Select(x => new ConteoDto(x.Key.ToString("yyyy-MM-dd"), x.N)).ToList(),
            registro,
            importacion,
            importaciones,
            inferidas,
            observaciones.Count(o => !string.IsNullOrWhiteSpace(o)),
            EstadisticasCalculo.TerminosFrecuentes(observaciones, TopTerminos),
            clases.GroupBy(c => c.Sede).OrderBy(g => g.Key)
                .Select(g => new ClaseSedeDto(g.Key, g.Where(x => x.ConClase).Sum(x => x.N), g.Where(x => !x.ConClase).Sum(x => x.N)))
                .ToList());
    }

    /// <summary>Persisted import log; before it existed, imports are inferred from "Importacion" audit batches.</summary>
    private async Task<(IReadOnlyList<ImportacionDto>, bool)> ImportacionesAsync(FiltroEstadisticas f, CancellationToken ct)
    {
        var q = db.ImportacionLogs.AsNoTracking();
        if (f.Desde is { } desde)
        {
            var d = desde.ToDateTime(TimeOnly.MinValue);
            q = q.Where(i => i.Fecha >= d);
        }

        if (f.Hasta is { } hasta)
        {
            var h = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue);
            q = q.Where(i => i.Fecha < h);
        }

        var logs = await q.OrderByDescending(i => i.Fecha).Take(MaxImportaciones)
            .Select(i => new ImportacionDto(i.Fecha, i.Usuario, i.FilasLeidas, i.CarpetasCreadas, i.CarpetasActualizadas, i.Errores))
            .ToListAsync(ct);
        if (logs.Count > 0 || await db.ImportacionLogs.AnyAsync(ct)) return (logs, false);

        var lotes = await Historial(f)
            .Where(h => h.Campo == "Importacion")
            .GroupBy(h => h.Fecha)
            .Select(g => new { Fecha = g.Key, N = g.Count() })
            .OrderByDescending(x => x.Fecha)
            .Take(MaxImportaciones)
            .ToListAsync(ct);
        return (lotes.Select(l => new ImportacionDto(l.Fecha, Importador, l.N, l.N, 0, 0)).ToList(), true);
    }
}
