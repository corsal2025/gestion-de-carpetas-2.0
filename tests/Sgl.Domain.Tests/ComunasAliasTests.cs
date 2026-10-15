using Sgl.Domain;

namespace Sgl.Domain.Tests;

public sealed class ComunasAliasTests
{
    [Theory]
    [InlineData("con con", "CONCÓN")]
    [InlineData("LA CALERA", "CALERA")]
    [InlineData("casa blanca", "CASABLANCA")]
    [InlineData("LLAY-LLAY", "LLAILLAY")]
    [InlineData("Llay Llay", "LLAILLAY")]
    public void Normalizar_accepts_common_spelling_variants(string raw, string official)
    {
        Assert.Equal(official, ComunasChile.Normalizar(raw)?.ToUpperInvariant());
    }

    [Fact]
    public void Normalizar_still_rejects_unknown_text()
    {
        Assert.Null(ComunasChile.Normalizar("ZZ NO EXISTE"));
    }
}
