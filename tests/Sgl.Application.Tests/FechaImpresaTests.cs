using Sgl.Application.Carpetas;

namespace Sgl.Application.Tests;

public sealed class FechaImpresaTests
{
    [Theory]
    [InlineData("2019-03-20", "20-03-2019")]
    [InlineData("20-03-2019", "20-03-2019")]
    [InlineData("S/C", "S/C")]
    [InlineData("", "S/C")]
    [InlineData(null, "S/C")]
    public void DdMmAaaa_pasa_el_ISO_a_dia_mes_ano_y_deja_el_resto(string? entrada, string esperado) =>
        Assert.Equal(esperado, FechaImpresa.DdMmAaaa(entrada));
}
