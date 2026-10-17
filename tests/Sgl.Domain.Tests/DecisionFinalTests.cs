using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>Once a carpeta is Otorgado or Denegado its decision is final: no path may change it any more.</summary>
public class DecisionFinalTests
{
    private static readonly DateTime Despues = new(2026, 3, 12, 15, 30, 0, DateTimeKind.Utc);

    private static Carpeta ConDecision(Decision final)
    {
        var c = CarpetaFactory.New();
        c.CambiarDecision(final, "ana", CarpetaFactory.Now);
        return c;
    }

    [Theory]
    [InlineData(Decision.Otorgado, Decision.Pendiente)]
    [InlineData(Decision.Otorgado, Decision.Denegado)]
    [InlineData(Decision.Denegado, Decision.Pendiente)]
    [InlineData(Decision.Denegado, Decision.Otorgado)]
    public void A_final_decision_cannot_be_changed_and_writes_no_history(Decision final, Decision otra)
    {
        var c = ConDecision(final);
        var filas = c.Historial.Count;

        var ex = Assert.Throws<DomainException>(() => c.CambiarDecision(otra, "ana", Despues));

        Assert.Contains("final", ex.Message);
        Assert.Contains(final.ToString(), ex.Message);
        Assert.Equal(final, c.Decision);
        Assert.Equal(filas, c.Historial.Count);
    }

    [Theory]
    [InlineData(Decision.Otorgado)]
    [InlineData(Decision.Denegado)]
    public void Setting_the_same_final_decision_is_a_silent_no_op(Decision final)
    {
        var c = ConDecision(final);
        var filas = c.Historial.Count;

        c.CambiarDecision(final, "ana", Despues);

        Assert.Equal(final, c.Decision);
        Assert.Equal(filas, c.Historial.Count);
    }

    [Theory]
    [InlineData(Decision.Otorgado)]
    [InlineData(Decision.Denegado)]
    public void Pendiente_can_become_final(Decision final)
    {
        var c = CarpetaFactory.New();

        c.CambiarDecision(final, "ana", Despues);

        Assert.Equal(final, c.Decision);
    }

    [Theory]
    [InlineData(Decision.Otorgado)]
    [InlineData(Decision.Denegado)]
    public void ForzarEstado_cannot_silently_reopen_a_final_decision(Decision final)
    {
        var c = ConDecision(final);
        var filas = c.Historial.Count;
        var estado = c.Estado;

        Assert.Throws<DomainException>(() => c.ForzarEstado(EstadoCarpeta.SeEncuentraEnArchivos, "ana", Despues));

        Assert.Equal(final, c.Decision);
        Assert.Equal(estado, c.Estado);
        Assert.Equal(filas, c.Historial.Count);
    }

    [Fact]
    public void ForzarEstado_to_a_state_that_keeps_the_same_final_decision_is_allowed()
    {
        var c = ConDecision(Decision.Otorgado);

        c.ForzarEstado(EstadoCarpeta.Otorgado, "ana", Despues);

        Assert.Equal(EstadoCarpeta.Otorgado, c.Estado);
        Assert.Equal(Decision.Otorgado, c.Decision);
    }

    [Fact]
    public void ForzarEstado_to_the_opposite_final_state_is_refused()
    {
        var c = ConDecision(Decision.Otorgado);

        Assert.Throws<DomainException>(() => c.ForzarEstado(EstadoCarpeta.Denegado, "ana", Despues));

        Assert.Equal(Decision.Otorgado, c.Decision);
    }

    [Fact]
    public void Undoing_the_very_action_that_made_it_final_still_restores_the_previous_decision()
    {
        var c = CarpetaFactory.New();
        var punto = c.PuntoRestauracion();
        c.CambiarDecision(Decision.Otorgado, "ana", Despues);

        c.RestaurarEstado(punto, c.Estado, "ana", Despues);

        Assert.Equal(Decision.Pendiente, c.Decision);
    }
}
