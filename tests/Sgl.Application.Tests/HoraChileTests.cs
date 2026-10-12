using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

public sealed class HoraChileTests
{
    // 2026-10-12 23:30 Chile (summer time, UTC-3) == 2026-10-13 02:30 UTC.
    private static readonly DateTimeOffset NocheChileEnUtc = new(2026, 10, 13, 2, 30, 0, TimeSpan.Zero);

    [Fact]
    public void HoyChile_uses_chile_calendar_date_late_at_night()
    {
        var clock = new FixedTimeProvider(NocheChileEnUtc);

        Assert.Equal(new DateOnly(2026, 10, 12), clock.HoyChile());
    }

    [Fact]
    public void RelojChile_local_now_is_chile_time()
    {
        var local = TimeZoneInfo.ConvertTime(NocheChileEnUtc, RelojChile.Instancia.LocalTimeZone);

        Assert.Equal(new DateTime(2026, 10, 12, 23, 30, 0), local.DateTime);
    }

    [Fact]
    public void DiasTramite_is_correct_at_2330_chile_time()
    {
        var hoy = new FixedTimeProvider(NocheChileEnUtc).HoyChile();
        var c = new CarpetaResumenDto(Guid.NewGuid(), "1-9", "X", Sede.Placilla, new DateOnly(2026, 10, 2), null, null, null, null, null, null,
            EstadoCarpeta.Citada, Decision.Pendiente, false);

        var ind = IndicadoresCalculator.Calcular(c, [], hoy);

        Assert.Equal(10, ind.DiasTramite);
    }

    [Fact]
    public void Fecha_of_utc_timestamp_maps_to_chile_date()
    {
        Assert.Equal(new DateOnly(2026, 10, 12), HoraChile.Fecha(NocheChileEnUtc.UtcDateTime));
    }

    [Fact]
    public void Dst_transition_keeps_dates_consistent()
    {
        // Chile enters DST on the first Saturday of September at 24:00 (2026-09-06 00:00 -> 01:00).
        var antes = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);  // UTC-4 -> 08:00
        var despues = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc); // UTC-3 -> 09:00

        Assert.Equal(new DateTime(2026, 9, 5, 8, 0, 0), HoraChile.ALocal(antes));
        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0), HoraChile.ALocal(despues));
        Assert.Equal(new DateOnly(2026, 9, 6), HoraChile.Fecha(new DateTime(2026, 9, 7, 2, 59, 0, DateTimeKind.Utc)));
    }
}
