using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class ModificarNombreTests
{
    private static readonly DateTime At = new(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ModificarNombre_stores_uppercase_trimmed_and_audits_with_autor()
    {
        var carpeta = CarpetaFactory.New();
        var anterior = carpeta.Ciudadano.NombreCompleto;

        carpeta.ModificarNombre("  maría josé  peña soto ", "ana", At);

        Assert.Equal("MARÍA JOSÉ", carpeta.Ciudadano.Nombre);
        Assert.Equal("PEÑA SOTO", carpeta.Ciudadano.Apellido);
        Assert.Equal("maria jose pena soto", carpeta.Ciudadano.NombreBusqueda);
        var entry = carpeta.Historial.Last();
        Assert.Equal("Nombre", entry.Campo);
        Assert.Equal(anterior, entry.Anterior);
        Assert.Equal("MARÍA JOSÉ PEÑA SOTO", entry.Nuevo);
        Assert.Equal("ana", entry.Usuario);
        Assert.Equal(At, entry.Fecha);
    }

    [Fact]
    public void ModificarNombre_with_same_name_does_not_audit()
    {
        var carpeta = CarpetaFactory.New();
        var count = carpeta.Historial.Count;

        carpeta.ModificarNombre(carpeta.Ciudadano.NombreCompleto.ToLowerInvariant(), "ana", At);

        Assert.Equal(count, carpeta.Historial.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ModificarNombre_rejects_empty(string nombre)
    {
        var carpeta = CarpetaFactory.New();
        Assert.Throws<DomainException>(() => carpeta.ModificarNombre(nombre, "ana", At));
    }

    [Fact]
    public void ModificarNombre_rejects_too_long()
    {
        var carpeta = CarpetaFactory.New();
        var largo = new string('A', Ciudadano.MaxNameLength + 1) + " PEREZ SOTO";
        Assert.Throws<DomainException>(() => carpeta.ModificarNombre(largo, "ana", At));
    }

    [Fact]
    public void ModificarNombre_requires_autor()
    {
        var carpeta = CarpetaFactory.New();
        Assert.Throws<DomainException>(() => carpeta.ModificarNombre("JUAN PEREZ SOTO", " ", At));
    }
}
