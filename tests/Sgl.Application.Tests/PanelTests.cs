using Sgl.Application.Carpetas;
using Sgl.Application.Panel;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class PanelCalculoTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 13);

    [Theory]
    [InlineData(0, 0, 0, 100, true)]
    [InlineData(3, 1, 4, 75, false)]
    [InlineData(0, 5, 5, 0, false)]
    [InlineData(7, 0, 7, 100, true)]
    public void MetaDelDia_computes_total_percentage_and_completion(int clasificadas, int pendientes, int total, int porcentaje, bool completa)
    {
        var meta = new MetaDelDiaDto(clasificadas, pendientes);

        Assert.Equal(total, meta.Total);
        Assert.Equal(porcentaje, meta.Porcentaje);
        Assert.Equal(completa, meta.Completa);
    }

    [Theory]
    [InlineData("Agenda", null, "PorClasificar (AvArgentina)", Bandeja.PorClasificar)]
    [InlineData("Registro", null, "Citada (AvArgentina)", Bandeja.ParaRevision)]
    [InlineData("Estado", "PorClasificar", "SubidaConaset", Bandeja.SubidaConaset)]
    [InlineData("Estado", "PorClasificar", "CambioDomicilio", Bandeja.CambioDomicilio)]
    [InlineData("Estado", "EsperaExamen", "Otorgado", Bandeja.Cerradas)]
    [InlineData("Estado", "ParaDenegar", "Denegado", Bandeja.Cerradas)]
    [InlineData("CajaArchivo", null, "CAJA 4", Bandeja.Archivadas)]
    public void EntradasModulo_maps_audit_entries_to_the_tray_they_enter(string campo, string? anterior, string nuevo, Bandeja esperada)
    {
        Assert.Equal(esperada, EntradasModulo.Bandeja(campo, anterior, nuevo));
    }

    [Theory]
    [InlineData("CajaArchivo", null, "Cola de Caja")]
    [InlineData("CajaArchivo", "CAJA 4", null)]
    [InlineData("Observación", null, "x")]
    [InlineData("Estado", null, "NoEsUnEstado")]
    [InlineData("Importacion", null, "AvArgentina - X")]
    public void EntradasModulo_ignores_entries_that_do_not_enter_a_tray(string campo, string? anterior, string? nuevo)
    {
        Assert.Null(EntradasModulo.Bandeja(campo, anterior, nuevo));
    }

    [Fact]
    public void SerieSieteDias_counts_per_day_oldest_first_and_ignores_older_days()
    {
        var entradas = new[]
        {
            (Hoy, Bandeja.PorClasificar),
            (Hoy, Bandeja.PorClasificar),
            (Hoy.AddDays(-6), Bandeja.PorClasificar),
            (Hoy.AddDays(-7), Bandeja.PorClasificar),
            (Hoy.AddDays(-1), Bandeja.SubidaConaset),
        };

        var serie = SerieSieteDias.Construir(entradas, Hoy);

        Assert.Equal([1, 0, 0, 0, 0, 0, 2], serie[Bandeja.PorClasificar]);
        Assert.Equal([0, 0, 0, 0, 0, 1, 0], serie[Bandeja.SubidaConaset]);
        Assert.Equal(new int[7], serie[Bandeja.Archivadas]);
    }

    [Fact]
    public void SemaforoModulo_is_red_when_any_carpeta_reaches_red_threshold()
    {
        var opciones = new BandejasOptions { Umbrales = { [Bandeja.PorPreparar] = new UmbralDias { Amarillo = 15, Rojo = 30 } } };
        var fechas = new[] { (Hoy.AddDays(-2), 4), (Hoy.AddDays(-20), 2), (Hoy.AddDays(-40), 1) };

        var s = SemaforoModulo.Evaluar(Bandeja.PorPreparar, fechas, Hoy, opciones);

        Assert.Equal(Semaforo.Rojo, s.Color);
        Assert.Equal(1, s.Rojas);
        Assert.Equal(2, s.Amarillas);
    }

    [Fact]
    public void SemaforoModulo_is_yellow_without_red_and_green_when_up_to_date()
    {
        var opciones = new BandejasOptions { Umbrales = { [Bandeja.PorClasificar] = new UmbralDias { Amarillo = 1, Rojo = 3 } } };

        Assert.Equal(Semaforo.Amarillo, SemaforoModulo.Evaluar(Bandeja.PorClasificar, [(Hoy.AddDays(-1), 1), (Hoy, 3)], Hoy, opciones).Color);
        Assert.Equal(Semaforo.Verde, SemaforoModulo.Evaluar(Bandeja.PorClasificar, [(Hoy, 3), (Hoy.AddDays(2), 1)], Hoy, opciones).Color);
        Assert.Equal(Semaforo.Verde, SemaforoModulo.Evaluar(Bandeja.PorClasificar, [], Hoy, opciones).Color);
    }

    [Theory]
    [InlineData(Bandeja.Cerradas)]
    [InlineData(Bandeja.Archivadas)]
    public void SemaforoModulo_does_not_apply_to_closing_trays(Bandeja bandeja)
    {
        var s = SemaforoModulo.Evaluar(bandeja, [(Hoy.AddDays(-400), 10)], Hoy, new BandejasOptions());

        Assert.Null(s.Color);
        Assert.Equal(0, s.Rojas);
    }

    [Fact]
    public void MensajeLico_prioritizes_duplicate_alerts_then_review_queue_then_pending()
    {
        Assert.Equal("Tienes 2 filas con alerta de duplicado en Por clasificar.", MensajeLico.Elegir(2, 5, new MetaDelDiaDto(0, 9)));
        Assert.Equal("Tienes 5 duplicados por revisar.", MensajeLico.Elegir(0, 5, new MetaDelDiaDto(0, 9)));
        Assert.Equal("Tienes 1 duplicado por revisar.", MensajeLico.Elegir(0, 1, new MetaDelDiaDto(0, 9)));
        Assert.Equal("Te quedan 9 por clasificar hoy. ¡Vamos!", MensajeLico.Elegir(0, 0, new MetaDelDiaDto(1, 9)));
        Assert.Equal("¡Por clasificar al día!", MensajeLico.Elegir(0, 0, new MetaDelDiaDto(4, 0)));
    }
}

public sealed class PanelServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private PanelService NewPanel() =>
        new(new PanelQuery(_db.NewContext()), new DuplicadoRepository(_db.NewContext()), _db.Clock,
            new BandejasOptions { Umbrales = { [Bandeja.PorClasificar] = new UmbralDias { Amarillo = 1, Rojo = 3 } } });

    [Fact]
    public async Task ObtenerAsync_builds_counts_meta_series_and_excludes_cancelled()
    {
        // Clock: 2026-05-04 10:00 UTC (06:00 Chile).
        var ayer = new DateTime(2026, 5, 3, 15, 0, 0, DateTimeKind.Utc);
        var hoy = _db.Clock.GetUtcNow().UtcDateTime;
        Guid aClasificar, aAnular;
        using (var ctx = _db.NewContext())
        {
            var juan = new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ");
            var ana = new Ciudadano(Rut.Parse("11111111-1"), "ANA", "SOTO");
            var pedro = new Ciudadano(Rut.Parse("22222222-2"), "PEDRO", "ROJAS");
            var a = Carpeta.CrearDesdeAgenda(juan, Sede.AvArgentina, new DateOnly(2026, 5, 4), null, null, "imp", ayer);
            var b = Carpeta.CrearDesdeAgenda(ana, Sede.AvArgentina, new DateOnly(2026, 5, 4), null, null, "imp", ayer);
            var c = Carpeta.CrearDesdeAgenda(pedro, Sede.AvArgentina, new DateOnly(2026, 4, 28), null, null, "imp", ayer);
            ctx.Carpetas.AddRange(a, b, c);
            ctx.SaveChanges();
            aClasificar = a.Id;
            aAnular = b.Id;
        }

        await _db.NewService().ClasificarAsync(new ClasificarCommand([aClasificar], EstadoCarpeta.SubidaConaset, ClaseLicencia.B, null, null, "javiera"));
        await _db.NewService().AnularAsync(aAnular, MotivosAnulacion.Duplicado, "javiera");

        var panel = await NewPanel().ObtenerAsync();

        var porClasificar = panel.Modulos.Single(m => m.Bandeja == Bandeja.PorClasificar);
        Assert.Equal(1, porClasificar.Conteo);
        Assert.Equal(Semaforo.Rojo, porClasificar.Semaforo);
        Assert.Equal(1, porClasificar.Atrasadas);
        Assert.Equal(1, panel.Modulos.Single(m => m.Bandeja == Bandeja.SubidaConaset).Conteo);
        Assert.Equal(1, panel.Meta.Clasificadas);
        Assert.Equal(1, panel.Meta.Pendientes);
        Assert.Equal(1, panel.Anuladas);
        Assert.Equal(1, panel.SerieAnuladas[^1]);
        Assert.Equal(2, panel.Modulos.Single(m => m.Bandeja == Bandeja.PorClasificar).Serie[^2]);
        Assert.Equal(1, panel.Modulos.Single(m => m.Bandeja == Bandeja.SubidaConaset).Serie[^1]);
        Assert.Equal(7, porClasificar.Serie.Count);
        Assert.False(string.IsNullOrWhiteSpace(panel.MensajeLico));
        _ = hoy;
    }

    [Fact]
    public async Task ActividadHoy_counts_carpetas_that_left_the_location_tray_today_and_the_ones_still_waiting()
    {
        var ayer = new DateTime(2026, 5, 3, 15, 0, 0, DateTimeKind.Utc);
        Guid movida, queda;
        using (var ctx = _db.NewContext())
        {
            var a = Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ"), Sede.AvArgentina, new DateOnly(2026, 5, 4), null, null, "imp", ayer);
            var b = Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("11111111-1"), "ANA", "SOTO"), Sede.AvArgentina, new DateOnly(2026, 5, 4), null, null, "imp", ayer);
            ctx.Carpetas.AddRange(a, b);
            ctx.SaveChanges();
            movida = a.Id;
            queda = b.Id;
        }

        await _db.NewService().ClasificarAsync(new ClasificarCommand([movida, queda], null, ClaseLicencia.B, "2019-01-01", null, "javiera"));
        await _db.NewService().ForzarEstadoAsync(movida, EstadoCarpeta.SubidaConaset, "javiera");

        var panel = await NewPanel().ObtenerAsync();

        var archivos = panel.ActividadHoy[Bandeja.SeEncuentraEnArchivos];
        Assert.Equal(1, archivos.Clasificadas);
        Assert.Equal(1, archivos.Pendientes);
        Assert.Equal(new MetaDelDiaDto(0, 0), panel.ActividadHoy[Bandeja.SeEncuentraEnOf43]);
    }

    [Fact]
    public async Task ObtenerMetaAsync_matches_panel_meta()
    {
        var meta = await NewPanel().ObtenerMetaAsync();

        Assert.Equal(0, meta.Total);
        Assert.True(meta.Completa);
    }
}
