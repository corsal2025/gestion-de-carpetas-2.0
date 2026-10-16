using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

public sealed class DuplicadosServiceTests : IDisposable
{
    private const string RutA = "12.345.678-5";
    private const string RutB = "11.111.111-1";

    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private Task<Guid> Registrar(string rut, Sede sede = Sede.AvArgentina, string nombre = "Juan", ClaseLicencia clases = ClaseLicencia.Ninguna) =>
        _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(
            rut, nombre, "Pérez", sede, new DateOnly(2026, 5, 4), "ana", clases, ConfirmarDuplicado: true));

    [Fact]
    public async Task Listar_returns_one_row_per_citizen_with_more_than_one_open_carpeta()
    {
        await Registrar(RutA);
        await Registrar(RutA, Sede.Placilla);
        await Registrar(RutB);

        var result = await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery());

        var row = Assert.Single(result.Items);
        Assert.Equal(1, result.Total);
        Assert.Equal("12.345.678-5", row.Rut);
        Assert.Equal(2, row.CarpetasAbiertas);
    }

    [Fact]
    public async Task Listar_ignores_closed_carpetas()
    {
        await Registrar(RutA);
        var cerrada = await Registrar(RutA);
        await _db.NewService().CambiarDecisionAsync(cerrada, Decision.Denegado, "ana");

        var result = await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery());

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Listar_filters_by_sede_and_text()
    {
        await Registrar(RutA, nombre: "Juan");
        await Registrar(RutA, Sede.Placilla, nombre: "Juan");
        await Registrar(RutB, Sede.MercadoPuerto, nombre: "María");
        await Registrar(RutB, Sede.MercadoPuerto, nombre: "María");

        var svc = _db.NewDuplicadosService();
        Assert.Single((await svc.ListarAsync(new ListarDuplicadosQuery(Sede: Sede.Placilla))).Items);
        Assert.Equal(2, (await svc.ListarAsync(new ListarDuplicadosQuery())).Total);
        var porNombre = await svc.ListarAsync(new ListarDuplicadosQuery(Texto: "maria"));
        Assert.Equal("11.111.111-1", Assert.Single(porNombre.Items).Rut);
        var porRut = await svc.ListarAsync(new ListarDuplicadosQuery(Texto: "12.345"));
        Assert.Equal("12.345.678-5", Assert.Single(porRut.Items).Rut);
    }

    [Fact]
    public async Task Listar_can_skip_citizens_that_already_have_a_carpeta_in_por_clasificar()
    {
        var antes = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        using (var ctx = _db.NewContext())
        {
            // Open agenda carpeta (Por clasificar) + a classified one: the citizen has a red row in the tray.
            var ana = new Ciudadano(Rut.Parse(RutA), "ANA", "SOTO");
            var anaClasificada = Carpeta.CrearDesdeAgenda(ana, Sede.Placilla, new DateOnly(2026, 4, 2), null, TramiteAgenda.Renovacion, "imp", antes);
            anaClasificada.Clasificar(EstadoCarpeta.SeEncuentraEnArchivos, ClaseLicencia.B, null, null, "javiera", antes);
            var anaAgenda = Carpeta.CrearDesdeAgenda(ana, Sede.AvArgentina, new DateOnly(2026, 5, 4), null, TramiteAgenda.Renovacion, "imp", antes);
            // Both carpetas already classified: only reachable from the cases section.
            var beto = new Ciudadano(Rut.Parse(RutB), "BETO", "TORO");
            var b1 = Carpeta.CrearDesdeAgenda(beto, Sede.Placilla, new DateOnly(2026, 4, 2), null, TramiteAgenda.Renovacion, "imp", antes);
            var b2 = Carpeta.CrearDesdeAgenda(beto, Sede.AvArgentina, new DateOnly(2026, 5, 4), null, TramiteAgenda.Renovacion, "imp", antes);
            b1.Clasificar(EstadoCarpeta.SeEncuentraEnArchivos, ClaseLicencia.B, null, null, "javiera", antes);
            b2.Clasificar(EstadoCarpeta.SeEncuentraEnArchivos, ClaseLicencia.B, null, null, "javiera", antes);
            ctx.Carpetas.AddRange(anaClasificada, anaAgenda, b1, b2);
            ctx.SaveChanges();
        }

        var svc = _db.NewDuplicadosService();
        Assert.Equal(2, (await svc.ListarAsync(new ListarDuplicadosQuery())).Total);

        var fuera = await svc.ListarAsync(new ListarDuplicadosQuery(SinCarpetaPorClasificar: true));

        var row = Assert.Single(fuera.Items);
        Assert.Equal(1, fuera.Total);
        Assert.Equal("11.111.111-1", row.Rut);
    }

    [Fact]
    public async Task Listar_orders_by_apellido_then_nombre()
    {
        var z = _db.NewService();
        foreach (var (apellido, nombre, rut) in new[] { ("Zapata", "Luis", RutA), ("Aguilar", "Rosa", RutB) })
        {
            for (var i = 0; i < 2; i++)
            {
                await z.RegistrarAsync(new RegistrarCarpetaCommand(
                    rut, nombre, apellido, Sede.AvArgentina, new DateOnly(2026, 5, 4), "ana", ClaseLicencia.Ninguna, ConfirmarDuplicado: true));
            }
        }

        var result = await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery());

        Assert.Equal(["ROSA AGUILAR", "LUIS ZAPATA"], result.Items.Select(i => i.NombreCompleto));
    }

    [Fact]
    public async Task Listar_returns_requested_range_server_side()
    {
        await Registrar(RutA);
        await Registrar(RutA);
        await Registrar(RutB);
        await Registrar(RutB);

        var page = await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery(Skip: 1, Take: 1));

        Assert.Equal(2, page.Total);
        Assert.Single(page.Items);
        Assert.Equal(2, (await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery())).Items.Count);
    }

    [Fact]
    public async Task ObtenerCarpetas_returns_open_carpetas_of_citizen()
    {
        await Registrar(RutA, clases: ClaseLicencia.B);
        await Registrar(RutA, Sede.Placilla);
        var row = Assert.Single((await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery())).Items);

        var carpetas = await _db.NewDuplicadosService().ObtenerCarpetasAsync(row.CiudadanoId);

        Assert.Equal(2, carpetas.Count);
        Assert.Contains(carpetas, c => c.Clases == ClaseLicencia.B);
        Assert.Contains(carpetas, c => c.Sede == Sede.Placilla);
    }

    [Fact]
    public async Task MarcarRevisado_hides_case_until_new_open_carpeta_is_added()
    {
        await Registrar(RutA);
        await Registrar(RutA);
        var row = Assert.Single((await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery())).Items);
        Assert.Equal(1, await _db.NewDuplicadosService().ContarPendientesAsync());

        _db.AdvanceClock(TimeSpan.FromMinutes(1));
        await _db.NewDuplicadosService().MarcarRevisadoAsync(row.CiudadanoId, "pedro");

        Assert.Empty((await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery())).Items);
        Assert.Equal(0, await _db.NewDuplicadosService().ContarPendientesAsync());

        _db.AdvanceClock(TimeSpan.FromMinutes(1));
        await Registrar(RutA, Sede.Placilla);

        Assert.Equal(1, await _db.NewDuplicadosService().ContarPendientesAsync());
    }

    [Fact]
    public async Task MarcarRevisado_twice_renews_review()
    {
        await Registrar(RutA);
        await Registrar(RutA);
        var row = Assert.Single((await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery())).Items);

        await _db.NewDuplicadosService().MarcarRevisadoAsync(row.CiudadanoId, "pedro");
        await _db.NewDuplicadosService().MarcarRevisadoAsync(row.CiudadanoId, "ana");

        await using var ctx = _db.NewContext();
        var revision = Assert.Single(ctx.RevisionesDuplicado);
        Assert.Equal("ana", revision.RevisadoPor);
    }

    [Fact]
    public async Task MarcarRevisado_requires_author()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewDuplicadosService().MarcarRevisadoAsync(Guid.NewGuid(), " "));
    }

    [Fact]
    public void Carpetas_have_composite_index_for_open_carpetas_by_citizen()
    {
        using var ctx = _db.NewContext();
        var entity = ctx.Model.FindEntityType(typeof(Carpeta))!;

        Assert.Contains(entity.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual([nameof(Carpeta.CiudadanoId), nameof(Carpeta.Decision)]));
    }

    [Fact]
    public async Task ObtenerDetalle_returns_rich_info_per_open_carpeta_including_last_movement()
    {
        var conCaja = await Registrar(RutA);
        await Registrar(RutA, Sede.Placilla);
        await _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(conCaja, "CAJA 7", "beto"));
        var row = Assert.Single((await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery())).Items);

        var detalle = await _db.NewDuplicadosService().ObtenerDetalleCarpetasAsync(row.CiudadanoId);

        Assert.Equal(2, detalle.Count);
        var d = Assert.Single(detalle, x => x.Id == conCaja);
        Assert.Equal("CAJA 7", d.CajaArchivo);
        Assert.Equal(Decision.Pendiente, d.Decision);
        Assert.Equal("beto", d.UltimoMovimientoPor);
        Assert.NotNull(d.UltimoMovimientoFecha);
        Assert.False(d.PendienteBusqueda);
        Assert.Null(d.PendienteBusquedaDesde);
        Assert.Contains(detalle, x => x.Sede == Sede.Placilla && x.CajaArchivo is null);
    }
}
