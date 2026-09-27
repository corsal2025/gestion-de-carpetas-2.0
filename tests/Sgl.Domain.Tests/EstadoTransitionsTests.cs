using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class EstadoTransitionsTests
{
    // Exactly the edges of the "Flujo de carpeta" stateDiagram in docs/arquitectura.html.
    private static readonly (EstadoCarpeta From, EstadoCarpeta To)[] ValidEdges =
    [
        (EstadoCarpeta.Citada, EstadoCarpeta.Revision),
        (EstadoCarpeta.Citada, EstadoCarpeta.PrimeraLicencia),
        (EstadoCarpeta.Citada, EstadoCarpeta.CambioDomicilio),
        (EstadoCarpeta.Revision, EstadoCarpeta.Alertada),
        (EstadoCarpeta.Revision, EstadoCarpeta.SubidaConaset),
        (EstadoCarpeta.SubidaConaset, EstadoCarpeta.EsperaExamen),
        (EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen),
        (EstadoCarpeta.CambioDomicilio, EstadoCarpeta.SubidaConaset),
        (EstadoCarpeta.EsperaExamen, EstadoCarpeta.ClasePendiente),
        (EstadoCarpeta.EsperaExamen, EstadoCarpeta.Otorgado),
        (EstadoCarpeta.EsperaExamen, EstadoCarpeta.ParaDenegar),
        (EstadoCarpeta.ParaDenegar, EstadoCarpeta.Denegado),
        (EstadoCarpeta.Alertada, EstadoCarpeta.ParaDenegar),
    ];

    public static TheoryData<EstadoCarpeta, EstadoCarpeta> ValidPairs()
    {
        var data = new TheoryData<EstadoCarpeta, EstadoCarpeta>();
        foreach (var (from, to) in ValidEdges)
        {
            data.Add(from, to);
        }
        return data;
    }

    public static TheoryData<EstadoCarpeta, EstadoCarpeta> InvalidPairs()
    {
        var data = new TheoryData<EstadoCarpeta, EstadoCarpeta>();
        foreach (var from in Enum.GetValues<EstadoCarpeta>())
        {
            foreach (var to in Enum.GetValues<EstadoCarpeta>())
            {
                if (!ValidEdges.Contains((from, to)))
                {
                    data.Add(from, to);
                }
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ValidPairs))]
    public void Valid_transition_is_allowed(EstadoCarpeta from, EstadoCarpeta to)
    {
        Assert.True(EstadoTransitions.CanTransition(from, to));
    }

    [Theory]
    [MemberData(nameof(InvalidPairs))]
    public void Invalid_transition_is_rejected(EstadoCarpeta from, EstadoCarpeta to)
    {
        Assert.False(EstadoTransitions.CanTransition(from, to));
    }

    [Theory]
    [MemberData(nameof(ValidPairs))]
    public void Carpeta_applies_valid_transition(EstadoCarpeta from, EstadoCarpeta to)
    {
        var carpeta = CarpetaFactory.InEstado(from);

        carpeta.CambiarEstado(to, "tester", CarpetaFactory.Now);

        Assert.Equal(to, carpeta.Estado);
    }

    [Theory]
    [MemberData(nameof(InvalidPairs))]
    public void Carpeta_rejects_invalid_transition_with_domain_error(EstadoCarpeta from, EstadoCarpeta to)
    {
        var carpeta = CarpetaFactory.InEstado(from);

        Assert.Throws<InvalidTransitionException>(() => carpeta.CambiarEstado(to, "tester", CarpetaFactory.Now));
        Assert.Equal(from, carpeta.Estado);
    }

    [Theory]
    [InlineData(EstadoCarpeta.Otorgado)]
    [InlineData(EstadoCarpeta.Denegado)]
    public void Final_states_have_no_next_states(EstadoCarpeta estado)
    {
        Assert.Empty(EstadoTransitions.NextStates(estado));
        Assert.True(EstadoTransitions.IsFinal(estado));
    }

    [Fact]
    public void NextStates_of_Citada_are_the_three_diagram_branches()
    {
        Assert.Equal(
            [EstadoCarpeta.Revision, EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.CambioDomicilio],
            EstadoTransitions.NextStates(EstadoCarpeta.Citada));
    }
}
