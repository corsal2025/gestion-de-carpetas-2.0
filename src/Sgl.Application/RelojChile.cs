using Sgl.Domain;

namespace Sgl.Application;

/// <summary>System clock whose local time zone is always Chile, regardless of the server's zone.</summary>
public sealed class RelojChile : TimeProvider
{
    public static RelojChile Instancia { get; } = new();

    public override TimeZoneInfo LocalTimeZone => HoraChile.Zona;
}

public static class TimeProviderChileExtensions
{
    /// <summary>Today's Chile calendar date from any TimeProvider (uses UTC + Chile zone, ignores provider's local zone).</summary>
    public static DateOnly HoyChile(this TimeProvider clock) => HoraChile.Fecha(clock.GetUtcNow().UtcDateTime);
}
