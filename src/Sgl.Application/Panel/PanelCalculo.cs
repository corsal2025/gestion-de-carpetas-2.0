using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Panel;

/// <summary>Maps an audit entry to the tray the carpeta entered with it (null when it enters none).</summary>
public static class EntradasModulo
{
    public static Bandeja? Bandeja(string campo, string? anterior, string? nuevo) => campo switch
    {
        "Agenda" => Domain.Bandeja.PorClasificar,
        "Registro" => Domain.Bandeja.PorPreparar,
        nameof(Carpeta.Estado) when Enum.TryParse<EstadoCarpeta>(nuevo, out var estado) && Enum.IsDefined(estado) =>
            Carpeta.ClasificarBandeja(estado, EstadoTransitions.DecisionFor(estado), null),
        nameof(Carpeta.CajaArchivo) when !string.IsNullOrWhiteSpace(nuevo) && nuevo != Carpeta.ColaDeCaja =>
            Domain.Bandeja.Archivadas,
        _ => null,
    };
}

/// <summary>Daily series (oldest first, today last) of the last seven Chile calendar days.</summary>
public static class SerieSieteDias
{
    public const int Dias = 7;

    public static IReadOnlyDictionary<Bandeja, int[]> Construir(IEnumerable<(DateOnly Fecha, Bandeja Bandeja)> entradas, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(entradas);
        var series = Enum.GetValues<Bandeja>().ToDictionary(b => b, _ => new int[Dias]);
        foreach (var (fecha, bandeja) in entradas)
        {
            var indice = Indice(fecha, hoy);
            if (indice is { } i)
            {
                series[bandeja][i]++;
            }
        }
        return series;
    }

    public static int[] Contar(IEnumerable<DateOnly> fechas, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(fechas);
        var serie = new int[Dias];
        foreach (var f in fechas)
        {
            if (Indice(f, hoy) is { } i)
            {
                serie[i]++;
            }
        }
        return serie;
    }

    private static int? Indice(DateOnly fecha, DateOnly hoy)
    {
        var atras = hoy.DayNumber - fecha.DayNumber;
        return atras is >= 0 and < Dias ? Dias - 1 - atras : null;
    }
}

/// <summary>
/// Traffic light per module: red when any carpeta reached the red threshold (days since appointment),
/// yellow when any reached yellow, green otherwise. Closing trays (Cerradas, Archivadas) have no traffic light.
/// </summary>
public static class SemaforoModulo
{
    public static SemaforoModuloDto Evaluar(Bandeja bandeja, IEnumerable<(DateOnly Fecha, int Cantidad)> fechas, DateOnly hoy, BandejasOptions opciones)
    {
        ArgumentNullException.ThrowIfNull(fechas);
        ArgumentNullException.ThrowIfNull(opciones);
        if (bandeja is Bandeja.Cerradas or Bandeja.Archivadas)
        {
            return new SemaforoModuloDto(null, 0, 0);
        }

        int amarillas = 0, rojas = 0;
        foreach (var (fecha, cantidad) in fechas)
        {
            var dias = hoy.DayNumber - fecha.DayNumber;
            switch (opciones.Evaluar(bandeja, Math.Max(0, dias)))
            {
                case Semaforo.Rojo: rojas += cantidad; break;
                case Semaforo.Amarillo: amarillas += cantidad; break;
            }
        }

        var color = rojas > 0 ? Semaforo.Rojo : amarillas > 0 ? Semaforo.Amarillo : Semaforo.Verde;
        return new SemaforoModuloDto(color, amarillas, rojas);
    }
}

/// <summary>Picks the single contextual message Lico shows on the home panel (most urgent first).</summary>
public static class MensajeLico
{
    public static string Elegir(int alertasDuplicado, int duplicadosPendientes, MetaDelDiaDto meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        if (alertasDuplicado > 0)
        {
            return $"Tienes {alertasDuplicado} {(alertasDuplicado == 1 ? "fila" : "filas")} con alerta de duplicado en Por clasificar.";
        }
        if (duplicadosPendientes > 0)
        {
            return $"Tienes {duplicadosPendientes} {(duplicadosPendientes == 1 ? "duplicado" : "duplicados")} por revisar.";
        }
        return meta.Pendientes > 0
            ? $"Te quedan {meta.Pendientes} por clasificar hoy. ¡Vamos!"
            : "¡Por clasificar al día!";
    }
}
