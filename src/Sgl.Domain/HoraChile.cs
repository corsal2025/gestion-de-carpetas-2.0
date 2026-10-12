namespace Sgl.Domain;

/// <summary>
/// Single source of truth for the business time zone (Chile continental).
/// Policy: audit timestamps are stored in UTC; every "today", day difference
/// and month boundary is computed on the Chile calendar date.
/// </summary>
public static class HoraChile
{
    public static TimeZoneInfo Zona { get; } = Resolver();

    /// <summary>Converts a stored UTC timestamp (Kind Utc or Unspecified) to Chile local time.</summary>
    public static DateTime ALocal(DateTime utc) =>
        utc.Kind == DateTimeKind.Local
            ? utc
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zona);

    /// <summary>Chile calendar date of a stored UTC timestamp.</summary>
    public static DateOnly Fecha(DateTime utc) => DateOnly.FromDateTime(ALocal(utc));

    private static TimeZoneInfo Resolver()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // try next id
            }
        }

        // Last resort: fixed UTC-3 (Chile summer time) so dates never drift to UTC.
        return TimeZoneInfo.CreateCustomTimeZone("Chile-Fallback", TimeSpan.FromHours(-3), "Chile", "Chile");
    }
}
