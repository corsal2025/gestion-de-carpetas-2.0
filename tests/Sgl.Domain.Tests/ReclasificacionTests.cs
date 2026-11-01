using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>Etapa «Reclasificación»: toda carpeta que entra a Cambios de domicilio queda pendiente hasta que alguien la confirma.</summary>
public class ReclasificacionTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private static Carpeta Saliente()
    {
        var c = Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse("12345678-5"), "ROSA", "MELENDEZ ARAYA"), Sede.AvArgentina,
            new DateOnly(2026, 10, 30), new TimeOnly(9, 30), TramiteAgenda.Renovacion, "importador", Now);
        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, "Viña del Mar", "javiera", Now);
        return c;
    }

    private static Carpeta Entrante() =>
        Carpeta.RegistrarDesdeCorreo(
            new Ciudadano(Rut.Parse("12345678-5"), "ROSA", "MELENDEZ ARAYA"), Sede.AvArgentina,
            new DateOnly(2026, 10, 29), "Viña del Mar", false, "Sistema · correo", Now);

    // Criterio 1
    [Fact]
    public void Clasificar_a_cambio_de_domicilio_deja_la_carpeta_pendiente()
    {
        Assert.True(Saliente().PendienteReclasificacion);
    }

    [Fact]
    public void Una_carpeta_nueva_o_clasificada_a_otro_destino_no_queda_pendiente()
    {
        var c = Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse("12345678-5"), "ROSA", "MELENDEZ ARAYA"), Sede.AvArgentina,
            new DateOnly(2026, 10, 30), new TimeOnly(9, 30), TramiteAgenda.Renovacion, "importador", Now);
        Assert.False(c.PendienteReclasificacion);

        c.Clasificar(EstadoCarpeta.PrimeraLicencia, ClaseLicencia.B, null, null, "javiera", Now);
        Assert.False(c.PendienteReclasificacion);
    }

    // Criterio 1
    [Fact]
    public void Registrar_desde_correo_deja_la_carpeta_pendiente()
    {
        Assert.True(Entrante().PendienteReclasificacion);
    }

    // Criterio 2
    [Fact]
    public void Una_saliente_pendiente_no_se_revisa_ni_se_pide_por_correo()
    {
        var c = Saliente();

        Assert.Throws<DomainException>(() => c.MarcarRevisada("ana", Now));
        Assert.Throws<DomainException>(() => c.MarcarPedidaPorCorreo("Viña del Mar", "ana", Now));
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.Null(c.RevisadaEn);
    }

    // Criterio 2
    [Fact]
    public void Una_entrante_pendiente_rechaza_todas_las_acciones_de_nos_piden_carpetas()
    {
        var c = Entrante();

        Assert.Throws<DomainException>(() => c.ClasificarEntrante("2024-01-10", "ana", Now));
        Assert.Throws<DomainException>(() => c.AvanzarLupaEntrante("ana", Now));
        Assert.Throws<DomainException>(() => c.MarcarSubidaEntrante("ana", Now));
        Assert.Throws<DomainException>(() => c.MarcarNoEncontradaEntrante("ana", Now));
        Assert.Throws<DomainException>(() => c.CerrarEntranteSinRegistro(null, "ana", Now));
        Assert.Throws<DomainException>(() => c.CerrarEntranteCarpetaDigital("ana", Now));
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.Null(c.FechaUltimaCarpeta);
    }

    // Criterio 3
    [Fact]
    public void Confirmar_deja_el_campo_en_false_conserva_todo_y_escribe_el_historial()
    {
        var c = Saliente();
        var estado = c.Estado;
        var etapa = c.FechaEstadoActual;
        var cita = c.FechaCitacion;
        var sentido = c.SentidoCambioDomicilio;

        c.ConfirmarReclasificacion("javiera", Now.AddHours(2));

        Assert.False(c.PendienteReclasificacion);
        Assert.Equal(estado, c.Estado);
        Assert.Equal(etapa, c.FechaEstadoActual);
        Assert.Equal(cita, c.FechaCitacion);
        Assert.Equal(sentido, c.SentidoCambioDomicilio);
        var linea = Assert.Single(c.Historial, h => h.Campo == Carpeta.CampoReclasificacion && h.Nuevo == "confirmada");
        Assert.Equal("javiera", linea.Usuario);
    }

    [Fact]
    public void Confirmada_la_entrante_y_la_saliente_siguen_su_flujo()
    {
        var saliente = Saliente();
        saliente.ConfirmarReclasificacion("javiera", Now);
        saliente.MarcarRevisada("ana", Now);
        saliente.MarcarPedidaPorCorreo("Viña del Mar", "ana", Now);
        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, saliente.Estado);

        var entrante = Entrante();
        entrante.ConfirmarReclasificacion("javiera", Now);
        entrante.ClasificarEntrante("2024-01-10", "ana", Now);
        entrante.MarcarSubidaEntrante("ana", Now);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, entrante.Estado);
    }

    [Fact]
    public void Confirmar_dos_veces_o_una_carpeta_no_pendiente_se_rechaza()
    {
        var c = Saliente();
        c.ConfirmarReclasificacion("javiera", Now);

        Assert.Throws<DomainException>(() => c.ConfirmarReclasificacion("javiera", Now));
        Assert.Throws<DomainException>(() => c.ConfirmarReclasificacion(" ", Now));
    }

    [Fact]
    public void Corregir_una_pendiente_la_mueve_y_limpia_el_campo()
    {
        var c = Saliente();

        c.CorregirClasificacionCambioDom(EstadoCarpeta.PrimeraLicencia, "Era primera licencia", null, "javiera", Now);

        Assert.Equal(EstadoCarpeta.PrimeraLicencia, c.Estado);
        Assert.False(c.PendienteReclasificacion);
        Assert.Null(c.SentidoCambioDomicilio);
    }

    [Fact]
    public void Salir_de_cambio_de_domicilio_por_ForzarEstado_limpia_el_campo()
    {
        var c = Saliente();

        c.ForzarEstado(EstadoCarpeta.PrimeraLicencia, "javiera", Now);

        Assert.False(c.PendienteReclasificacion);
    }

    // Criterio 9
    [Fact]
    public void Deshacer_la_correccion_devuelve_la_carpeta_a_pendiente()
    {
        var c = Saliente();
        var punto = c.PuntoRestauracion();
        Assert.True(punto.PendienteReclasificacion);
        c.CorregirClasificacionCambioDom(EstadoCarpeta.CanjeLicExtranjera, "Es canje", null, "javiera", Now);
        Assert.False(c.PendienteReclasificacion);

        c.RestaurarEstado(punto, EstadoCarpeta.CanjeLicExtranjera, "javiera", Now.AddMinutes(1));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.True(c.PendienteReclasificacion);
        Assert.Equal(SentidoCambioDomicilio.Saliente, c.SentidoCambioDomicilio);
    }

    [Fact]
    public void Un_punto_de_una_carpeta_no_pendiente_no_la_vuelve_pendiente()
    {
        var c = Saliente();
        c.ConfirmarReclasificacion("javiera", Now);
        var punto = c.PuntoRestauracion();
        c.MarcarRevisada("ana", Now);
        c.MarcarPedidaPorCorreo("Viña del Mar", "ana", Now);

        c.RestaurarEstado(punto, EstadoCarpeta.CambioDomicilioSolicitado, "ana", Now);

        Assert.False(c.PendienteReclasificacion);
    }

    // Puerta obligatoria: las vias genericas tampoco la saltan.
    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset)]
    public void CambiarEstado_hacia_la_familia_posterior_a_la_puerta_se_rechaza_si_esta_pendiente(EstadoCarpeta destino)
    {
        var c = Saliente();

        Assert.Throws<DomainException>(() => c.CambiarEstado(destino, "ana", Now));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.True(c.PendienteReclasificacion);
    }

    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset)]
    public void ForzarEstado_hacia_la_familia_posterior_a_la_puerta_se_rechaza_si_esta_pendiente(EstadoCarpeta destino)
    {
        var c = Saliente();

        var ex = Assert.Throws<DomainException>(() => c.ForzarEstado(destino, "ana", Now));

        Assert.Equal(Carpeta.MensajeReclasificacionPendiente, ex.Message);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.True(c.PendienteReclasificacion);
    }

    [Fact]
    public void Una_entrante_pendiente_tampoco_se_cierra_por_ForzarEstado_a_sin_registro()
    {
        var c = Entrante();

        Assert.Throws<DomainException>(() => c.ForzarEstado(EstadoCarpeta.CambioDomSinRegistro, "ana", Now));
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
    }

    [Fact]
    public void Tras_confirmar_CambiarEstado_y_ForzarEstado_a_la_familia_si_funcionan()
    {
        var a = Saliente();
        a.ConfirmarReclasificacion("ana", Now);
        a.CambiarEstado(EstadoCarpeta.CambioDomicilioSolicitado, "ana", Now);
        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, a.Estado);

        var b = Saliente();
        b.ConfirmarReclasificacion("ana", Now);
        b.ForzarEstado(EstadoCarpeta.CambioDomSubidoConaset, "ana", Now);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, b.Estado);
    }

    // MEDIUM-1: una entrante pendiente solo sale por 1° licencia / canje (o se queda); nada la blanquea en dos pasos.
    public static TheoryData<EstadoCarpeta> FueraDeLaFamiliaEntrante => new()
    {
        EstadoCarpeta.NoExisteCarpeta,
        EstadoCarpeta.SubidaConF8,
        EstadoCarpeta.SubidaConOficio,
        EstadoCarpeta.CrearCertificado,
        EstadoCarpeta.SubidaConaset,
        EstadoCarpeta.SeEncuentraEnArchivos,
        EstadoCarpeta.SeEncuentraEnOf43,
    };

    [Theory]
    [MemberData(nameof(FueraDeLaFamiliaEntrante))]
    public void Una_entrante_pendiente_no_sale_por_CambiarEstado_a_estados_fuera_de_la_familia(EstadoCarpeta destino)
    {
        var c = Entrante();

        // Archivos / Of. 43 ya los veta la tabla de transiciones; el resto, la puerta de Reclasificación.
        Assert.ThrowsAny<Exception>(() => c.CambiarEstado(destino, "ana", Now));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.True(c.PendienteReclasificacion);
        Assert.Equal(SentidoCambioDomicilio.Entrante, c.SentidoCambioDomicilio);
    }

    [Theory]
    [MemberData(nameof(FueraDeLaFamiliaEntrante))]
    public void Una_entrante_pendiente_no_sale_por_ForzarEstado_a_estados_fuera_de_la_familia(EstadoCarpeta destino)
    {
        var c = Entrante();

        var ex = Assert.Throws<DomainException>(() => c.ForzarEstado(destino, "ana", Now));

        Assert.Equal(Carpeta.MensajeEntrantePendienteSoloReclasificacion, ex.Message);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.True(c.PendienteReclasificacion);
    }

    [Theory]
    [InlineData(EstadoCarpeta.PrimeraLicencia)]
    [InlineData(EstadoCarpeta.CanjeLicExtranjera)]
    public void Una_entrante_pendiente_si_se_corrige_a_primera_licencia_o_canje(EstadoCarpeta destino)
    {
        var c = Entrante();

        c.ForzarEstado(destino, "ana", Now);

        Assert.Equal(destino, c.Estado);
        Assert.False(c.PendienteReclasificacion);
    }

    [Fact]
    public void El_blanqueo_en_dos_pasos_de_una_entrante_pendiente_queda_bloqueado()
    {
        var c = Entrante();

        Assert.Throws<DomainException>(() => c.ForzarEstado(EstadoCarpeta.SubidaConF8, "ana", Now));
        c.ForzarEstado(EstadoCarpeta.CambioDomicilio, "ana", Now.AddMinutes(1));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.True(c.PendienteReclasificacion);
    }

    [Fact]
    public void Tras_confirmar_la_entrante_si_sale_a_los_estados_de_su_flujo()
    {
        var c = Entrante();
        c.ConfirmarReclasificacion("ana", Now);

        c.ForzarEstado(EstadoCarpeta.SubidaConF8, "ana", Now);

        Assert.Equal(EstadoCarpeta.SubidaConF8, c.Estado);
        Assert.Equal(SentidoCambioDomicilio.Entrante, c.SentidoCambioDomicilio);
    }

    [Fact]
    public void Salir_de_una_pendiente_hacia_Archivos_o_canje_por_ForzarEstado_sigue_permitido()
    {
        var c = Saliente();

        c.ForzarEstado(EstadoCarpeta.CanjeLicExtranjera, "ana", Now);

        Assert.Equal(EstadoCarpeta.CanjeLicExtranjera, c.Estado);
        Assert.False(c.PendienteReclasificacion);
    }
}
