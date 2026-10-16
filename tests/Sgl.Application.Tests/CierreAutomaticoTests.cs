using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

/// <summary>Resolving a 1° Licencia carpeta stamps the closing date and freezes the processing days.</summary>
public sealed class CierreAutomaticoTests : IDisposable
{
    private static readonly DateOnly Citacion = new(2026, 4, 24);

    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    // A fresh registration lands in "Para revisión" (Citada); the 1° Licencia tray holds PrimeraLicencia carpetas.
    private async Task<Guid> Registrar()
    {
        var id = await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(
            "12.345.678-5", "Juan", "Pérez", Sede.AvArgentina, Citacion, "ana", ConfirmarDuplicado: true));
        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.PrimeraLicencia, "ana");
        return id;
    }

    private async Task<CarpetaResumenDto> Fila(Guid id) =>
        (await _db.NewService().ListarAsync(new ListarCarpetasQuery(Take: 10))).Items.Single(i => i.Id == id);

    [Theory]
    [InlineData(Decision.Otorgado)]
    [InlineData(Decision.Denegado)]
    public async Task Deciding_stamps_today_as_closing_date_and_leaves_the_primera_licencia_tray(Decision decision)
    {
        var id = await Registrar();
        Assert.Equal(1, (await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery()))[Bandeja.PorPreparar]);

        // Clock: 2026-05-04 10:00 UTC = 06:00 Chile, ten days after the citación.
        await _db.NewService().CambiarDecisionAsync(id, decision, "javiera");

        var fila = await Fila(id);
        var hoy = new DateOnly(2026, 5, 4);
        Assert.Equal(hoy, fila.FechaCierre);
        Assert.Equal(hoy.DayNumber - Citacion.DayNumber, fila.Indicadores!.DiasTotales);
        Assert.Equal(hoy.DayNumber - Citacion.DayNumber, fila.Indicadores.DiasTramite);
        Assert.Equal(0, (await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery()))[Bandeja.PorPreparar]);
        Assert.Equal(1, (await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery()))[Bandeja.Cerradas]);
    }

    [Fact]
    public async Task Processing_days_stay_frozen_after_closure_while_the_clock_moves_on()
    {
        var id = await Registrar();
        await _db.NewService().CambiarDecisionAsync(id, Decision.Otorgado, "javiera");
        var antes = (await Fila(id)).Indicadores!.DiasTramite;

        _db.AdvanceClock(TimeSpan.FromDays(30));

        Assert.Equal(antes, (await Fila(id)).Indicadores!.DiasTramite);
    }
}
