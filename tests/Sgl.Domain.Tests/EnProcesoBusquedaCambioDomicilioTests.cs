using Sgl.Domain;

namespace Sgl.Domain.Tests;

public sealed class EnProcesoBusquedaCambioDomicilioTests
{
    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    public void Change_of_address_states_allow_the_search_mark(EstadoCarpeta estado)
    {
        Assert.True(Carpeta.PermiteEnProcesoDeBusqueda(estado));
    }

    [Fact]
    public void Change_of_address_does_not_enable_printed_listing_rules()
    {
        Assert.False(Carpeta.PermiteBusquedaPendiente(EstadoCarpeta.CambioDomicilio));
    }
}
