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
        (EstadoCarpeta.ClasePendiente, EstadoCarpeta.EsperaExamen),
        (EstadoCarpeta.ClasePendiente, EstadoCarpeta.Otorgado),
        (EstadoCarpeta.ClasePendiente, EstadoCarpeta.ParaDenegar),
        (EstadoCarpeta.ParaDenegar, EstadoCarpeta.Denegado),
        (EstadoCarpeta.Alertada, EstadoCarpeta.ParaDenegar),
        // Manual classification of agenda-imported carpetas.
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.PrimeraLicencia),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.SubidaConaset),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.SubidaConF8),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.CambioDomSubidoConaset),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.CambioDomSubidoCorreo),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.SubidaConOficio),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.SeEncuentraEnArchivos),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.SeEncuentraEnOf43),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.CambioDomicilioSolicitado),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.CambioDomicilio),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.NoExisteCarpeta),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.CrearCertificado),
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.CanjeLicExtranjera),
        // "Dejar para revisión": a carpeta without resolution goes back to Citada (Para revisión).
        (EstadoCarpeta.PorClasificar, EstadoCarpeta.Citada),
        // Phase 3 (R8): physical search flow. Found -> uploaded; not found -> F8.
        (EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.SubidaConaset),
        (EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.NoExisteCarpeta),
        (EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.CrearCertificado),
        (EstadoCarpeta.SeEncuentraEnOf43, EstadoCarpeta.SubidaConaset),
        (EstadoCarpeta.SeEncuentraEnOf43, EstadoCarpeta.NoExisteCarpeta),
        (EstadoCarpeta.SeEncuentraEnOf43, EstadoCarpeta.CrearCertificado),
        // A missing carpeta is uploaded with F8 or with an oficio; it never goes back to the shelves.
        (EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.SubidaConF8),
        (EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.SubidaConOficio),
        // F8 / certificate modules: a requested F8 is uploaded (SubidaConF8) or not generated (Crear certificado),
        // and a signed certificate / oficio uploads the carpeta (SubidaConOficio).
        (EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.CrearCertificado),
        (EstadoCarpeta.CrearCertificado, EstadoCarpeta.SubidaConOficio),
        (EstadoCarpeta.CambioDomicilio, EstadoCarpeta.SubidaConF8),
        (EstadoCarpeta.CambioDomicilio, EstadoCarpeta.CrearCertificado),
        (EstadoCarpeta.CambioDomicilio, EstadoCarpeta.SubidaConOficio),
        (EstadoCarpeta.CambioDomicilio, EstadoCarpeta.CambioDomSubidoConaset),
        (EstadoCarpeta.CambioDomicilio, EstadoCarpeta.NoExisteCarpeta),
        // Mail sync: the request email filed in "CARP. YA SUBIDAS".
        (EstadoCarpeta.CambioDomicilio, EstadoCarpeta.CambioDomSubidoCorreo),
        (EstadoCarpeta.CambioDomicilioSolicitado, EstadoCarpeta.SubidaConF8),
        (EstadoCarpeta.CambioDomicilioSolicitado, EstadoCarpeta.SubidaConOficio),
        (EstadoCarpeta.CambioDomicilioSolicitado, EstadoCarpeta.CrearCertificado),
        (EstadoCarpeta.CambioDomicilioSolicitado, EstadoCarpeta.CambioDomSubidoConaset),
        (EstadoCarpeta.CambioDomicilioSolicitado, EstadoCarpeta.NoExisteCarpeta),
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

    [Fact]
    public void NextStates_of_ClasePendiente_can_resume_or_terminate()
    {
        Assert.Equal(
            [EstadoCarpeta.EsperaExamen, EstadoCarpeta.Otorgado, EstadoCarpeta.ParaDenegar],
            EstadoTransitions.NextStates(EstadoCarpeta.ClasePendiente));
    }
}
