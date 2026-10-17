using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

/// <summary>Top-bar search over every carpeta (any tray, also cancelled) with the tray where each one is now.</summary>
public sealed class BusquedaGlobalTests : IDisposable
{
    private static readonly DateOnly Hoy = new(2026, 5, 4); // TestDb clock (Chile date)
    private static readonly DateTime Ayer = new(2026, 5, 3, 15, 0, 0, DateTimeKind.Utc);
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private Guid Agenda(string rut, string nombre, string apellido = "PRUEBA", DateOnly? cita = null)
    {
        using var ctx = _db.NewContext();
        var ciudadano = ctx.Ciudadanos.AsEnumerable().FirstOrDefault(c => c.Rut.Value == rut) ?? new Ciudadano(Rut.Parse(rut), nombre, apellido);
        var c = Carpeta.CrearDesdeAgenda(ciudadano, Sede.AvArgentina, cita ?? Hoy, null, TramiteAgenda.Renovacion, "imp", Ayer);
        ctx.Carpetas.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    private Task<IReadOnlyList<ResultadoBusquedaGlobalDto>> Buscar(string texto) => _db.NewService().BuscarGlobalAsync(texto);

    [Theory]
    [InlineData("12.345.678-5")]
    [InlineData("12345678-5")]
    [InlineData("123456785")]
    [InlineData("12.345.678")]
    public async Task Finds_by_rut_with_or_without_dots_and_dash(string texto)
    {
        var id = Agenda("12345678-5", "JUAN");
        Agenda("11111111-1", "ANA");

        var r = await Buscar(texto);

        Assert.Equal([id], r.Select(x => x.Id));
    }

    [Fact]
    public async Task Finds_by_name_ignoring_case_and_accents()
    {
        var id = Agenda("12345678-5", "JOSÉ", "MUÑOZ");
        Agenda("11111111-1", "ANA");

        var r = await Buscar("jose munoz");

        Assert.Equal([id], r.Select(x => x.Id));
        Assert.Equal("JOSÉ MUÑOZ", r[0].NombreCompleto);
        Assert.Equal("12.345.678-5", r[0].Rut);
    }

    [Fact]
    public async Task Reports_the_tray_with_the_Revision_classification()
    {
        var limpia = Agenda("12345678-5", "JUAN");
        var dup1 = Agenda("11111111-1", "ANA");
        var dup2 = Agenda("11111111-1", "ANA", cita: Hoy.AddDays(-3));
        var citada = await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand("66666666-6", "RAUL", "PRUEBA", Sede.Placilla, Hoy, "ana"));

        var r = (await Buscar("prueba")).ToDictionary(x => x.Id);

        Assert.Equal(Bandeja.PorClasificar, r[limpia].Bandeja);
        Assert.Equal(Bandeja.RevisionDuplicadas, r[dup1].Bandeja);
        Assert.Equal(Bandeja.RevisionDuplicadas, r[dup2].Bandeja);
        Assert.Equal(Bandeja.ParaRevision, r[citada].Bandeja);
        Assert.All(r.Values, x => Assert.False(x.Anulada));
    }

    [Fact]
    public async Task Includes_cancelled_carpetas_marked_as_such()
    {
        var dup1 = Agenda("11111111-1", "ANA");
        var dup2 = Agenda("11111111-1", "ANA", cita: Hoy.AddDays(-3));
        await _db.NewService().AnularAsync(dup2, MotivosAnulacion.Duplicado, "javiera");

        var r = (await Buscar("11111111-1")).ToDictionary(x => x.Id);

        Assert.Equal(2, r.Count);
        Assert.True(r[dup2].Anulada);
        Assert.Equal(Bandeja.PorClasificar, r[dup1].Bandeja);
    }

    [Fact]
    public async Task Returns_at_most_twenty_matches()
    {
        for (var i = 0; i < 25; i++)
        {
            Agenda("12345678-5", "JUAN", cita: Hoy.AddDays(-i));
        }

        var r = await Buscar("prueba");

        Assert.Equal(BusquedaGlobal.Limite, r.Count);
        Assert.Equal(20, BusquedaGlobal.Limite);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ju")]
    public async Task Short_or_empty_text_returns_nothing(string texto)
    {
        Agenda("12345678-5", "JUAN");

        Assert.Empty(await Buscar(texto));
    }
}
