using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

public sealed class BandejasTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static readonly DateOnly Hoy = new(2026, 5, 4);

    private static CarpetaResumenDto Resumen(
        DateOnly? citacion = null, DateOnly? subida = null, DateTime? registro = null,
        Decision decision = Decision.Pendiente, bool pendiente = false) =>
        new(Guid.NewGuid(), "1-9", "X", Sede.Placilla, citacion ?? new DateOnly(2026, 4, 1), subida, null, null, null, null, null,
            EstadoCarpeta.Citada, decision, pendiente)
        { FechaRegistro = registro ?? new DateTime(2026, 4, 20, 8, 0, 0) };

    // Audit timestamps are UTC; date-only fixtures are placed at 12:00 UTC so they land on the same Chile date.
    private static MovimientoDto Mov(DateTime fecha, string campo, string? nuevo, string usuario = "ana") =>
        new(Guid.Empty, fecha.TimeOfDay == TimeSpan.Zero ? fecha.AddHours(12) : fecha, usuario, campo, nuevo);

    [Fact]
    public void DiasTramite_counts_from_citacion_regardless_of_estado_changes()
    {
        var ind = IndicadoresCalculator.Calcular(Resumen(citacion: new DateOnly(2026, 4, 1)), [
            Mov(new DateTime(2026, 4, 10), "Estado", "Revision"),
            Mov(new DateTime(2026, 4, 24), "Estado", "Citada"),
            Mov(new DateTime(2026, 5, 1), "Observación", "x", "luis"),
        ], Hoy);

        Assert.Equal(33, ind.DiasTramite);
        Assert.Equal(new DateTime(2026, 5, 1, 12, 0, 0), ind.UltimoMovimiento);
        Assert.Equal("luis", ind.UltimoUsuario);
    }

    [Fact]
    public void DiasTramite_is_null_when_citacion_is_in_the_future_and_zero_today()
    {
        var futura = IndicadoresCalculator.Calcular(Resumen(citacion: Hoy.AddDays(10)), [], Hoy);
        var hoy = IndicadoresCalculator.Calcular(Resumen(citacion: Hoy), [], Hoy);

        Assert.Null(futura.DiasTramite);
        Assert.Equal(0, hoy.DiasTramite);
    }

    [Fact]
    public void DiasSinCarpeta_counts_from_last_change_to_no_existe_else_citacion()
    {
        var conHist = IndicadoresCalculator.Calcular(Resumen(), [
            Mov(new DateTime(2026, 4, 10), "Estado", "NoExisteCarpeta"),
            Mov(new DateTime(2026, 4, 12), "Estado", "Citada"),
            Mov(new DateTime(2026, 4, 24), "Estado", "NoExisteCarpeta"),
        ], Hoy);
        Assert.Equal(10, conHist.DiasSinCarpeta);

        var sinHist = IndicadoresCalculator.Calcular(Resumen(citacion: new DateOnly(2026, 4, 1)), [], Hoy);
        Assert.Equal(33, sinHist.DiasSinCarpeta);
        Assert.Null(sinHist.UltimoMovimiento);
    }

    [Fact]
    public void DiasBuscando_and_MarcadaPor_come_from_last_pendiente_on()
    {
        var ind = IndicadoresCalculator.Calcular(Resumen(pendiente: true), [
            Mov(new DateTime(2026, 4, 1), "PendienteBusqueda", "Si", "old"),
            Mov(new DateTime(2026, 4, 2), "PendienteBusqueda", "No", "old"),
            Mov(new DateTime(2026, 4, 30), "PendienteBusqueda", "Si", "marta"),
        ], Hoy);

        Assert.Equal(4, ind.DiasBuscando);
        Assert.Equal("marta", ind.MarcadaPor);
    }

    [Fact]
    public void DiasBuscando_is_null_when_not_pendiente()
    {
        var ind = IndicadoresCalculator.Calcular(Resumen(), [Mov(new DateTime(2026, 4, 30), "PendienteBusqueda", "Si")], Hoy);
        Assert.Null(ind.DiasBuscando);
    }

    [Fact]
    public void FechaCierre_uses_last_final_decision_else_subida_and_DiasTotales_from_citacion()
    {
        var conHist = IndicadoresCalculator.Calcular(Resumen(decision: Decision.Otorgado), [
            Mov(new DateTime(2026, 4, 21, 15, 0, 0), "Decision", "Otorgado"),
        ], Hoy);
        Assert.Equal(new DateOnly(2026, 4, 21), conHist.FechaCierre);
        Assert.Equal(20, conHist.DiasTotales);

        var sinHist = IndicadoresCalculator.Calcular(Resumen(decision: Decision.Denegado, subida: new DateOnly(2026, 4, 11)), [], Hoy);
        Assert.Equal(new DateOnly(2026, 4, 11), sinHist.FechaCierre);
    }

    [Fact]
    public void FechaArchivo_ignores_cola_de_caja_and_DiasCitacionSubida_is_computed()
    {
        var ind = IndicadoresCalculator.Calcular(Resumen(subida: new DateOnly(2026, 4, 6)), [
            Mov(new DateTime(2026, 4, 25), "CajaArchivo", Carpeta.ColaDeCaja),
            Mov(new DateTime(2026, 4, 28), "CajaArchivo", "CAJA 3"),
            Mov(new DateTime(2026, 5, 2), "CajaArchivo", Carpeta.ColaDeCaja),
        ], Hoy);
        Assert.Equal(new DateOnly(2026, 4, 28), ind.FechaArchivo);
        Assert.Equal(5, ind.DiasCitacionSubida);
    }

    [Theory]
    [InlineData(0, Semaforo.Verde)]
    [InlineData(14, Semaforo.Verde)]
    [InlineData(15, Semaforo.Amarillo)]
    [InlineData(29, Semaforo.Amarillo)]
    [InlineData(30, Semaforo.Rojo)]
    public void Semaforo_uses_default_thresholds(int dias, Semaforo esperado)
    {
        Assert.Equal(esperado, new BandejasOptions().Evaluar(Bandeja.PorPreparar, dias));
    }

    [Fact]
    public void Semaforo_uses_configured_threshold_per_bandeja()
    {
        var opts = new BandejasOptions();
        opts.Umbrales[Bandeja.SinCarpetaF8] = new UmbralDias { Amarillo = 3, Rojo = 7 };
        Assert.Equal(Semaforo.Rojo, opts.Evaluar(Bandeja.SinCarpetaF8, 7));
        Assert.Equal(Semaforo.Verde, opts.Evaluar(Bandeja.SubidaConF8, 7));
    }

    private async Task<Guid> Registrar(string rut, Sede sede = Sede.AvArgentina, DateOnly? citacion = null) =>
        await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(rut, "Juan", "Pérez", sede, citacion ?? Hoy, "ana", ConfirmarDuplicado: true));

    [Fact]
    public async Task Listar_filters_by_bandeja_in_sql_and_counts_are_grouped()
    {
        var a = await Registrar("12.345.678-5");
        var b = await Registrar("11.111.111-1");
        var c = await Registrar("22.222.222-2", Sede.Placilla);
        await _db.NewService().ForzarEstadoAsync(b, EstadoCarpeta.SubidaConaset, "ana");
        await _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(c, "CAJA 9", "ana"));

        var subidas = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.SubidaConaset, Take: 10));
        Assert.Equal(b, Assert.Single(subidas.Items).Id);

        var conteos = await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery());
        Assert.Equal(1, conteos[Bandeja.ParaRevision]);
        Assert.Equal(1, conteos[Bandeja.SubidaConaset]);
        Assert.Equal(1, conteos[Bandeja.Archivadas]);
        Assert.Equal(0, conteos[Bandeja.Cerradas]);

        var soloAv = await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery(Sede: Sede.AvArgentina));
        Assert.Equal(0, soloAv[Bandeja.Archivadas]);
        _ = a;
    }

    [Fact]
    public async Task Listar_range_includes_indicadores_and_otras_abiertas()
    {
        var a = await Registrar("12.345.678-5");
        await Registrar("12.345.678-5");
        _db.AdvanceClock(TimeSpan.FromDays(3));
        await _db.NewService().ForzarEstadoAsync(a, EstadoCarpeta.Revision, "luis");
        _db.AdvanceClock(TimeSpan.FromDays(2));

        var res = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Take: 10));
        var fila = res.Items.Single(i => i.Id == a);

        Assert.Equal(1, fila.OtrasAbiertas);
        Assert.NotNull(fila.Indicadores);
        Assert.Equal(5, fila.Indicadores!.DiasTramite);
        Assert.Equal("luis", fila.Indicadores.UltimoUsuario);
    }

    [Fact]
    public async Task Listar_filters_by_rango_de_fechas_de_citacion()
    {
        await Registrar("12.345.678-5", citacion: new DateOnly(2026, 1, 10));
        await Registrar("11.111.111-1", citacion: new DateOnly(2026, 3, 10));

        var res = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Desde: new DateOnly(2026, 2, 1), Hasta: new DateOnly(2026, 12, 31), Take: 10));
        Assert.Equal(1, res.Total);
    }
}
