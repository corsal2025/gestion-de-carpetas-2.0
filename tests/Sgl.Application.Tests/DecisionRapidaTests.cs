using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

/// <summary>Otorgada / Denegada from the "Por preparar" tray: closes the carpeta with today's date and can be undone.</summary>
public sealed class DecisionRapidaTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    // Registered carpetas start in "Para revisión"; the classification moves them to 1° Licencia.
    private async Task<Guid> RegistrarAsync()
    {
        var id = await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand("12345678-5", "JUAN", "PEREZ", Sede.AvArgentina, new DateOnly(2026, 5, 4), "ana"));
        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.PrimeraLicencia, "ana");
        return id;
    }

    [Theory]
    [InlineData(Decision.Otorgado)]
    [InlineData(Decision.Denegado)]
    public async Task Decision_moves_the_carpeta_to_Cerradas_with_todays_closing_date(Decision decision)
    {
        var id = await RegistrarAsync();
        Assert.Single((await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.PorPreparar, Take: 10))).Items);

        await _db.NewService().CambiarDecisionAsync(id, decision, "ana");

        var cerrada = Assert.Single((await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.Cerradas, Take: 10))).Items);
        Assert.Equal(decision, cerrada.Decision);
        Assert.Equal(HoraChile.Fecha(_db.Clock.GetUtcNow().UtcDateTime), cerrada.FechaCierre);
        Assert.Empty((await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.PorPreparar, Take: 10))).Items);
    }

    [Fact]
    public async Task Decision_can_be_undone_back_to_Por_preparar()
    {
        var id = await RegistrarAsync();
        var antes = await _db.NewService().ObtenerAsync(id);

        var punto = await _db.NewService().CambiarDecisionAsync(id, Decision.Otorgado, "ana");
        await _db.NewService().DeshacerEstadoAsync(id, punto, antes.Estado, "ana");

        var det = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(Decision.Pendiente, det.Decision);
        Assert.Null(det.FechaCierre);
        Assert.Equal(antes.FechaSubida, det.FechaSubida);
        Assert.Single((await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.PorPreparar, Take: 10))).Items);
    }

    [Theory]
    [InlineData(Decision.Otorgado, Decision.Denegado)]
    [InlineData(Decision.Denegado, Decision.Pendiente)]
    public async Task A_final_decision_cannot_be_changed_through_the_service(Decision final, Decision otra)
    {
        var id = await RegistrarAsync();
        await _db.NewService().CambiarDecisionAsync(id, final, "ana");

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().CambiarDecisionAsync(id, otra, "ana"));

        Assert.Equal(final, (await _db.NewService().ObtenerAsync(id)).Decision);
    }
}
