using Microsoft.Extensions.Logging.Abstractions;
using Sgl.Domain;
using Sgl.Web.Services;

namespace Sgl.Application.Tests;

public sealed class ErroresUiYAutorTests
{
    [Fact]
    public void Error_de_negocio_se_muestra_tal_cual()
    {
        var msg = ErroresUi.Mensaje(new DomainException("El RUT no es válido."), NullLogger.Instance, "x");
        Assert.Equal("El RUT no es válido.", msg);
    }

    [Fact]
    public void Error_inesperado_no_filtra_su_detalle()
    {
        var msg = ErroresUi.Mensaje(new InvalidOperationException("SQLite Error 5: 'database is locked' /datos/sgl.db"), NullLogger.Instance, "x");
        Assert.Equal(ErroresUi.Generico, msg);
        Assert.DoesNotContain("SQLite", msg);
    }

    [Fact]
    public void Autor_sin_autenticar_usa_el_nombre_ingresado()
    {
        var a = new AutorState();
        Assert.Equal(AutorState.Predeterminado, a.Nombre);
        a.Nombre = "  Ana  ";
        Assert.Equal("Ana", a.Nombre);
    }

    [Fact]
    public void Autor_autenticado_manda_y_no_se_puede_cambiar_a_mano()
    {
        var a = new AutorState();
        a.FijarAutenticado("MUNI-ana");
        a.Nombre = "Otro";
        a.Ingresado = "Otro";
        Assert.Equal("MUNI-ana", a.Nombre);
        Assert.True(a.HasIngresado);
    }
}
