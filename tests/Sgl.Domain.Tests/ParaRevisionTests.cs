using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>"Para revisión": carpetas left in Citada after the initial classification (no resolution yet).</summary>
public class ParaRevisionTests
{
    [Fact]
    public void Citada_without_resolution_belongs_to_Para_revision()
    {
        var c = CarpetaFactory.New();

        Assert.Equal(Bandeja.ParaRevision, c.Bandeja);
        Assert.Equal(Bandeja.ParaRevision, Carpeta.ClasificarBandeja(EstadoCarpeta.Citada, Decision.Pendiente, null));
    }

    [Fact]
    public void Carpeta_leaves_Para_revision_when_it_gets_a_state()
    {
        var c = CarpetaFactory.New();

        c.CambiarEstado(EstadoCarpeta.PrimeraLicencia, "javiera", CarpetaFactory.Now);

        Assert.Equal(Bandeja.PorPreparar, c.Bandeja);
    }

    [Fact]
    public void Expression_form_matches_the_rule_for_Citada()
    {
        var fn = Carpeta.BandejaExpresion.Compile();
        var c = CarpetaFactory.New();

        Assert.Equal(Bandeja.ParaRevision, fn(c));
    }
}
