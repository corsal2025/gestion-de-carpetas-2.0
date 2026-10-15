using Sgl.Application.Carpetas;
using Sgl.Application.Estadisticas;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class EstadisticasQueryTests : IDisposable
{
    private static readonly DateOnly Hoy = new(2026, 5, 4);
    private static readonly DateTime Ahora = new(2026, 5, 4, 10, 0, 0);
    private readonly TestDb _db = new();
    private int _rutSeq = 10_000_000;

    public void Dispose() => _db.Dispose();

    private Ciudadano NuevoCiudadano()
    {
        var body = (_rutSeq++).ToString();
        return new Ciudadano(Rut.Parse($"{body}-{Rut.ComputeCheckDigit(body)}"), "Nombre", "Apellido");
    }

    private Task<EstadisticasDto> Obtener(FiltroEstadisticas? f = null) =>
        new EstadisticasService(new EstadisticasQuery(_db.NewContext()), _db.Clock).ObtenerAsync(f ?? new FiltroEstadisticas());

    private void Guardar(params Carpeta[] carpetas)
    {
        using var ctx = _db.NewContext();
        ctx.Carpetas.AddRange(carpetas);
        ctx.SaveChanges();
    }

    private Carpeta Importada(Sede sede, DateOnly citacion, DateOnly? subida, EstadoCarpeta estado,
        Decision decision = Decision.Pendiente, Ciudadano? ciudadano = null, string? caja = null, string? obs = null, string? comuna = null) =>
        Carpeta.CrearImportada(Guid.NewGuid(), ciudadano ?? NuevoCiudadano(), sede, citacion, subida, null, estado, decision,
            obs, null, caja, comuna, Ahora);

    [Fact]
    public async Task SubidasPorMes_groups_by_month_sede_and_channel()
    {
        Guardar(
            Importada(Sede.Placilla, new(2026, 4, 1), new(2026, 4, 10), EstadoCarpeta.SubidaConaset),
            Importada(Sede.Placilla, new(2026, 4, 1), new(2026, 4, 12), EstadoCarpeta.SubidaConF8),
            Importada(Sede.Placilla, new(2026, 4, 1), new(2026, 4, 20), EstadoCarpeta.CambioDomSubidoCorreo),
            Importada(Sede.AvArgentina, new(2026, 4, 1), new(2026, 5, 2), EstadoCarpeta.SubidaConaset));

        var r = await Obtener();

        var abril = Assert.Single(r.Actuales.SubidasPorMes, s => s.Mes == 4);
        Assert.Equal((Sede.Placilla, 1, 1, 1, 3), (abril.Sede, abril.Conaset, abril.F8, abril.Correo, abril.Total));
        Assert.Equal(1, r.Kpis.SubidasEsteMes);
        Assert.Equal(15, r.Actuales.CitacionSubida.Mediana); // 9, 11, 19, 31
        Assert.Equal(31, r.Actuales.MasLentas[0].Dias);
    }

    [Fact]
    public async Task TiempoEntrega_averages_closed_carpetas_overall_and_by_origin_module()
    {
        Guardar(
            Importada(Sede.Placilla, new(2026, 4, 1), new(2026, 4, 11), EstadoCarpeta.Citada, Decision.Otorgado),
            Importada(Sede.Placilla, new(2026, 4, 1), new(2026, 4, 21), EstadoCarpeta.Citada, Decision.Denegado),
            Importada(Sede.Placilla, new(2026, 4, 1), new(2026, 4, 5), EstadoCarpeta.SubidaConaset),
            Importada(Sede.Placilla, new(2026, 4, 1), null, EstadoCarpeta.Citada));

        var r = await Obtener();

        Assert.Equal(3, r.Actuales.TiempoEntrega.General.Muestras);
        Assert.Equal(11.3, r.Actuales.TiempoEntrega.General.Promedio);
        var primera = r.Actuales.TiempoEntrega.PorModulo.Single(m => m.Modulo == Bandeja.PorPreparar).Tiempos;
        Assert.Equal(2, primera.Muestras);
        Assert.Equal(15, primera.Promedio);
    }

    [Fact]
    public async Task Filters_by_sede_and_citacion_range()
    {
        Guardar(
            Importada(Sede.Placilla, new(2026, 1, 10), null, EstadoCarpeta.Citada),
            Importada(Sede.Placilla, new(2026, 3, 10), null, EstadoCarpeta.Citada),
            Importada(Sede.MercadoPuerto, new(2026, 3, 10), null, EstadoCarpeta.Citada));

        var r = await Obtener(new FiltroEstadisticas(Sede.Placilla, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));

        var fila = Assert.Single(r.Actuales.EstadoPorSede);
        Assert.Equal((Sede.Placilla, "Citada", 1), (fila.Sede, fila.Clave, fila.Cantidad));
        Assert.Equal(1, Assert.Single(r.Actuales.CitadasSinSubir).Cantidad);
    }

    [Fact]
    public async Task Atrasadas_uses_red_threshold_of_open_trays_only()
    {
        Guardar(
            Importada(Sede.Placilla, Hoy.AddDays(-45), null, EstadoCarpeta.Citada),
            Importada(Sede.Placilla, Hoy.AddDays(-5), null, EstadoCarpeta.Citada),
            Importada(Sede.Placilla, Hoy.AddDays(-90), new(2026, 3, 1), EstadoCarpeta.Otorgado, Decision.Otorgado));

        var r = await Obtener();

        Assert.Equal(1, r.Kpis.CarpetasAtrasadas);
    }

    [Fact]
    public async Task Decisiones_rate_and_personas_with_several_open()
    {
        var persona = NuevoCiudadano();
        Guardar(
            Importada(Sede.Placilla, new(2026, 3, 1), new(2026, 3, 5), EstadoCarpeta.Otorgado, Decision.Otorgado),
            Importada(Sede.Placilla, new(2026, 3, 1), new(2026, 3, 6), EstadoCarpeta.Denegado, Decision.Denegado),
            Importada(Sede.Placilla, new(2026, 3, 1), null, EstadoCarpeta.Citada, ciudadano: persona),
            Importada(Sede.AvArgentina, new(2026, 3, 2), null, EstadoCarpeta.Citada, ciudadano: persona));

        var r = await Obtener();

        var placilla = Assert.Single(r.Actuales.DecisionPorSede);
        Assert.Equal(0.5, placilla.TasaOtorgamiento);
        Assert.Equal(new DecisionMesDto(2026, 3, 1, 1), Assert.Single(r.Actuales.DecisionPorMes));
        Assert.Equal(1, r.Actuales.PersonasConVariasAbiertas);
    }

    [Fact]
    public async Task Busqueda_metrics_come_from_audit_history()
    {
        var c = Importada(Sede.Placilla, new(2026, 4, 1), null, EstadoCarpeta.SeEncuentraEnArchivos);
        c.MarcarPendienteBusqueda(true, "ana", Ahora.AddHours(-4));
        c.MarcarPendienteBusqueda(false, "ana", Ahora.AddHours(-1));
        Guardar(c);

        var r = await Obtener();

        Assert.True(r.Busquedas.HayDatos);
        Assert.Equal(1, r.Busquedas.Encontradas);
        Assert.Equal(3, r.Busquedas.HorasParaEncontrar.Mediana);
        Assert.Equal(0, r.Kpis.BusquedasAbiertas);
    }

    [Fact]
    public async Task Uso_archivo_clases_and_observaciones()
    {
        var a = Importada(Sede.Placilla, new(2026, 4, 1), null, EstadoCarpeta.Citada, obs: "Falta certificado");
        var b = Importada(Sede.Placilla, new(2026, 4, 1), null, EstadoCarpeta.Citada, obs: "falta firma");
        a.AsignarCaja("CAJA 1", "ana", Ahora);
        b.AsignarCaja(Carpeta.ColaDeCaja, "luis", Ahora);
        a.AsignarClases(ClaseLicencia.B, "ana", Ahora);
        Guardar(a, b);

        var r = await Obtener();

        Assert.Equal(new ConteoDto("ana", 2), r.Uso.CambiosPorFuncionario[0]);
        Assert.Equal(2, r.Uso.Importadas);
        Assert.True(r.Uso.ImportacionesInferidas);
        Assert.Equal(2, Assert.Single(r.Uso.Importaciones).FilasLeidas);
        Assert.Equal(new ConteoDto("FALTA", 2), r.Uso.TerminosObservacion[0]);
        Assert.Equal((1, 1), (r.Uso.ClasePorSede[0].ConClase, r.Uso.ClasePorSede[0].SinClase));
        Assert.Equal((1, 1.0, 1), (r.Archivo.CajasConCarpetas, r.Archivo.OcupacionPromedio!.Value, r.Archivo.EnColaDeCaja));
        Assert.Equal(1, Assert.Single(r.Archivo.PorMes).CajasCerradas);
    }

    [Fact]
    public async Task Persisted_import_log_takes_precedence_over_inferred()
    {
        using (var ctx = _db.NewContext())
        {
            ctx.ImportacionLogs.Add(new ImportacionLog(Ahora, "ana", 10, 7, 3, 0));
            ctx.SaveChanges();
        }

        var r = await Obtener();

        Assert.False(r.Uso.ImportacionesInferidas);
        Assert.Equal(7, Assert.Single(r.Uso.Importaciones).Creadas);
    }

    [Fact]
    public async Task Empty_database_returns_zeros_without_failing()
    {
        var r = await Obtener();
        Assert.False(r.Busquedas.HayDatos);
        Assert.Null(r.Kpis.MedianaDiasCitacionSubida);
        Assert.Empty(r.Actuales.SubidasPorMes);
    }
}
