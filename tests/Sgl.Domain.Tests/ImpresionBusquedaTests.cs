using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>R10: the search listing can be marked as printed (audited) only while the carpeta is in Archivos / Of. 43.</summary>
public class ImpresionBusquedaTests
{
    private static readonly DateTime Impresa = new(2026, 3, 11, 14, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43)]
    public void MarcarImpresa_records_the_date_and_audits(EstadoCarpeta estado)
    {
        var c = CarpetaFactory.InEstado(estado);

        c.MarcarImpresa("ana", Impresa);

        Assert.Equal(Impresa, c.FechaImpresion);
        Assert.Contains(c.Historial, h => h.Campo == "FechaImpresion" && h.Anterior is null && h.Usuario == "ana");
    }

    [Fact]
    public void MarcarImpresa_again_restamps_and_audits_the_reprint()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);
        c.MarcarImpresa("ana", Impresa);
        var segunda = Impresa.AddDays(1);

        c.MarcarImpresa("pedro", segunda);

        Assert.Equal(segunda, c.FechaImpresion);
        Assert.Equal(2, c.Historial.Count(h => h.Campo == "FechaImpresion"));
    }

    [Theory]
    [InlineData(EstadoCarpeta.Citada)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.SubidaConaset)]
    public void MarcarImpresa_is_rejected_outside_the_search_states(EstadoCarpeta estado)
    {
        var c = CarpetaFactory.InEstado(estado);

        Assert.Throws<DomainException>(() => c.MarcarImpresa("ana", Impresa));
        Assert.Null(c.FechaImpresion);
    }

    [Fact]
    public void Leaving_the_search_states_clears_the_print_date()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);
        c.MarcarImpresa("ana", Impresa);

        c.ForzarEstado(EstadoCarpeta.NoExisteCarpeta, "ana", Impresa.AddHours(1));

        Assert.Null(c.FechaImpresion);
        Assert.Contains(c.Historial, h => h.Campo == "FechaImpresion" && h.Nuevo is null);
    }

    [Fact]
    public void Moving_between_archivos_and_of43_keeps_the_print_date()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);
        c.MarcarImpresa("ana", Impresa);

        c.ForzarEstado(EstadoCarpeta.SeEncuentraEnOf43, "ana", Impresa.AddHours(1));

        Assert.Equal(Impresa, c.FechaImpresion);
    }

    [Fact]
    public void An_annulled_carpeta_cannot_be_marked()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);
        c.Anular("Duplicado", "ana", Impresa);

        Assert.Throws<DomainException>(() => c.MarcarImpresa("ana", Impresa));
    }
}
