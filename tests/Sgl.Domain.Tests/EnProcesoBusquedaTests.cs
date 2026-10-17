using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class EnProcesoBusquedaTests
{
    private static readonly DateTime Now = CarpetaFactory.Now;

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, true)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43, true)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta, true)]
    [InlineData(EstadoCarpeta.SubidaConaset, false)]
    [InlineData(EstadoCarpeta.CrearCertificado, false)]
    public void PermiteEnProcesoDeBusqueda_is_the_archive_states_plus_NoExiste(EstadoCarpeta estado, bool esperado) =>
        Assert.Equal(esperado, Carpeta.PermiteEnProcesoDeBusqueda(estado));

    [Fact]
    public void PermiteBusquedaPendiente_is_not_widened()
    {
        Assert.False(Carpeta.PermiteBusquedaPendiente(EstadoCarpeta.NoExisteCarpeta));
    }

    [Fact]
    public void Mark_is_allowed_in_NoExisteCarpeta_and_survives_while_it_stays()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.NoExisteCarpeta);

        c.MarcarPendienteBusqueda(true, "ana", Now);

        Assert.True(c.PendienteBusqueda);
    }

    [Fact]
    public void Mark_is_rejected_in_a_state_outside_the_set()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SubidaConaset);

        Assert.Throws<DomainException>(() => c.MarcarPendienteBusqueda(true, "ana", Now));
        Assert.False(c.PendienteBusqueda);
    }

    [Fact]
    public void Mark_is_kept_when_moving_from_Archivos_to_NoExiste_but_the_printed_stamp_is_cleared()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);
        c.MarcarPendienteBusqueda(true, "ana", Now);
        c.MarcarImpresa("ana", Now);

        c.CambiarEstado(EstadoCarpeta.NoExisteCarpeta, "ana", Now);

        Assert.True(c.PendienteBusqueda);
        Assert.Null(c.FechaImpresion);
    }

    [Theory]
    [InlineData(EstadoCarpeta.SubidaConF8)]
    [InlineData(EstadoCarpeta.CrearCertificado)]
    public void Mark_is_cleared_when_leaving_NoExiste_to_a_state_outside_the_set(EstadoCarpeta destino)
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.NoExisteCarpeta);
        c.MarcarPendienteBusqueda(true, "ana", Now);

        c.CambiarEstado(destino, "ana", Now);

        Assert.False(c.PendienteBusqueda);
        Assert.Contains(c.Historial, h => h.Campo == nameof(Carpeta.PendienteBusqueda) && h.Nuevo == "No");
    }

    [Fact]
    public void MarcarImpresa_still_rejects_NoExiste()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.NoExisteCarpeta);

        Assert.Throws<DomainException>(() => c.MarcarImpresa("ana", Now));
    }
}
