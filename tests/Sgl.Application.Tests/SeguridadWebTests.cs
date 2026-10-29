using Microsoft.AspNetCore.Http;
using Sgl.Web.Services;

namespace Sgl.Application.Tests;

public sealed class SeguridadWebTests
{
    [Theory]
    [InlineData(null, "localhost:5030", true)]
    [InlineData("", "localhost:5030", true)]
    [InlineData("http://localhost:5030", "localhost:5030", true)]
    [InlineData("HTTP://LOCALHOST:5030", "localhost:5030", true)]
    [InlineData("http://localhost:5031", "localhost:5030", false)]
    [InlineData("http://localhost.evil.com", "localhost:5030", false)]
    [InlineData("http://evil.com/localhost", "localhost:5030", false)]
    [InlineData("http://evil.com", "localhost:5030", false)]
    [InlineData("https://sgl.muni.cl", "sgl.muni.cl", true)]
    [InlineData("null", "localhost:5030", false)]
    [InlineData("javascript://localhost:5030", "localhost:5030", false)]
    public void Origin_se_compara_de_forma_exacta(string? origin, string host, bool esperado) =>
        Assert.Equal(esperado, SeguridadWeb.OrigenPermitido(origin, host));

    [Theory]
    [InlineData(1, true)]
    [InlineData(3650, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(3651, false)]
    [InlineData(int.MaxValue, false)]
    public void Dias_debe_estar_entre_1_y_3650(int dias, bool valido) =>
        Assert.Equal(valido, SeguridadWeb.ValidarFiltroEstadisticas(dias, null, null) is null);

    [Fact]
    public void Sin_dias_ni_fechas_es_valido() =>
        Assert.Null(SeguridadWeb.ValidarFiltroEstadisticas(null, null, null));

    [Fact]
    public void Desde_posterior_a_hasta_se_rechaza()
    {
        Assert.NotNull(SeguridadWeb.ValidarFiltroEstadisticas(30, new DateOnly(2026, 5, 2), new DateOnly(2026, 5, 1)));
        Assert.Null(SeguridadWeb.ValidarFiltroEstadisticas(30, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public async Task Middleware_agrega_las_cabeceras_de_seguridad()
    {
        var app = new Microsoft.AspNetCore.Builder.ApplicationBuilder(new EmptyServiceProvider());
        app.UseCabecerasSeguridad();
        var handler = app.Build();
        var context = new DefaultHttpContext();

        await handler(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
        Assert.Equal("same-origin", context.Response.Headers["Referrer-Policy"]);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
