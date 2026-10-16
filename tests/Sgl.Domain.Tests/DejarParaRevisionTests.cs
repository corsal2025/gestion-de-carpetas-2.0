using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>"Dejar para revisión" from Por clasificar: no class, no state; the carpeta goes to Para revisión.</summary>
public class DejarParaRevisionTests
{
    [Fact]
    public void Por_clasificar_can_be_left_for_review_without_class_or_state()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.PorClasificar);

        c.DejarParaRevision("javiera", CarpetaFactory.Now);

        Assert.Equal(EstadoCarpeta.Citada, c.Estado);
        Assert.Equal(Bandeja.ParaRevision, c.Bandeja);
    }

    [Fact]
    public void Only_por_clasificar_can_be_left_for_review()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.PrimeraLicencia);

        Assert.Throws<DomainException>(() => c.DejarParaRevision("javiera", CarpetaFactory.Now));
    }
}
