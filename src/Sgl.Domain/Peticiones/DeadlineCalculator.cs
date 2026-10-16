namespace PeticionCambioDomicilio.Domain;

/// <summary>
/// Plazo legal en días hábiles (lunes a viernes). Los feriados chilenos NO se descuentan:
/// el conteo queda ligeramente conservador en semanas con feriado, que es el lado seguro
/// para un plazo legal.
/// Portado de LicenciasCarpetas/CambioDomicilio/Domain/DeadlineCalculator.cs.
/// </summary>
public static class DeadlineCalculator
{
    public const int PlazoDiasHabiles = 15;

    public static DateOnly AddBusinessDays(DateOnly start, int businessDays)
    {
        var date = start;
        var added = 0;
        while (added < businessDays)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                added++;
            }
        }

        return date;
    }

    /// <summary>Días hábiles entre dos fechas (excluye la de inicio, incluye la de fin si es hábil).</summary>
    public static int BusinessDaysBetween(DateOnly from, DateOnly to)
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
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
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
    public static PlazoInfo Para(Peticion p)
    {
        var inicio = p.EnviadaEn is { } e ? DateOnly.FromDateTime(e.LocalDateTime) : (DateOnly?)null;

        if (inicio is null)
        {
            return new PlazoInfo(null, null, 0, DeadlineCalculator.PlazoDiasHabiles, false);
        }

        var hoy = DateOnly.FromDateTime(DateTime.Now);
        var vence = DeadlineCalculator.AddBusinessDays(inicio.Value, DeadlineCalculator.PlazoDiasHabiles);
        var transcurridos = DeadlineCalculator.BusinessDaysBetween(inicio.Value, hoy);
        var restantes = DeadlineCalculator.PlazoDiasHabiles - transcurridos;

        return new PlazoInfo(inicio, vence, transcurridos, restantes, restantes < 0);
    }
}
