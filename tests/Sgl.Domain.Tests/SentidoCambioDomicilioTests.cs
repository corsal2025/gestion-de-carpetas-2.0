using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>E8: dirección del caso de cambio de domicilio (saliente / entrante), estados de cierre y corrección de clasificación.</summary>
public class SentidoCambioDomicilioTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Saliente: born from the agenda and classified by a person as «Cambio de domicilio».</summary>
    private static Carpeta Saliente(EstadoCarpeta? destino = null)
    {
        var c = Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse("12345678-5"), "JUANA", "PEREZ SOTO"), Sede.AvArgentina,
            new DateOnly(2026, 10, 30), new TimeOnly(9, 30), TramiteAgenda.Renovacion, "importador", Now);
        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, "Viña del Mar", "javiera", Now);
        c.ConfirmarReclasificacion("javiera", Now);
        if (destino is { } d && d != EstadoCarpeta.CambioDomicilio)
        {
            c.CambiarEstado(d, "javiera", Now);
        }
        return c;
    }

    /// <summary>Entrante: born from the mail sync.</summary>
    private static Carpeta Entrante()
    {
        var c = Carpeta.RegistrarDesdeCorreo(
            new Ciudadano(Rut.Parse("12345678-5"), "JUANA", "PEREZ SOTO"), Sede.AvArgentina,
            new DateOnly(2026, 10, 29), "Viña del Mar", false, "Sistema · correo", Now);
        c.ConfirmarReclasificacion("javiera", Now);
        return c;
    }

    public static TheoryData<EstadoCarpeta> VetadosSaliente() => new()
    {
        EstadoCarpeta.NoExisteCarpeta,
        EstadoCarpeta.SubidaConF8,
        EstadoCarpeta.SubidaConOficio,
        EstadoCarpeta.CrearCertificado,
        EstadoCarpeta.SubidaConaset,
    };

    // Criterio 1
    [Fact]
    public void Clasificar_a_cambio_de_domicilio_fija_la_direccion_saliente()
    {
        Assert.Equal(SentidoCambioDomicilio.Saliente, Saliente().SentidoCambioDomicilio);
    }

    [Fact]
    public void Saliente_recorre_CambioDomicilio_Solicitado_y_cierra_en_Conaset_o_en_correo()
    {
        var porConaset = Saliente(EstadoCarpeta.CambioDomicilioSolicitado);
        porConaset.CambiarEstado(EstadoCarpeta.CambioDomSubidoConaset, "usuario", Now);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, porConaset.Estado);

        var porCorreo = Saliente(EstadoCarpeta.CambioDomicilioSolicitado);
        porCorreo.CambiarEstado(EstadoCarpeta.CambioDomSubidoCorreo, "usuario", Now);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoCorreo, porCorreo.Estado);
        Assert.Equal(Bandeja.CambioDomSubidoCorreo, porCorreo.Bandeja);
    }

    [Fact]
    public void Saliente_en_Por_pedir_puede_marcarse_ya_subida_sin_envio()
    {
        var c = Saliente();
        c.CambiarEstado(EstadoCarpeta.CambioDomSubidoConaset, "usuario", Now);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, c.Estado);
    }

    [Theory]
    [MemberData(nameof(VetadosSaliente))]
    public void Saliente_rechaza_F8_oficio_certificado_y_no_existe_con_CambiarEstado(EstadoCarpeta destino)
    {
        foreach (var origen in new[] { EstadoCarpeta.CambioDomicilio, EstadoCarpeta.CambioDomicilioSolicitado })
        {
            var c = Saliente(origen);
            Assert.Throws<InvalidTransitionException>(() => c.CambiarEstado(destino, "usuario", Now));
            Assert.Equal(origen, c.Estado);
        }
    }

    [Theory]
    [MemberData(nameof(VetadosSaliente))]
    public void Saliente_rechaza_F8_oficio_certificado_y_no_existe_con_ForzarEstado(EstadoCarpeta destino)
    {
        foreach (var origen in new[] { EstadoCarpeta.CambioDomicilio, EstadoCarpeta.CambioDomicilioSolicitado })
        {
            var c = Saliente(origen);
            Assert.Throws<DomainException>(() => c.ForzarEstado(destino, "usuario", Now));
            Assert.Equal(origen, c.Estado);
        }
    }

    [Fact]
    public void Saliente_solo_llega_a_subido_con_correo_desde_Solicitado()
    {
        var porPedir = Saliente();
        Assert.Throws<InvalidTransitionException>(() => porPedir.CambiarEstado(EstadoCarpeta.CambioDomSubidoCorreo, "usuario", Now));
        Assert.False(EstadoTransitions.CanTransition(EstadoCarpeta.CambioDomicilio, EstadoCarpeta.CambioDomSubidoCorreo, SentidoCambioDomicilio.Saliente));
        Assert.True(EstadoTransitions.CanTransition(EstadoCarpeta.CambioDomicilioSolicitado, EstadoCarpeta.CambioDomSubidoCorreo, SentidoCambioDomicilio.Saliente));
    }

    [Theory]
    [InlineData(EstadoCarpeta.CambioDomSinRegistro)]
    [InlineData(EstadoCarpeta.CambioDomCarpetaDigital)]
    public void Saliente_no_puede_cerrarse_con_los_estados_del_entrante(EstadoCarpeta destino)
    {
        var c = Saliente();
        Assert.Throws<InvalidTransitionException>(() => c.CambiarEstado(destino, "usuario", Now));
        Assert.Throws<DomainException>(() => c.ForzarEstado(destino, "usuario", Now));
    }

    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    public void Saliente_no_se_fuerza_a_subido_con_correo_sin_pasar_por_la_prueba(EstadoCarpeta origen)
    {
        var c = Saliente(origen);

        Assert.False(Carpeta.PermiteForzar(origen, EstadoCarpeta.CambioDomSubidoCorreo, SentidoCambioDomicilio.Saliente));
        Assert.Throws<DomainException>(() => c.ForzarEstado(EstadoCarpeta.CambioDomSubidoCorreo, "usuario", Now));
        Assert.Equal(origen, c.Estado);
    }

    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo)]
    public void Entrante_no_se_fuerza_a_Solicitado_ni_a_subido_con_correo(EstadoCarpeta destino)
    {
        var c = Entrante();

        Assert.False(Carpeta.PermiteForzar(EstadoCarpeta.CambioDomicilio, destino, SentidoCambioDomicilio.Entrante));
        Assert.Throws<DomainException>(() => c.ForzarEstado(destino, "usuario", Now));
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
    }

    [Fact]
    public void Forzar_fuera_de_la_familia_quita_la_direccion_y_lo_deja_en_el_historial()
    {
        var c = Saliente();

        c.ForzarEstado(EstadoCarpeta.CanjeLicExtranjera, "usuario", Now);

        Assert.Null(c.SentidoCambioDomicilio);
        Assert.Contains(c.Historial, h => h.Campo == "Dirección" && h.Anterior == "Saliente" && h.Nuevo is null);
    }

    [Fact]
    public void Forzar_dentro_de_la_familia_o_a_las_salidas_de_F8_del_entrante_conserva_la_direccion()
    {
        var saliente = Saliente();
        saliente.ForzarEstado(EstadoCarpeta.CambioDomSubidoConaset, "usuario", Now);
        Assert.Equal(SentidoCambioDomicilio.Saliente, saliente.SentidoCambioDomicilio);

        var entrante = Entrante();
        entrante.ForzarEstado(EstadoCarpeta.SubidaConF8, "usuario", Now);
        Assert.Equal(SentidoCambioDomicilio.Entrante, entrante.SentidoCambioDomicilio);
    }

    [Fact]
    public void Forzar_a_la_familia_desde_fuera_sin_direccion_se_rechaza()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.PrimeraLicencia);

        var ex = Assert.Throws<DomainException>(() => c.ForzarEstado(EstadoCarpeta.CambioDomicilio, "usuario", Now));

        Assert.Contains("Clasificar", ex.Message);
        Assert.Equal(EstadoCarpeta.PrimeraLicencia, c.Estado);
        Assert.Null(c.SentidoCambioDomicilio);
    }

    [Fact]
    public void Forzar_a_la_familia_desde_fuera_con_direccion_la_fija()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.PrimeraLicencia);

        c.ForzarEstado(EstadoCarpeta.CambioDomicilio, "usuario", Now, SentidoCambioDomicilio.Saliente);

        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.Equal(SentidoCambioDomicilio.Saliente, c.SentidoCambioDomicilio);
    }

    // Importación: lo que el Excel trae dentro de la familia es saliente (lo piden personas de la agenda).
    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilio, SentidoCambioDomicilio.Saliente)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado, SentidoCambioDomicilio.Saliente)]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset, SentidoCambioDomicilio.Saliente)]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo, SentidoCambioDomicilio.Saliente)]
    [InlineData(EstadoCarpeta.SubidaConaset, null)]
    public void Importada_en_la_familia_queda_saliente(EstadoCarpeta estado, SentidoCambioDomicilio? esperado)
    {
        var c = Carpeta.CrearImportada(
            Guid.NewGuid(), new Ciudadano(Rut.Parse("12345678-5"), "JUANA", "PEREZ SOTO"), Sede.AvArgentina,
            new DateOnly(2026, 10, 1), null, null, estado, Decision.Pendiente, null, null, null, null, Now);

        Assert.Equal(esperado, c.SentidoCambioDomicilio);
    }

    // Criterio 2
    [Fact]
    public void Registrar_desde_correo_fija_la_direccion_entrante()
    {
        Assert.Equal(SentidoCambioDomicilio.Entrante, Entrante().SentidoCambioDomicilio);
    }

    [Theory]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.CambioDomSinRegistro)]
    [InlineData(EstadoCarpeta.CambioDomCarpetaDigital)]
    public void Entrante_en_CambioDomicilio_llega_a_sus_salidas(EstadoCarpeta destino)
    {
        var c = Entrante();
        c.CambiarEstado(destino, "usuario", Now);
        Assert.Equal(destino, c.Estado);
    }

    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo)]
    public void Entrante_no_puede_pasar_a_Solicitado_ni_a_subido_con_correo(EstadoCarpeta destino)
    {
        var c = Entrante();
        Assert.Throws<InvalidTransitionException>(() => c.CambiarEstado(destino, "usuario", Now));
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
    }

    [Theory]
    [InlineData(EstadoCarpeta.CambioDomSinRegistro)]
    [InlineData(EstadoCarpeta.CambioDomCarpetaDigital)]
    public void Cierres_del_entrante_van_a_su_bandeja_propia_y_cierran_el_proceso(EstadoCarpeta destino)
    {
        var c = Entrante();
        c.CambiarEstado(destino, "usuario", Now);

        Assert.Equal(Bandeja.CambioDomCerradoEntrante, c.Bandeja);
        Assert.Equal(Bandeja.CambioDomCerradoEntrante, Carpeta.BandejaDeOrigen(destino));
        Assert.Equal(Bandeja.CambioDomCerradoEntrante, Carpeta.ClasificarBandeja(destino, Decision.Pendiente, null));
        Assert.NotNull(c.FechaCierre);
        Assert.Null(c.FechaSubida);
        Assert.True(Carpeta.EsEstadoCierre(destino));
        Assert.False(Carpeta.EsParaOtorgar(destino));
    }

    [Fact]
    public void Entrante_subida_no_es_para_otorgar_en_ningun_estado()
    {
        var c = Entrante();
        c.CambiarEstado(EstadoCarpeta.CambioDomSubidoConaset, "usuario", Now);

        Assert.True(Carpeta.EsParaOtorgar(EstadoCarpeta.CambioDomSubidoConaset));
        Assert.Throws<DomainException>(() => c.Otorgar("matias", Now));
        Assert.Throws<DomainException>(() => c.AsignarResolucion(Resolucion.EsperaExamen, "matias", Now));
        Assert.False(Carpeta.ParaOtorgarExpresion.Compile()(c));

        var saliente = Saliente();
        saliente.CambiarEstado(EstadoCarpeta.CambioDomSubidoConaset, "usuario", Now);
        Assert.True(Carpeta.ParaOtorgarExpresion.Compile()(saliente));
    }

    // Lupa: solo la entrante busca una carpeta física; la saliente se pide a la otra comuna.
    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    public void La_lupa_no_aplica_a_una_saliente(EstadoCarpeta estado)
    {
        var c = Saliente(estado);

        Assert.Throws<DomainException>(() => c.AvanzarLupa("usuario", Now));
        Assert.Throws<DomainException>(() => c.MarcarPendienteBusqueda(true, "usuario", Now));

        Assert.False(c.PendienteBusqueda);
        Assert.False(c.CarpetaEncontrada);
        c.MarcarPendienteBusqueda(false, "usuario", Now);
    }

    [Fact]
    public void La_lupa_sigue_disponible_para_la_entrante()
    {
        var c = Entrante();

        c.AvanzarLupa("usuario", Now);
        Assert.True(c.PendienteBusqueda);
        c.AvanzarLupa("usuario", Now);
        Assert.True(c.CarpetaEncontrada);
    }

    // Criterio 3
    [Fact]
    public void Corregir_a_primera_licencia_deja_motivo_en_el_historial_y_quita_la_direccion()
    {
        var c = Saliente();

        c.CorregirClasificacionCambioDom(EstadoCarpeta.PrimeraLicencia, "Era primera licencia", null, "javiera", Now);

        Assert.Equal(EstadoCarpeta.PrimeraLicencia, c.Estado);
        Assert.Null(c.SentidoCambioDomicilio);
        var linea = Assert.Single(c.Historial, h => h.Campo == "Corrección de clasificación");
        Assert.Equal("Era primera licencia", linea.Nuevo);
        Assert.Equal("javiera", linea.Usuario);
        Assert.Contains(c.Historial, h => h.Campo == "Dirección" && h.Anterior == "Saliente" && h.Nuevo is null);
    }

    [Fact]
    public void Deshacer_la_correccion_devuelve_estado_y_direccion()
    {
        var c = Saliente();
        var punto = c.PuntoRestauracion();
        c.CorregirClasificacionCambioDom(EstadoCarpeta.CanjeLicExtranjera, "Es canje", null, "javiera", Now);
        Assert.Null(c.SentidoCambioDomicilio);

        c.RestaurarEstado(punto, EstadoCarpeta.CanjeLicExtranjera, "javiera", Now.AddMinutes(1));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.Equal(SentidoCambioDomicilio.Saliente, c.SentidoCambioDomicilio);
    }

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, "15/03/2019")]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43, "10/01/2024")]
    public void Corregir_a_archivos_u_of43_se_decide_por_la_fecha(EstadoCarpeta destino, string fecha)
    {
        var c = Entrante();

        c.CorregirClasificacionCambioDom(destino, "Ya tenía carpeta", fecha, "javiera", Now);

        Assert.Equal(destino, c.Estado);
        Assert.NotNull(c.FechaUltimaCarpeta);
        Assert.Null(c.SentidoCambioDomicilio);
    }

    [Fact]
    public void Corregir_a_archivos_con_fecha_de_of43_o_sin_fecha_se_rechaza_sin_cambiar_nada()
    {
        var c = Saliente();
        var historial = c.Historial.Count;

        Assert.Throws<DomainException>(() => c.CorregirClasificacionCambioDom(EstadoCarpeta.SeEncuentraEnArchivos, "x", "10/01/2024", "javiera", Now));
        Assert.Throws<DomainException>(() => c.CorregirClasificacionCambioDom(EstadoCarpeta.SeEncuentraEnOf43, "x", null, "javiera", Now));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.Equal(SentidoCambioDomicilio.Saliente, c.SentidoCambioDomicilio);
        Assert.Equal(historial, c.Historial.Count);
    }

    [Fact]
    public void Corregir_a_no_existe_y_a_cambio_de_domicilio_son_destinos_validos()
    {
        var a = Entrante();
        a.CorregirClasificacionCambioDom(EstadoCarpeta.NoExisteCarpeta, "No está en el archivo", null, "javiera", Now);
        Assert.Equal(EstadoCarpeta.NoExisteCarpeta, a.Estado);
        Assert.Null(a.SentidoCambioDomicilio);

        var b = Saliente(EstadoCarpeta.CambioDomicilioSolicitado);
        b.CorregirClasificacionCambioDom(EstadoCarpeta.CambioDomicilio, "Se volvió a Por pedir", null, "javiera", Now);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, b.Estado);
        Assert.Equal(SentidoCambioDomicilio.Saliente, b.SentidoCambioDomicilio);
    }

    [Fact]
    public void Corregir_exige_motivo_destino_valido_y_carpeta_de_la_familia()
    {
        var c = Saliente();
        Assert.Throws<DomainException>(() => c.CorregirClasificacionCambioDom(EstadoCarpeta.PrimeraLicencia, "  ", null, "javiera", Now));
        Assert.Throws<DomainException>(() => c.CorregirClasificacionCambioDom(EstadoCarpeta.PrimeraLicencia, new string('m', Carpeta.MaxMotivoCorreccionLength + 1), null, "javiera", Now));
        Assert.Throws<DomainException>(() => c.CorregirClasificacionCambioDom(EstadoCarpeta.SubidaConaset, "x", null, "javiera", Now));
        Assert.Throws<DomainException>(() => c.CorregirClasificacionCambioDom(EstadoCarpeta.PrimeraLicencia, "x", null, " ", Now));
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);

        var fuera = CarpetaFactory.InEstado(EstadoCarpeta.PrimeraLicencia);
        Assert.Throws<DomainException>(() => fuera.CorregirClasificacionCambioDom(EstadoCarpeta.CanjeLicExtranjera, "x", null, "javiera", Now));
    }
}
