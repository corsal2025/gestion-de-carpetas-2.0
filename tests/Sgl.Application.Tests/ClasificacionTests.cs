using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

public sealed class ClasificacionTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static readonly DateTime Antes = new(2025, 1, 10, 9, 0, 0, DateTimeKind.Utc);

    private (Guid A, Guid B) SeedAgenda(bool conHistorialPrevio = false)
    {
        using var ctx = _db.NewContext();
        var juan = new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ");
        var ana = new Ciudadano(Rut.Parse("11111111-1"), "ANA", "SOTO");
        if (conHistorialPrevio)
        {
            ctx.Carpetas.Add(Carpeta.Registrar(juan, Sede.Placilla, new DateOnly(2019, 3, 1), "x", Antes));
            ctx.Carpetas.Add(Carpeta.Registrar(juan, Sede.MercadoPuerto, new DateOnly(2021, 6, 2), "x", Antes));
        }
        var a = Carpeta.CrearDesdeAgenda(juan, Sede.AvArgentina, new DateOnly(2026, 5, 4), new TimeOnly(9, 0), TramiteAgenda.Renovacion, "imp", Antes);
        var b = Carpeta.CrearDesdeAgenda(ana, Sede.AvArgentina, new DateOnly(2026, 5, 4), new TimeOnly(8, 30), TramiteAgenda.PrimeraVezExtension, "imp", Antes);
        ctx.Carpetas.AddRange(a, b);
        ctx.SaveChanges();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task ClasificarAsync_batch_moves_all_out_of_PorClasificar()
    {
        var (a, b) = SeedAgenda();

        await _db.NewService().ClasificarAsync(new ClasificarCommand([a, b], EstadoCarpeta.SubidaConaset, null, null, "javiera"));

        var conteos = await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery());
        Assert.Equal(0, conteos[Bandeja.PorClasificar]);
        Assert.Equal(2, conteos[Bandeja.SubidaConaset]);
        var det = await _db.NewService().ObtenerAsync(a);
        Assert.Contains(det.Historial, h => h.Campo == "Estado" && h.Anterior == "PorClasificar" && h.Usuario == "javiera");
    }

    [Fact]
    public async Task ClasificarAsync_single_with_fecha_and_comuna()
    {
        var (a, _) = SeedAgenda();

        await _db.NewService().ClasificarAsync(new ClasificarCommand([a], EstadoCarpeta.CambioDomicilio, "10/02/2024", "QUILPUÉ", "javiera"));

        var det = await _db.NewService().ObtenerAsync(a);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, det.Estado);
        Assert.Equal("10/02/2024", det.FechaUltimaCarpeta);
        Assert.Equal("QUILPUÉ", det.Comuna);
    }

    [Fact]
    public async Task ClasificarAsync_is_atomic_when_one_carpeta_is_not_PorClasificar()
    {
        var (a, b) = SeedAgenda();
        await _db.NewService().ClasificarAsync(new ClasificarCommand([b], EstadoCarpeta.NoExisteCarpeta, null, null, "javiera"));

        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().ClasificarAsync(new ClasificarCommand([a, b], EstadoCarpeta.SubidaConaset, null, null, "javiera")));

        Assert.Equal(EstadoCarpeta.PorClasificar, (await _db.NewService().ObtenerAsync(a)).Estado);
    }

    [Fact]
    public async Task ListarAsync_PorClasificar_returns_agenda_fields_and_previous_tramite_hint()
    {
        SeedAgenda(conHistorialPrevio: true);

        var res = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.PorClasificar, Take: 50));

        Assert.Equal(2, res.Total);
        Assert.Equal(new TimeOnly(8, 30), res.Items[0].HoraCita);
        var juan = res.Items.Single(i => i.TramiteAgenda == TramiteAgenda.Renovacion);
        Assert.Equal(new TimeOnly(9, 0), juan.HoraCita);
        Assert.NotNull(juan.TramitePrevio);
        Assert.Equal(2, juan.TramitePrevio!.Cantidad);
        Assert.Equal(Sede.MercadoPuerto, juan.TramitePrevio.UltimaSede);
        Assert.Equal(new DateOnly(2021, 6, 2), juan.TramitePrevio.UltimaFecha);
        Assert.Null(res.Items.Single(i => i.TramiteAgenda == TramiteAgenda.PrimeraVezExtension).TramitePrevio);
    }
}
