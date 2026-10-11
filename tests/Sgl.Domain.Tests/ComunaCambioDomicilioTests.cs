using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class ComunaCambioDomicilioTests
{
    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilio, true)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado, true)]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset, true)]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo, true)]
    [InlineData(EstadoCarpeta.Citada, false)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, false)]
    [InlineData(EstadoCarpeta.Otorgado, false)]
    public void EsCambioDomicilio_only_for_change_of_address_states(EstadoCarpeta estado, bool esperado)
    {
        Assert.Equal(esperado, Carpeta.EsCambioDomicilio(estado));
    }
}
