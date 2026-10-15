using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class ValidacionesRonda4Tests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("vina del mar", "VIÑA DEL MAR")]
    [InlineData("  Quilpue ", "QUILPUÉ")]
    [InlineData("VALPARAISO", "VALPARAÍSO")]
    public void AsignarComuna_accepts_any_case_and_accents_and_stores_the_official_name(string escrito, string oficial)
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.CambioDomicilio);

        c.AsignarComuna(escrito, "javiera", Now);

        Assert.Equal(oficial, c.Comuna);
    }

    [Fact]
    public void AsignarComuna_rejects_an_unknown_comuna()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.CambioDomicilio);

        var ex = Assert.Throws<DomainException>(() => c.AsignarComuna("Springfield", "javiera", Now));

        Assert.Contains("comuna", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(c.Comuna);
    }

    [Fact]
    public void Comunas_list_has_the_346_comunas()
    {
        Assert.Equal(346, ComunasChile.Todas.Count);
    }

    [Fact]
    public void AsignarCaja_rejects_labels_longer_than_the_column()
    {
        var c = CarpetaFactory.New();

        Assert.Throws<DomainException>(() => c.AsignarCaja(new string('X', Carpeta.MaxCajaLength + 1), "javiera", Now));
        c.AsignarCaja(new string('X', Carpeta.MaxCajaLength), "javiera", Now);
        Assert.Equal(Carpeta.MaxCajaLength, c.CajaArchivo!.Length);
    }
}
