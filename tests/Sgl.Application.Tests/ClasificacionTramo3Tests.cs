using Sgl.Application.Carpetas;
using Sgl.Application.Solicitudes;
using Sgl.Domain;

namespace Sgl.Application.Tests;

/// <summary>Tramo 3: lo que las funcionarias pueden elegir en «Por clasificar» y el camino sin atajo hasta Crear certificado.</summary>
public sealed class ClasificacionTramo3Tests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static readonly DateTime Antes = new(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);

    private Guid Seed(string rut = "12345678-5")
    {
        using var ctx = _db.NewContext();
        var c = Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse(rut), "JUAN", "PEREZ"), Sede.AvArgentina, new DateOnly(2026, 5, 4), new TimeOnly(9, 0),
            TramiteAgenda.Renovacion, "imp", Antes);
        ctx.Carpetas.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    private Task ClasificarAsync(Guid id, EstadoCarpeta? destino, string? fecha = null, string? comuna = null) =>
        _db.NewService().ClasificarAsync(new ClasificarCommand([id], destino, ClaseLicencia.B, fecha, comuna, "Funcionaria Prueba"));

    [Fact]
    public void Las_opciones_de_la_pantalla_son_las_cinco_del_negocio()
    {
        var opciones = Sgl.Web.Services.Labels.OpcionesClasificacion;

        Assert.Equal(
            new[]
            {
                EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.SeEncuentraEnOf43,
                EstadoCarpeta.CambioDomicilio, EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.CanjeLicExtranjera,
            }.OrderBy(e => e),
            opciones.OrderBy(e => e));
    }

    [Fact]
    public async Task Primera_licencia_y_canje_se_pueden_elegir()
    {
        var a = Seed("12345678-5");
        var b = Seed("11111111-1");

        await ClasificarAsync(a, EstadoCarpeta.PrimeraLicencia);
        await ClasificarAsync(b, EstadoCarpeta.CanjeLicExtranjera);

        Assert.Equal(EstadoCarpeta.PrimeraLicencia, (await _db.NewService().ObtenerAsync(a)).Estado);
        Assert.Equal(EstadoCarpeta.CanjeLicExtranjera, (await _db.NewService().ObtenerAsync(b)).Estado);
    }

    [Fact]
    public async Task Cambio_de_domicilio_sin_comuna_se_rechaza_y_con_comuna_pasa()
    {
        var id = Seed();

        await Assert.ThrowsAsync<DomainException>(() => ClasificarAsync(id, EstadoCarpeta.CambioDomicilio));
        Assert.Equal(EstadoCarpeta.PorClasificar, (await _db.NewService().ObtenerAsync(id)).Estado);

        await ClasificarAsync(id, EstadoCarpeta.CambioDomicilio, comuna: "QUILPUÉ");

        var det = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, det.Estado);
        Assert.Equal("QUILPUÉ", det.Comuna);
    }

    [Theory]
    [InlineData("2023-06-30", EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData("2023-07-01", EstadoCarpeta.SeEncuentraEnOf43)]
    public async Task Archivos_u_Of43_segun_la_fecha_de_ultima_carpeta(string fecha, EstadoCarpeta esperado)
    {
        var id = Seed();

        await ClasificarAsync(id, null, fecha);

        Assert.Equal(esperado, (await _db.NewService().ObtenerAsync(id)).Estado);
    }

    [Fact]
    public async Task No_existe_carpeta_se_puede_elegir_y_abre_la_solicitud_F8_automatica()
    {
        var id = Seed();

        await ClasificarAsync(id, EstadoCarpeta.NoExisteCarpeta);

        Assert.Equal(EstadoCarpeta.NoExisteCarpeta, (await _db.NewService().ObtenerAsync(id)).Estado);
        var solicitud = Assert.Single((await _db.NewF8Service().ListarAsync(new ListarSolicitudesF8Query(EstadoSolicitudF8.Solicitado))).Items);
        Assert.Equal(id, solicitud.CarpetaId);
        Assert.Equal(EstadoCarpeta.NoExisteCarpeta, solicitud.EstadoOrigen);
    }

    [Fact]
    public async Task Las_demas_clasificaciones_no_abren_F8()
    {
        var id = Seed();

        await ClasificarAsync(id, EstadoCarpeta.PrimeraLicencia);

        Assert.Empty((await _db.NewF8Service().ListarAsync(new ListarSolicitudesF8Query())).Items);
    }

    [Fact]
    public async Task Archivos_a_NoExiste_a_F8_no_generado_a_CrearCertificado_de_punta_a_punta()
    {
        var id = Seed();
        await ClasificarAsync(id, null, "2019-03-15");
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, (await _db.NewService().ObtenerAsync(id)).Estado);

        // El atajo directo ya no existe.
        await Assert.ThrowsAsync<InvalidTransitionException>(() =>
            _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.CrearCertificado, "Funcionaria Prueba")));
        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.CrearCertificado, "Funcionaria Prueba"));
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, (await _db.NewService().ObtenerAsync(id)).Estado);

        // No existe carpeta (abre F8 automática) y luego el F8 no se pudo generar.
        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.NoExisteCarpeta, "Funcionaria Prueba");
        var solicitud = Assert.Single((await _db.NewF8Service().ListarAsync(new ListarSolicitudesF8Query(EstadoSolicitudF8.Solicitado))).Items);
        var resultado = await _db.NewF8Service().MarcarNoSeGeneroAsync(solicitud.Id, "Funcionaria Prueba");

        Assert.False(resultado.Obsoleta);
        Assert.Equal(EstadoCarpeta.CrearCertificado, (await _db.NewService().ObtenerAsync(id)).Estado);
    }

    [Fact]
    public async Task F8_pedido_desde_Of43_que_no_se_genera_pasa_por_NoExiste_y_queda_en_el_historial()
    {
        var id = Seed();
        await ClasificarAsync(id, null, "2024-02-01");
        var pedido = await _db.NewF8Service().SolicitarF8Async(id, "Funcionaria Prueba");

        await _db.NewF8Service().MarcarNoSeGeneroAsync(pedido.SolicitudId, "Funcionaria Prueba");

        var det = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(EstadoCarpeta.CrearCertificado, det.Estado);
        Assert.Contains(det.Historial, h => h.Campo == "Estado" && h.Anterior == "SeEncuentraEnOf43" && h.Nuevo == "NoExisteCarpeta");
        Assert.Contains(det.Historial, h => h.Campo == "Estado" && h.Anterior == "NoExisteCarpeta" && h.Nuevo == "CrearCertificado");
    }
}
