using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Tests;

public class TextNormalizationTests
{
    [Theory]
    [InlineData("VIÑA DEL MAR", "vina del mar")]
    [InlineData("  Valparaíso  ", "valparaiso")]
    [InlineData("QUILPUÉ", "quilpue")]
    [InlineData("Estado  de   la\tCarpeta", "estado de la carpeta")]
    [InlineData("ÑUÑOA", "nunoa")]
    public void Fold_baja_a_minusculas_saca_tildes_y_colapsa_espacios(string entrada, string esperado)
    {
        Assert.Equal(esperado, TextNormalization.Fold(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fold_de_vacio_es_cadena_vacia(string? entrada)
    {
        Assert.Equal(string.Empty, TextNormalization.Fold(entrada));
    }
}
