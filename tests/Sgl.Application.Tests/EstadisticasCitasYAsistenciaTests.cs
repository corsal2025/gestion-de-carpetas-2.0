using Sgl.Application.Estadisticas;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>Read-only statistics of cancelled appointments and attendance (E7).</summary>
public sealed class EstadisticasCitasYAsistenciaTests : IDisposable
{
    private static readonly DateTime Ahora = new(2026, 5, 4, 10, 0, 0);
    private readonly TestDb _db = new();
    private int _rutSeq = 20_000_000;

    public void Dispose() => _db.Dispose();

    private Rut NuevoRut()
    {
        var body = (_rutSeq++).ToString();
        return Rut.Parse($"{body}-{Rut.ComputeCheckDigit(body)}");
    }

    private Task<EstadisticasDto> Obtener(FiltroEstadisticas? f = null) =>
        new EstadisticasService(new EstadisticasQuery(_db.NewContext()), _db.Clock).ObtenerAsync(f ?? new FiltroEstadisticas());

    private void Guardar(params object[] entidades)
    {
        using var ctx = _db.NewContext();
        ctx.AddRange(entidades);
        ctx.SaveChanges();
    }

    private static CitaCancelada Cancelada(Sede sede, DateOnly fecha, string tramite = "Renovación") =>
        new(fecha, new DateTime(2026, 3, 1, 9, 0, 0), new TimeOnly(9, 30), sede, tramite, "Motivo", Ahora);

    private Carpeta Agendada(Sede sede, DateOnly fecha)
    {
        var rut = NuevoRut();
        return Carpeta.CrearDesdeAgenda(new Ciudadano(rut, "Nombre", "Apellido"), sede, fecha, null, TramiteAgenda.Renovacion, "imp", Ahora);
    }

    private Carpeta Extra(Sede sede, DateOnly fecha)
    {
        var rut = NuevoRut();
        return Carpeta.CrearExtra(new Ciudadano(rut, "Nombre", "Apellido"), sede, fecha, "Renovación", TramiteAgenda.Renovacion, "ana", Ahora);
    }

    private Asistencia Marca(TipoAsistencia tipo, Sede sede, DateOnly fecha, Guid? carpetaId = null)
    {
        var id = carpetaId ?? Guid.NewGuid();
        return tipo == TipoAsistencia.Vino
            ? Asistencia.Vino(id, NuevoRut(), sede, fecha, "ana", Ahora)
            : Asistencia.Extra(id, NuevoRut(), sede, fecha, "ana", Ahora);
    }

    [Fact]
    public async Task Canceladas_se_agrupan_por_anio_mes_de_la_cita_y_sede_y_respetan_el_filtro()
    {
        Guardar(
            Cancelada(Sede.Placilla, new(2026, 4, 2)),
            Cancelada(Sede.Placilla, new(2026, 4, 20), "Primera vez"),
            Cancelada(Sede.AvArgentina, new(2026, 4, 5)),
            Cancelada(Sede.Placilla, new(2026, 5, 3)),
            Cancelada(Sede.Placilla, new(2025, 4, 3)));

        var todo = await Obtener();

        Assert.Equal(
            [(2025, 4, Sede.Placilla, 1), (2026, 4, Sede.AvArgentina, 1), (2026, 4, Sede.Placilla, 2), (2026, 5, Sede.Placilla, 1)],
            todo.Canceladas.Select(c => (c.Anio, c.Mes, c.Sede, c.Cantidad)).OrderBy(c => c.Item1).ThenBy(c => c.Item2).ThenBy(c => c.Item3));

        var placilla = await Obtener(new FiltroEstadisticas(Sede: Sede.Placilla, Desde: new(2026, 4, 1), Hasta: new(2026, 4, 30)));

        var unica = Assert.Single(placilla.Canceladas);
        Assert.Equal((2026, 4, Sede.Placilla, 2), (unica.Anio, unica.Mes, unica.Sede, unica.Cantidad));
    }

    [Fact]
    public async Task Asistencia_cuenta_agendadas_vinieron_y_extras_por_mes_y_sede_sin_anuladas()
    {
        var a1 = Agendada(Sede.Placilla, new(2026, 4, 2));
        var a2 = Agendada(Sede.Placilla, new(2026, 4, 3));
        var a3 = Agendada(Sede.Placilla, new(2026, 4, 4));
        var otra = Agendada(Sede.AvArgentina, new(2026, 4, 4));
        var extra = Extra(Sede.Placilla, new(2026, 4, 5));
        var vinoAnulada = Marca(TipoAsistencia.Vino, Sede.Placilla, new(2026, 4, 4), a3.Id);
        vinoAnulada.Anular("ana", Ahora);
        var extra2 = Extra(Sede.Placilla, new(2026, 4, 6));
        var extraAnulada = Marca(TipoAsistencia.Extra, Sede.Placilla, new(2026, 4, 6), extra2.Id);
        extraAnulada.Anular("ana", Ahora);
        Guardar(
            a1, a2, a3, otra, extra, extra2,
            Marca(TipoAsistencia.Vino, Sede.Placilla, new(2026, 4, 2), a1.Id),
            Marca(TipoAsistencia.Vino, Sede.Placilla, new(2026, 4, 3), a2.Id),
            vinoAnulada,
            Marca(TipoAsistencia.Extra, Sede.Placilla, new(2026, 4, 5), extra.Id),
            extraAnulada,
            Asistencia.PorAgregar(NuevoRut(), Sede.Placilla, new(2026, 4, 7), "ana", Ahora));

        var r = await Obtener();

        var placilla = Assert.Single(r.Asistencia, x => x.Sede == Sede.Placilla);
        Assert.Equal((2026, 4, 3, 2, 1), (placilla.Anio, placilla.Mes, placilla.Agendadas, placilla.Vinieron, placilla.Extras));
        var argentina = Assert.Single(r.Asistencia, x => x.Sede == Sede.AvArgentina);
        Assert.Equal((1, 0, 0), (argentina.Agendadas, argentina.Vinieron, argentina.Extras));

        var acotado = await Obtener(new FiltroEstadisticas(Sede: Sede.Placilla, Desde: new(2026, 4, 3), Hasta: new(2026, 4, 3)));
        var dia = Assert.Single(acotado.Asistencia);
        Assert.Equal((1, 1, 0), (dia.Agendadas, dia.Vinieron, dia.Extras));
    }

    // M4: sin meses futuros ni meses anteriores a la primera asistencia registrada (un 0 % ahí engaña).
    [Fact]
    public async Task Asistencia_no_incluye_meses_futuros_ni_anteriores_a_la_primera_asistencia_registrada()
    {
        var antes = Agendada(Sede.Placilla, new(2026, 2, 10));
        var abril = Agendada(Sede.Placilla, new(2026, 4, 2));
        var futuro = Agendada(Sede.Placilla, new(2026, 7, 9));
        Guardar(antes, abril, futuro, Marca(TipoAsistencia.Vino, Sede.Placilla, new(2026, 4, 2), abril.Id));

        var r = await Obtener();

        var unico = Assert.Single(r.Asistencia);
        Assert.Equal((2026, 4), (unico.Anio, unico.Mes));
    }

    [Fact]
    public async Task Asistencia_sin_ninguna_marca_registrada_queda_vacia_aunque_haya_agendadas()
    {
        Guardar(Agendada(Sede.Placilla, new(2026, 4, 2)), Agendada(Sede.AvArgentina, new(2026, 3, 2)));

        var r = await Obtener();

        Assert.Empty(r.Asistencia);
    }

    [Fact]
    public async Task Asistencia_incluye_el_mes_actual_aunque_sea_el_ultimo()
    {
        var mayo = Agendada(Sede.Placilla, new(2026, 5, 20));
        var abril = Agendada(Sede.Placilla, new(2026, 4, 2));
        Guardar(mayo, abril, Marca(TipoAsistencia.Vino, Sede.Placilla, new(2026, 4, 2), abril.Id));

        var r = await Obtener();

        Assert.Equal([(2026, 4), (2026, 5)], r.Asistencia.Select(x => (x.Anio, x.Mes)).OrderBy(x => x.Item1).ThenBy(x => x.Item2));
    }

    [Fact]
    public async Task Base_vacia_devuelve_las_dos_tablas_vacias_sin_excepcion()
    {
        var r = await Obtener();

        Assert.Empty(r.Canceladas);
        Assert.Empty(r.Asistencia);
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(0, 3, null)]
    [InlineData(4, 3, 0.75)]
    [InlineData(5, 0, 0.0)]
    public void Porcentaje_de_asistencia_es_vinieron_sobre_agendadas_y_nulo_sin_agendadas(int agendadas, int vinieron, double? esperado)
    {
        var dto = new AsistenciaMesSedeDto(2026, 4, Sede.Placilla, agendadas, vinieron, 0);

        Assert.Equal(esperado, dto.PorcentajeAsistencia);
    }

    [Fact]
    public void Los_dtos_nuevos_no_exponen_rut_ni_nombre_ni_texto_libre()
    {
        foreach (var tipo in new[] { typeof(CanceladasMesSedeDto), typeof(AsistenciaMesSedeDto) })
        {
            foreach (var p in tipo.GetProperties())
            {
                Assert.DoesNotContain("rut", p.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("nombre", p.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("apellido", p.Name, StringComparison.OrdinalIgnoreCase);
                Assert.NotEqual(typeof(string), p.PropertyType);
            }
        }
    }
}
