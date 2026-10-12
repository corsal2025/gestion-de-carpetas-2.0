using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>One audit entry reduced to what the tray indicators need.</summary>
public sealed record MovimientoDto(Guid CarpetaId, DateTime Fecha, string Usuario, string Campo, string? Nuevo);

/// <summary>Automatic per-carpeta values computed from the audit trail; never typed by users.</summary>
public sealed record IndicadoresCarpeta(
    int? DiasTramite,
    int DiasSinCarpeta,
    int? DiasBuscando,
    string? MarcadaPor,
    DateTime? UltimoMovimiento,
    string? UltimoUsuario,
    DateOnly? FechaCierre,
    DateOnly? FechaArchivo,
    int? DiasCitacionSubida,
    int? DiasTotales);

public static class IndicadoresCalculator
{
    private const string CampoEstado = "Estado";
    private const string CampoDecision = "Decision";
    private const string CampoCaja = "CajaArchivo";
    private const string CampoPendiente = "PendienteBusqueda";

    public static IndicadoresCarpeta Calcular(CarpetaResumenDto c, IEnumerable<MovimientoDto> movimientos, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(c);
        var movs = movimientos.OrderBy(m => m.Fecha).ToList();

        var noEncontrada = movs.LastOrDefault(m => m.Campo == CampoEstado && m.Nuevo == nameof(EstadoCarpeta.NoExisteCarpeta));
        var inicioSinCarpeta = noEncontrada is not null ? HoraChile.Fecha(noEncontrada.Fecha) : c.FechaCitacion;

        var marca = c.PendienteBusqueda ? movs.LastOrDefault(m => m.Campo == CampoPendiente && m.Nuevo == "Si") : null;
        var ultimo = movs.LastOrDefault();

        var cierreMov = movs.LastOrDefault(m => m.Campo == CampoDecision && m.Nuevo is nameof(Decision.Otorgado) or nameof(Decision.Denegado));
        DateOnly? fechaCierre = c.Decision == Decision.Pendiente
            ? null
            : cierreMov is not null ? HoraChile.Fecha(cierreMov.Fecha) : c.FechaSubida;

        var archivo = movs.LastOrDefault(m => m.Campo == CampoCaja && !string.IsNullOrWhiteSpace(m.Nuevo) && m.Nuevo != Carpeta.ColaDeCaja);

        return new IndicadoresCarpeta(
            DiasTramite: c.FechaCitacion > hoy ? null : Dias(c.FechaCitacion, hoy),
            DiasSinCarpeta: Dias(inicioSinCarpeta, hoy),
            DiasBuscando: marca is null ? null : Dias(HoraChile.Fecha(marca.Fecha), hoy),
            MarcadaPor: marca?.Usuario,
            UltimoMovimiento: ultimo?.Fecha,
            UltimoUsuario: ultimo?.Usuario,
            FechaCierre: fechaCierre,
            FechaArchivo: archivo is null ? null : HoraChile.Fecha(archivo.Fecha),
            DiasCitacionSubida: c.FechaSubida is { } s ? s.DayNumber - c.FechaCitacion.DayNumber : null,
            DiasTotales: Dias(c.FechaCitacion, fechaCierre ?? hoy));
    }

    private static int Dias(DateOnly desde, DateOnly hasta) => Math.Max(0, hasta.DayNumber - desde.DayNumber);
}

public enum Semaforo
{
    Verde,
    Amarillo,
    Rojo,
}

public sealed class UmbralDias
{
    public int Amarillo { get; set; } = 15;

    public int Rojo { get; set; } = 30;
}

/// <summary>Traffic-light thresholds (days) per tray, bound from <c>Bandejas:Umbrales</c>.</summary>
public sealed class BandejasOptions
{
    public const string SectionName = "Bandejas";

    public Dictionary<Bandeja, UmbralDias> Umbrales { get; set; } = [];

    public Semaforo Evaluar(Bandeja bandeja, int dias)
    {
        var u = Umbrales.TryGetValue(bandeja, out var cfg) ? cfg : new UmbralDias();
        return dias >= u.Rojo ? Semaforo.Rojo : dias >= u.Amarillo ? Semaforo.Amarillo : Semaforo.Verde;
    }
}
