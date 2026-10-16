using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Tests;

public sealed class NombreMayusculasTests
{
    [Theory]
    [InlineData("juan pérez", "JUAN PÉREZ")]
    [InlineData("  María Ñuñez  ", "MARÍA ÑUÑEZ")]
    public void ElNombreSiempreQuedaEnMayusculas(string entrada, string esperado)
    {
        var p = new Peticion { NombreCompleto = entrada, Rut = "1-9", Comuna = "MAIPU" };
        Assert.Equal(esperado, p.NombreCompleto);
    }
}
