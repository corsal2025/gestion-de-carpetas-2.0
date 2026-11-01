namespace PeticionCambioDomicilio.Domain;

/// <summary>
/// Días hábiles de lunes a viernes con calendario nacional 2026–2027 y feriados adicionales configurables.
/// Los feriados extraordinarios y locales deben verificarse y configurarse por el municipio.
/// </summary>
public static class DeadlineCalculator
{
    public const int PlazoDiasHabiles = 15;

    // Referencia revisada en 2026: https://www.feriados.cl/ y https://www.feriados.cl/2027.php.
    // Fuera de estos años no se presume que el calendario nacional esté completo.
    private static readonly HashSet<DateOnly> FeriadosNacionales =
    [
        new(2026, 1, 1), new(2026, 4, 3), new(2026, 5, 1), new(2026, 5, 21), new(2026, 6, 29),
        new(2026, 7, 16), new(2026, 9, 18), new(2026, 10, 12), new(2026, 12, 8), new(2026, 12, 25),
        new(2027, 1, 1), new(2027, 3, 26), new(2027, 5, 21), new(2027, 6, 21), new(2027, 6, 28),
        new(2027, 7, 16), new(2027, 9, 17), new(2027, 10, 11), new(2027, 11, 1), new(2027, 12, 8),
    ];

    public static bool CalendarioNacionalIncluye(int anio) => anio is 2026 or 2027;

    private static bool EsHabil(DateOnly fecha, IReadOnlySet<DateOnly>? adicionales) =>
        fecha.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !FeriadosNacionales.Contains(fecha) && !(adicionales?.Contains(fecha) ?? false);

    public static DateOnly AddBusinessDays(DateOnly start, int businessDays, IReadOnlySet<DateOnly>? adicionales = null)
    {
        var date = start;
        var added = 0;
        while (added < businessDays)
        {
            date = date.AddDays(1);
            if (EsHabil(date, adicionales))
            {
                added++;
            }
        }

        return date;
    }

    /// <summary>Días hábiles entre dos fechas (excluye la de inicio, incluye la de fin si es hábil).</summary>
    public static int BusinessDaysBetween(DateOnly from, DateOnly to, IReadOnlySet<DateOnly>? adicionales = null)
    {
        if (from >= to)
        {
            return 0;
        }

        var count = 0;
        var date = from;
        while (date < to)
        {
            date = date.AddDays(1);
            if (EsHabil(date, adicionales))
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>Estado del plazo de una petición, listo para pintar en la tabla.</summary>
public sealed record PlazoInfo(
    DateOnly? Inicio,
    DateOnly? Vence,
    int DiasTranscurridos,
    int DiasRestantes,
    bool Vencido)
{
    /// <summary>
    /// El conteo arranca cuando se envió la solicitud (EnviadaEn) — el plazo legal de 15 días
    /// hábiles corre desde que se pide, no antes. Mientras no se haya enviado, no hay reloj.
    /// </summary>
    public static PlazoInfo Para(Peticion p, DateOnly? fechaReferencia = null, IReadOnlySet<DateOnly>? feriadosAdicionales = null)
    {
        var inicio = p.EnviadaEn is { } e ? Sgl.Domain.HoraChile.Fecha(e.UtcDateTime) : (DateOnly?)null;

        if (inicio is null)
        {
            return new PlazoInfo(null, null, 0, DeadlineCalculator.PlazoDiasHabiles, false);
        }

        var hoy = fechaReferencia ?? Sgl.Domain.HoraChile.Fecha(DateTime.UtcNow);
        var vence = DeadlineCalculator.AddBusinessDays(inicio.Value, DeadlineCalculator.PlazoDiasHabiles, feriadosAdicionales);
        var transcurridos = DeadlineCalculator.BusinessDaysBetween(inicio.Value, hoy, feriadosAdicionales);
        var restantes = DeadlineCalculator.PlazoDiasHabiles - transcurridos;

        return new PlazoInfo(inicio, vence, transcurridos, restantes, restantes < 0);
    }
}
