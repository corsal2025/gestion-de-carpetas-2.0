using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Tests;

public class RutValidatorTests
{
    [Theory]
    [InlineData("18785387-7", "18.785.387-7")]
    [InlineData("18.785.387-7", "18.785.387-7")]
    [InlineData("187853877", "18.785.387-7")]
    [InlineData("  18785387-7  ", "18.785.387-7")]
    public void Normaliza_a_forma_canonica_con_puntos(string entrada, string esperado)
    {
        Assert.Equal(esperado, RutValidator.NormalizeAndValidate(entrada));
    }

    [Theory]
    [InlineData("12345678-5", "12.345.678-5")]
    [InlineData("5126663-3", "05.126.663-3")]  // 7 dígitos → se rellena con cero
    public void Rellena_cuerpo_de_siete_digitos(string entrada, string esperado)
    {
        Assert.Equal(esperado, RutValidator.NormalizeAndValidate(entrada));
    }

    [Theory]
    [InlineData("11111111-1", "11.111.111-1")]
    public void Acepta_rut_de_prueba(string entrada, string esperado)
    {
        Assert.Equal(esperado, RutValidator.NormalizeAndValidate(entrada));
    }

    [Fact]
    public void Digito_verificador_K_se_acepta_en_mayuscula_o_minuscula()
    {
        // 12.345.670 tiene DV = K
        Assert.Equal("12.345.670-K", RutValidator.NormalizeAndValidate("12345670-k"));
        Assert.Equal("12.345.670-K", RutValidator.NormalizeAndValidate("12.345.670-K"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es un rut")]
    [InlineData("1")]
    [InlineData("18785387-3")]   // DV incorrecto
    [InlineData("123456789012-5")] // demasiado largo
    [InlineData("RUT INVALIDO")]
    public void Rechaza_entradas_sin_forma_de_rut_o_dv_incorrecto(string? entrada)
    {
        Assert.Null(RutValidator.NormalizeAndValidate(entrada));
    }
}
