using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Panel;

/// <summary>Home panel use cases: module cards (count, traffic light, 7-day series), today's goal and Lico's message.</summary>
public sealed class PanelService(IPanelQuery query, IDuplicadoRepository duplicados, TimeProvider clock, BandejasOptions opciones)
{
    private const string CampoEstado = nameof(Carpeta.Estado);
    private static readonly string PorClasificar = nameof(EstadoCarpeta.PorClasificar);

    public async Task<PanelDto> ObtenerAsync(CancellationToken ct = default)
    {
        var hoy = clock.HoyChile();
        // One extra UTC day so every Chile calendar day of the window is complete; filtered by Chile date below.
        var desde = hoy.AddDays(-SerieSieteDias.Dias).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var grupos = await query.GruposAsync(ct);
        var movimientos = await query.MovimientosDesdeAsync(desde, ct);
        var alertas = await query.AlertasDuplicadoAsync(ct);
        var anuladas = await query.AnuladasAsync(ct);
        var fechasAnulacion = await query.FechasAnulacionDesdeAsync(desde, ct);
        var (_, duplicadosPendientes) = await duplicados.SearchAsync(new DuplicadoSearch(null, null, null, 0, 0), ct);

        var porBandeja = grupos.ToLookup(g => g.Bandeja);
        var series = SerieSieteDias.Construir(
            movimientos
                .Select(m => (Fecha: HoraChile.Fecha(m.Fecha), Bandeja: EntradasModulo.Bandeja(m.Campo, m.Anterior, m.Nuevo)))
                .Where(x => x.Bandeja is not null)
                .Select(x => (x.Fecha, x.Bandeja!.Value)),
            hoy);

        var modulos = Enum.GetValues<Bandeja>()
            .Select(b =>
            {
                var semaforo = SemaforoModulo.Evaluar(b, porBandeja[b].Select(g => (g.FechaCitacion, g.Cantidad)), hoy, opciones);
                return new ModuloPanelDto(b, porBandeja[b].Sum(g => g.Cantidad), semaforo.Color, semaforo.Rojas, series[b]);
            })
            .ToList();

        var meta = new MetaDelDiaDto(ClasificadasHoy(movimientos, hoy), porBandeja[Bandeja.PorClasificar].Sum(g => g.Cantidad));
        // Location trays are worked down to empty: activity = carpetas moved out today, pending = still waiting.
        var actividad = BandejasDeUbicacion.ToDictionary(
            b => b.Bandeja,
            b => new MetaDelDiaDto(SalidasHoy(movimientos, b.Estado, hoy), porBandeja[b.Bandeja].Sum(g => g.Cantidad)));
        return new PanelDto(
            modulos,
            meta,
            duplicadosPendientes,
            alertas,
            anuladas,
            SerieSieteDias.Contar(fechasAnulacion.Select(HoraChile.Fecha), hoy),
            MensajeLico.Elegir(alertas, duplicadosPendientes, meta),
            actividad);
    }

    private static readonly (Bandeja Bandeja, string Estado)[] BandejasDeUbicacion =
    [
        (Bandeja.SeEncuentraEnArchivos, nameof(EstadoCarpeta.SeEncuentraEnArchivos)),
        (Bandeja.SeEncuentraEnOf43, nameof(EstadoCarpeta.SeEncuentraEnOf43)),
    ];

    private static int SalidasHoy(IEnumerable<MovimientoPanelDto> movimientos, string estado, DateOnly hoy) =>
        movimientos.Count(m => m.Campo == CampoEstado && m.Anterior == estado && HoraChile.Fecha(m.Fecha) == hoy);

    /// <summary>Only today's goal (used by the thin bar in "Por clasificar").</summary>
    public async Task<MetaDelDiaDto> ObtenerMetaAsync(CancellationToken ct = default)
    {
        var hoy = clock.HoyChile();
        var desde = hoy.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var movimientos = await query.MovimientosDesdeAsync(desde, ct);
        return new MetaDelDiaDto(ClasificadasHoy(movimientos, hoy), await query.PendientesPorClasificarAsync(ct));
    }

    private static int ClasificadasHoy(IEnumerable<MovimientoPanelDto> movimientos, DateOnly hoy) =>
        SalidasHoy(movimientos, PorClasificar, hoy);
}
