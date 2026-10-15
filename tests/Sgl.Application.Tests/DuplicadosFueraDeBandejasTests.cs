using Sgl.Application.Carpetas;
using Sgl.Application.Panel;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>Possible duplicates live only in their own module until resolved: never in a tray nor its counter.</summary>
public sealed class DuplicadosFueraDeBandejasTests : IDisposable
{
    private const string RutA = "12.345.678-5";
    private const string RutB = "11.111.111-1";

    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private Task<Guid> Registrar(string rut, Sede sede = Sede.AvArgentina) =>
        _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(
            rut, "Juan", "Pérez", sede, new DateOnly(2026, 5, 4), "ana", ClaseLicencia.Ninguna, ConfirmarDuplicado: true));

    private async Task<int> TotalEnBandejas()
    {
        var counts = await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery());
        return counts.Values.Sum();
    }

    private async Task<int> EnBandejaListada(Bandeja bandeja) =>
        (await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: bandeja))).Items.Count;

    private async Task<Bandeja> BandejaDe(Guid id)
    {
        var item = (await _db.NewService().ListarAsync(new ListarCarpetasQuery())).Items.First(i => i.Id == id);
        return Carpeta.ClasificarBandeja(item.Estado, item.Decision, item.CajaArchivo);
    }

    private async Task<(Guid, Guid)> DosAbiertas()
    {
        var a = await Registrar(RutA);
        var b = await Registrar(RutA, Sede.Placilla);
        return (a, b);
    }

    [Fact]
    public async Task Possible_duplicate_is_excluded_from_tray_counts_and_listings_but_not_from_untrayed_search()
    {
        var (a, _) = await DosAbiertas();
        var bandeja = await BandejaDe(a);

        Assert.Equal(0, await TotalEnBandejas());
        Assert.Equal(0, await EnBandejaListada(bandeja));
        Assert.Empty(await _db.NewService().ListarIdsAsync(new ListarCarpetasQuery(Bandeja: bandeja)));
        Assert.Equal(2, (await _db.NewService().ListarAsync(new ListarCarpetasQuery())).Total);
    }

    [Fact]
    public async Task Mantener_ambas_returns_both_carpetas_to_the_trays()
    {
        var (a, _) = await DosAbiertas();
        var bandeja = await BandejaDe(a);
        _db.AdvanceClock(TimeSpan.FromMinutes(1));
        await _db.NewDuplicadosService().MarcarRevisadoAsync(
            (await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery())).Items.Single().CiudadanoId, "pedro");

        Assert.Equal(2, await TotalEnBandejas());
        Assert.Equal(2, await EnBandejaListada(bandeja));
        Assert.Equal(2, (await _db.NewService().ListarIdsAsync(new ListarCarpetasQuery(Bandeja: bandeja))).Count);
    }

    [Fact]
    public async Task Annulling_one_open_carpeta_returns_the_remaining_one_to_the_trays()
    {
        var (a, b) = await DosAbiertas();
        var bandeja = await BandejaDe(a);
        await _db.NewService().AnularAsync(a, MotivosAnulacion.Duplicado, "javiera");

        Assert.Equal(1, await TotalEnBandejas());
        Assert.Equal(1, await EnBandejaListada(bandeja));
    }

    [Fact]
    public async Task Citizen_with_single_open_carpeta_is_unaffected()
    {
        var solo = await Registrar(RutB);
        await DosAbiertas();
        var bandeja = await BandejaDe(solo);

        Assert.Equal(1, await TotalEnBandejas());
        Assert.Equal(1, await EnBandejaListada(bandeja));
    }
}

/// <summary>The home panel (module counters, traffic light, late counts, 7-day series) obeys the same rule as the trays.</summary>
public sealed class DuplicadosFueraDelPanelTests : IDisposable
{
    private const string RutA = "12.345.678-5";
    private const string RutB = "11.111.111-1";

    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private PanelService NewPanel() =>
        new(new PanelQuery(_db.NewContext()), new DuplicadoRepository(_db.NewContext()), _db.Clock,
            new BandejasOptions { Umbrales = { [Bandeja.PorClasificar] = new UmbralDias { Amarillo = 1, Rojo = 3 } } });

    private Task<Guid> Registrar(string rut, Sede sede = Sede.AvArgentina, DateOnly? cita = null) =>
        _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(
            rut, "Juan", "Pérez", sede, cita ?? new DateOnly(2026, 5, 4), "ana", ClaseLicencia.Ninguna, ConfirmarDuplicado: true));

    private static int Total(PanelDto p) => p.Modulos.Sum(m => m.Conteo);

    [Fact]
    public async Task Panel_module_counts_skip_pending_duplicates_but_keep_the_duplicates_counter()
    {
        await Registrar(RutB);
        await Registrar(RutA);
        await Registrar(RutA, Sede.Placilla);

        var panel = await NewPanel().ObtenerAsync();

        Assert.Equal(1, Total(panel));
        Assert.Equal(1, panel.DuplicadosPendientes);
    }

    [Fact]
    public async Task Panel_semaphore_and_late_counts_ignore_pending_duplicates()
    {
        var ayer = new DateTime(2026, 5, 3, 15, 0, 0, DateTimeKind.Utc);
        var vieja = new DateOnly(2026, 4, 20);
        using (var ctx = _db.NewContext())
        {
            var dup = new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ");
            ctx.Carpetas.AddRange(
                Carpeta.CrearDesdeAgenda(dup, Sede.AvArgentina, vieja, null, null, "imp", ayer),
                Carpeta.CrearDesdeAgenda(dup, Sede.Placilla, vieja, null, null, "imp", ayer));
            ctx.SaveChanges();
        }

        var panel = await NewPanel().ObtenerAsync();

        var porClasificar = panel.Modulos.Single(m => m.Bandeja == Bandeja.PorClasificar);
        Assert.Equal(0, porClasificar.Conteo);
        Assert.Equal(0, porClasificar.Atrasadas);
        Assert.Equal(Semaforo.Verde, porClasificar.Semaforo);
        Assert.Equal(0, porClasificar.Serie.Sum());
    }

    [Fact]
    public async Task Panel_counts_both_again_after_mantener_ambas()
    {
        await Registrar(RutA);
        await Registrar(RutA, Sede.Placilla);
        _db.AdvanceClock(TimeSpan.FromMinutes(1));
        await _db.NewDuplicadosService().MarcarRevisadoAsync(
            (await _db.NewDuplicadosService().ListarAsync(new ListarDuplicadosQuery())).Items.Single().CiudadanoId, "pedro");

        var panel = await NewPanel().ObtenerAsync();

        Assert.Equal(2, Total(panel));
        Assert.Equal(0, panel.DuplicadosPendientes);
    }

    [Fact]
    public async Task Panel_counts_the_remaining_carpeta_after_annulling_one()
    {
        var a = await Registrar(RutA);
        await Registrar(RutA, Sede.Placilla);
        await _db.NewService().AnularAsync(a, MotivosAnulacion.Duplicado, "javiera");

        var panel = await NewPanel().ObtenerAsync();

        Assert.Equal(1, Total(panel));
    }

    [Fact]
    public async Task Meta_pending_goal_ignores_pending_duplicates_in_por_clasificar()
    {
        var ayer = new DateTime(2026, 5, 3, 15, 0, 0, DateTimeKind.Utc);
        using (var ctx = _db.NewContext())
        {
            var dup = new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ");
            var solo = new Ciudadano(Rut.Parse("11111111-1"), "ANA", "SOTO");
            ctx.Carpetas.AddRange(
                Carpeta.CrearDesdeAgenda(dup, Sede.AvArgentina, new DateOnly(2026, 5, 4), null, null, "imp", ayer),
                Carpeta.CrearDesdeAgenda(dup, Sede.Placilla, new DateOnly(2026, 5, 4), null, null, "imp", ayer),
                Carpeta.CrearDesdeAgenda(solo, Sede.AvArgentina, new DateOnly(2026, 5, 4), null, null, "imp", ayer));
            ctx.SaveChanges();
        }

        var panel = await NewPanel().ObtenerAsync();
        var meta = await NewPanel().ObtenerMetaAsync();

        Assert.Equal(1, panel.Modulos.Single(m => m.Bandeja == Bandeja.PorClasificar).Conteo);
        Assert.Equal(1, panel.Meta.Pendientes);
        Assert.Equal(1, meta.Pendientes);
        Assert.Equal(1, panel.DuplicadosPendientes);
    }
}
