using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

/// <summary>Phase 3 use cases: R6 default classification, R7 stage date, R10 listing + print mark, R11 undo.</summary>
public sealed class Fase3ServicioTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static readonly DateTime Antes = new(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);

    private Guid SeedAgenda(string rut = "12345678-5", string nombre = "JUAN", string apellido = "PEREZ", string? fechaUltima = null)
    {
        using var ctx = _db.NewContext();
        var persona = new Ciudadano(Rut.Parse(rut), nombre, apellido);
        var c = Carpeta.CrearDesdeAgenda(persona, Sede.AvArgentina, new DateOnly(2026, 5, 4), new TimeOnly(9, 0), TramiteAgenda.Renovacion, "imp", Antes);
        ctx.Carpetas.Add(c);
        ctx.SaveChanges();
        if (fechaUltima is not null)
        {
            using var ctx2 = _db.NewContext();
            var cargada = ctx2.Carpetas.Single(x => x.Id == c.Id);
            cargada.ModificarFechaUltimaCarpeta(fechaUltima, "imp", Antes);
            ctx2.SaveChanges();
        }
        return c.Id;
    }

    // ---- R6 ----

    [Theory]
    [InlineData("30-06-2023", EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData("01-07-2023", EstadoCarpeta.SeEncuentraEnOf43)]
    public async Task Clasificar_without_state_computes_the_destination_from_the_date(string fecha, EstadoCarpeta esperado)
    {
        var id = SeedAgenda();

        var r = await _db.NewService().ClasificarAsync(new ClasificarCommand([id], null, ClaseLicencia.B, fecha, null, "javiera"));

        var det = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(esperado, det.Estado);
        Assert.Equal(esperado, Assert.Single(r.Aplicadas).Destino);
        Assert.Equal([id], r.IdsEn(esperado));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", det.FechaUltimaCarpeta);
    }

    [Fact]
    public async Task Clasificar_with_an_explicit_state_ignores_the_date_default()
    {
        var id = SeedAgenda();

        await _db.NewService().ClasificarAsync(new ClasificarCommand([id], EstadoCarpeta.PrimeraLicencia, ClaseLicencia.B, "01-01-2019", null, "javiera"));

        Assert.Equal(EstadoCarpeta.PrimeraLicencia, (await _db.NewService().ObtenerAsync(id)).Estado);
    }

    [Fact]
    public async Task Clasificar_without_state_and_without_date_cannot_be_applied()
    {
        var id = SeedAgenda();

        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().ClasificarAsync(new ClasificarCommand([id], null, ClaseLicencia.B, null, null, "javiera")));

        Assert.Equal(EstadoCarpeta.PorClasificar, (await _db.NewService().ObtenerAsync(id)).Estado);
    }

    [Fact]
    public async Task Batch_without_state_uses_each_stored_date_and_is_all_or_nothing()
    {
        var conFecha = SeedAgenda("11111111-1", "ANA", "SOTO", "2020-01-01");
        var sinFecha = SeedAgenda("22222222-2", "LUIS", "DIAZ");

        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().ClasificarAsync(new ClasificarCommand([conFecha, sinFecha], null, ClaseLicencia.B, null, null, "javiera")));
        Assert.Equal(EstadoCarpeta.PorClasificar, (await _db.NewService().ObtenerAsync(conFecha)).Estado);

        var r = await _db.NewService().ClasificarAsync(new ClasificarCommand([conFecha], null, ClaseLicencia.B, null, null, "javiera"));
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, Assert.Single(r.Aplicadas).Destino);
    }

    // ---- R7 ----

    [Fact]
    public async Task Listing_exposes_the_stage_date_and_it_moves_with_the_state()
    {
        var id = SeedAgenda();
        var antes = (await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.PorClasificar, Take: 10))).Items.Single();
        Assert.Equal(Antes, antes.FechaEstadoActual);

        _db.AdvanceClock(TimeSpan.FromHours(30));
        await _db.NewService().ClasificarAsync(new ClasificarCommand([id], null, ClaseLicencia.B, "2019-01-01", null, "javiera"));

        var despues = (await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.SeEncuentraEnArchivos, Take: 10))).Items.Single();
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, despues.FechaEstadoActual);
    }

    [Fact]
    public async Task Upload_stamps_closing_date_in_the_tray_and_never_for_search_states()
    {
        var id = SeedAgenda();
        await _db.NewService().ClasificarAsync(new ClasificarCommand([id], null, ClaseLicencia.B, "2019-01-01", null, "javiera"));
        var buscando = (await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.SeEncuentraEnArchivos, Take: 10))).Items.Single();
        Assert.Null(buscando.FechaCierre);
        Assert.Null(buscando.FechaSubida);

        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.SubidaConaset, "ana");

        var subida = (await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.SubidaConaset, Take: 10))).Items.Single();
        var hoy = HoraChile.Fecha(_db.Clock.GetUtcNow().UtcDateTime);
        Assert.Equal(hoy, subida.FechaCierre);
        Assert.Equal(hoy, subida.FechaSubida);
        Assert.Equal(hoy, subida.Indicadores!.FechaCierre);
    }

    // ---- R11 ----

    [Fact]
    public async Task Undo_restores_the_state_and_audits_it()
    {
        var id = SeedAgenda();
        await _db.NewService().ClasificarAsync(new ClasificarCommand([id], null, ClaseLicencia.B, "2019-01-01", null, "javiera"));
        var punto = await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.SubidaConaset, "ana");

        await _db.NewService().DeshacerEstadoAsync(id, punto, EstadoCarpeta.SubidaConaset, "ana");

        var det = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, det.Estado);
        Assert.Null(det.FechaSubida);
        Assert.Null(det.FechaCierre);
        Assert.Contains(det.Historial, h => h.Campo == "Deshacer");
    }

    [Fact]
    public async Task Undo_is_refused_when_another_change_happened_in_between()
    {
        var id = SeedAgenda();
        await _db.NewService().ClasificarAsync(new ClasificarCommand([id], null, ClaseLicencia.B, "2019-01-01", null, "javiera"));
        var punto = await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.SubidaConaset, "ana");
        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.NoExisteCarpeta, "pedro");

        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().DeshacerEstadoAsync(id, punto, EstadoCarpeta.SubidaConaset, "ana"));

        Assert.Equal(EstadoCarpeta.NoExisteCarpeta, (await _db.NewService().ObtenerAsync(id)).Estado);
    }

    // ---- R10 ----

    private async Task<Guid> EnArchivosAsync(string rut, string nombre, string apellido, string fecha)
    {
        var id = SeedAgenda(rut, nombre, apellido);
        await _db.NewService().ClasificarAsync(new ClasificarCommand([id], null, ClaseLicencia.B, fecha, null, "javiera"));
        return id;
    }

    [Fact]
    public async Task Listing_has_folio_chile_emission_total_and_is_ordered_by_last_folder_date()
    {
        var reciente = await EnArchivosAsync("11111111-1", "ANA", "ZAMORA", "2023-06-01");
        var antigua = await EnArchivosAsync("22222222-2", "LUIS", "ARAYA", "2019-02-02");
        var media = await EnArchivosAsync("33333333-3", "MARTA", "LIRA", "2021-03-03");
        await EnOf43Async();

        var l = await _db.NewService().ObtenerListadoBusquedaAsync(Bandeja.SeEncuentraEnArchivos, new ListarCarpetasQuery());

        Assert.Equal([antigua, media, reciente], l.Items.Select(i => i.Id).ToArray());
        Assert.Equal(3, l.Total);
        Assert.Equal([1, 2, 3], l.Items.Select(i => i.Numero).ToArray());
        Assert.Equal("Archivo-20260504-0600", l.Folio); // 10:00 UTC is 06:00 in Chile in May (UTC-4, winter time)
        Assert.Equal("02-02-2019", l.Items[0].FechaUltimaCarpeta);
    }

    private async Task EnOf43Async()
    {
        var id = SeedAgenda("44444444-4", "OTRO", "NOMBRE");
        await _db.NewService().ClasificarAsync(new ClasificarCommand([id], null, ClaseLicencia.B, "2024-01-01", null, "javiera"));
    }

    [Fact]
    public async Task Listing_can_be_limited_to_selected_ids_and_rejects_other_trays()
    {
        var a = await EnArchivosAsync("11111111-1", "ANA", "ZAMORA", "2023-06-01");
        await EnArchivosAsync("22222222-2", "LUIS", "ARAYA", "2019-02-02");

        var l = await _db.NewService().ObtenerListadoBusquedaAsync(Bandeja.SeEncuentraEnArchivos, new ListarCarpetasQuery(), [a]);

        Assert.Equal([a], l.Items.Select(i => i.Id).ToArray());
        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().ObtenerListadoBusquedaAsync(Bandeja.SubidaConaset, new ListarCarpetasQuery()));
    }

    [Fact]
    public async Task Marking_printed_is_audited_atomic_and_shown_in_the_listing()
    {
        var a = await EnArchivosAsync("11111111-1", "ANA", "ZAMORA", "2023-06-01");
        var b = await EnArchivosAsync("22222222-2", "LUIS", "ARAYA", "2019-02-02");
        var fuera = SeedAgenda("55555555-5", "NO", "CLASIFICADA");

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().MarcarImpresasAsync([a, fuera], "ana"));
        Assert.Null((await _db.NewService().ObtenerAsync(a)).FechaImpresion);

        await _db.NewService().MarcarImpresasAsync([a, b], "ana");

        var l = await _db.NewService().ObtenerListadoBusquedaAsync(Bandeja.SeEncuentraEnArchivos, new ListarCarpetasQuery());
        Assert.All(l.Items, i => Assert.NotNull(i.FechaImpresion));
        Assert.Contains((await _db.NewService().ObtenerAsync(a)).Historial, h => h.Campo == "FechaImpresion" && h.Usuario == "ana");
    }
}
