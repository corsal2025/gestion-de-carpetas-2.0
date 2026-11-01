using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>Asistencia del día, criterio 6: el RUT se formatea al tipearlo y un DV que no calza se informa sin buscar.</summary>
public class RutFormatoEntradaTests
{
    [Theory]
    [InlineData("123456785", "12.345.678-5")]
    [InlineData("12345678-5", "12.345.678-5")]
    [InlineData("12.345.678-5", "12.345.678-5")]
    [InlineData("98765432", "9.876.543-2")]
    [InlineData("1", "1")]
    [InlineData("12", "1-2")]
    [InlineData("10000013k", "10.000.013-K")]
    [InlineData("  12 345 678 5  ", "12.345.678-5")]
    [InlineData("abc", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("1234567890123", "123.456.789-0")]
    public void FormatearEntrada_PoneLosPuntosYElGuion(string? entrada, string esperado) =>
        Assert.Equal(esperado, Rut.FormatearEntrada(entrada));

    [Fact]
    public void Validar_RutCorrecto_NoHayError()
    {
        var error = Rut.Validar("12.345.678-5", out var rut);

        Assert.Null(error);
        Assert.Equal("12345678-5", rut!.Value);
    }

    [Fact]
    public void Validar_DvQueNoCalza_InformaElDigitoEsperadoYNoDevuelveRut()
    {
        var error = Rut.Validar("12.345.678-9", out var rut);

        Assert.Null(rut);
        Assert.Contains("dígito verificador", error);
        Assert.Contains("5", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validar_Vacio_PideEscribirUnRut(string? entrada)
    {
        Assert.NotNull(Rut.Validar(entrada, out var rut));
        Assert.Null(rut);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("12.34A.678-5")]
    [InlineData("1234567890123-1")]
    public void Validar_FormatoImposible_DaError(string entrada)
    {
        Assert.NotNull(Rut.Validar(entrada, out var rut));
        Assert.Null(rut);
    }
}
