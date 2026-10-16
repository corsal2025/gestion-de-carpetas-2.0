using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class CiudadanoNombreTests
{
    private static readonly Rut UnRut = Rut.Parse("12345678-5");

    [Fact]
    public void Constructor_stores_names_in_uppercase_keeping_accents()
    {
        var c = new Ciudadano(UnRut, " maría josé ", "peña álvarez");

        Assert.Equal("MARÍA JOSÉ", c.Nombre);
        Assert.Equal("PEÑA ÁLVAREZ", c.Apellido);
        Assert.Equal("maria jose pena alvarez", c.NombreBusqueda);
    }

    [Theory]
    [InlineData("María Aidée Pradenas Tiznado", "MARÍA AIDÉE", "PRADENAS TIZNADO")]
    [InlineData("Bryam Bravo", "BRYAM", "BRAVO")]
    [InlineData("Ismael", "ISMAEL", "SIN APELLIDO")]
    public void Separar_takes_the_last_two_words_as_surnames(string completo, string nombre, string apellido)
    {
        Assert.Equal((nombre, apellido), Ciudadano.Separar(completo));
    }

    [Fact]
    public void Separar_without_text_gives_placeholders()
    {
        Assert.Equal(("SIN NOMBRE", "SIN APELLIDO"), Ciudadano.Separar("   "));
    }

    [Fact]
    public void NormalizarNombre_splits_the_vecino_placeholder_and_is_idempotent()
    {
        var c = new Ciudadano(UnRut, "Jose Angel Sarmiento Saenz", "Vecino");

        var primera = c.NormalizarNombre();
        var segunda = c.NormalizarNombre();

        Assert.True(primera);
        Assert.False(segunda);
        Assert.Equal("JOSE ANGEL", c.Nombre);
        Assert.Equal("SARMIENTO SAENZ", c.Apellido);
        Assert.Equal("jose angel sarmiento saenz", c.NombreBusqueda);
    }

    [Fact]
    public void NormalizarNombre_leaves_a_clean_name_untouched()
    {
        var c = new Ciudadano(UnRut, "MAURICIO", "SALAZAR");

        Assert.False(c.NormalizarNombre());
        Assert.Equal("MAURICIO", c.Nombre);
        Assert.Equal("SALAZAR", c.Apellido);
    }
}
