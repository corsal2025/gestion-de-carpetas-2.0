using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Ews;
using Sgl.Application.Carpetas;
using Sgl.Application.CambioDomicilio;
using Sgl.Application.Solicitudes;
using Sgl.Domain;
using Sgl.Infrastructure.CambioDomicilio;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>E13: «Nos piden carpetas» (entrante): clasificar por fecha, lupa, subida, las tres salidas, confirmación y plazo estimado.</summary>
public sealed class NosPidenEntrantesTests : IDisposable
{
    private const string CasillaPrueba = "buzon.prueba@example.test";
    private static readonly DateTime Antes = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sgl-entrantes-{Guid.NewGuid():N}");
    private readonly ComunaDirectory _directorio;
    private readonly MailFalso _mail = new();
    private int _rut = 11111111;

    public NosPidenEntrantesTests()
    {
        Directory.CreateDirectory(_dir);
        var csv = Path.Combine(_dir, "comunas.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Comuna,ContactEmail,Domain",
            "\"QUILPUE\",\"licencias@quilpue.example.test\",\"quilpue.example.test\""));
        _directorio = new ComunaDirectory(csv);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* archivo aún tomado por el proceso */ }
    }

    private sealed record Circuito(EntrantesService Entrantes, CarpetaService Carpetas, SolicitudesF8Service F8, CorreoComunasService Correo);

    /// <summary>One context and one gate for everything, like a scoped Blazor circuit.</summary>
    private Circuito Nuevo(bool real = false, string? casilla = CasillaPrueba)
    {
        var ctx = _db.NewContext();
        var gate = new UnidadDeTrabajoGate();
        var opciones = new CambioDomicilioOptions { EnvioReal = real };
        var correo = new CorreoComunasService(
            ctx, _mail, _directorio, new AppOptions { TestModeEmail = casilla }, opciones, gate, _db.Clock, NullLogger<CorreoComunasService>.Instance);
        var entrantes = new EntrantesService(
            new CarpetaRepository(ctx), new EntrantesRepository(ctx), new SolicitudF8Repository(ctx), correo, opciones, _db.Clock, gate);
        var f8 = new SolicitudesF8Service(
            new CarpetaRepository(ctx), new SolicitudF8Repository(ctx), new CertificadoRepository(ctx), new FirmantesRepository(ctx), _db.Clock, gate: gate);
        return new Circuito(entrantes, _db.NewServiceCompartido(ctx, gate), f8, correo);
    }

    private static string RutValido(int cuerpo)
    {
        var suma = 0;
        var factor = 2;
        for (var n = cuerpo; n > 0; n /= 10)
        {
            suma += n % 10 * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }
        var dv = 11 - suma % 11;
        return $"{cuerpo}-{(dv == 11 ? "0" : dv == 10 ? "K" : dv.ToString())}";
    }

    private async Task<Guid> SembrarAsync(
        string? fecha = null, string? comuna = "QUILPUE", string nombre = "Rosa", string apellido = "Melendez Araya", DateOnly? llego = null)
    {
        await using var ctx = _db.NewContext();
        var c = Carpeta.RegistrarDesdeCorreo(
            new Ciudadano(Rut.Parse(RutValido(_rut++)), nombre, apellido), Sede.AvArgentina, llego ?? new DateOnly(2026, 5, 1), comuna, false, "Sistema · correo", Antes);
        c.ConfirmarReclasificacion("setup", Antes);
        if (fecha is not null)
        {
            c.ClasificarEntrante(fecha, "setup", Antes);
        }
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<Guid> SembrarSalienteAsync()
    {
        await using var ctx = _db.NewContext();
        var c = Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse(RutValido(_rut++)), "Saliente", "Ficticia"), Sede.AvArgentina, new DateOnly(2026, 5, 1),
            new TimeOnly(9, 0), TramiteAgenda.Renovacion, "setup", Antes);
        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, "MAIPU", "setup", Antes);
        c.ConfirmarReclasificacion("setup", Antes);
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<CarpetaDetalleDto> DetalleAsync(Guid id) => await _db.NewService().ObtenerAsync(id);

    private async Task<List<EnvioCorreoComuna>> EnviosAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.EnviosCorreoComuna.AsNoTracking().Include(e => e.Carpetas).OrderBy(e => e.EnviadoEn).ToListAsync();
    }

    // Criterio 1: 2020-05-01 -> Archivo; 2024-01-10 -> Of. 43; 2026-09-01 -> Digital y ofrece solo la plantilla C.
    [Fact]
    public async Task Clasificar_segun_la_fecha_ubica_el_sector_y_una_digital_ofrece_solo_la_plantilla_C()
    {
        var archivo = await SembrarAsync();
        var oficina = await SembrarAsync();
        var digital = await SembrarAsync();
        var sinFecha = await SembrarAsync();
        var circuito = Nuevo();

        Assert.Equal(SectorFisico.Archivo, await circuito.Entrantes.ClasificarAsync(archivo, "01-05-2020", "ana"));
        Assert.Equal(SectorFisico.Oficina43, await circuito.Entrantes.ClasificarAsync(oficina, "2024-01-10", "ana"));
        Assert.Equal(SectorFisico.Digital, await circuito.Entrantes.ClasificarAsync(digital, "2026-09-01", "ana"));

        Assert.Equal([sinFecha], (await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.PorClasificar)).Select(f => f.Id));
        var enArchivo = Assert.Single(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Archivo));
        Assert.Equal(archivo, enArchivo.Id);
        Assert.Equal("2020-05-01", enArchivo.FechaUltimaCarpeta);
        Assert.Equal(oficina, Assert.Single(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Oficina43)).Id);
        var enDigital = Assert.Single(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Digital));
        Assert.Equal(digital, enDigital.Id);
        Assert.Equal(SalidasEntrante.CarpetaDigital, enDigital.Salidas);
        Assert.Equal(SalidasEntrante.NoEstaLaCarpeta | SalidasEntrante.SinRegistro, enArchivo.Salidas);
        Assert.Equal(SalidasEntrante.SinRegistro, (await Nuevo().Entrantes.ObtenerAsync(sinFecha)).Salidas);

        var resumen = await Nuevo().Entrantes.ResumenAsync();
        Assert.Equal((1, 1, 1, 1, 0), (resumen.PorClasificar, resumen.Archivo, resumen.Oficina43, resumen.Digital, resumen.Cerradas));
        Assert.Equal(1, await Nuevo().Entrantes.ContarPorClasificarAsync());

        // Una digital solo recibe la plantilla C: ni «sin registro» ni «no está la carpeta».
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.ResponderAsync(digital, TipoEnvioCorreoComuna.SinRegistro, null, null, null, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.NoEstaLaCarpetaAsync(digital, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.ResponderAsync(archivo, TipoEnvioCorreoComuna.Digital, null, null, null, "ana"));
        Assert.Empty(_mail.Intentos);
    }

    [Fact]
    public async Task Clasificar_rechaza_una_fecha_invalida_y_una_solicitud_que_ya_no_esta_abierta()
    {
        var id = await SembrarAsync();
        var saliente = await SembrarSalienteAsync();

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.ClasificarAsync(id, "no es fecha", "ana"));
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.ClasificarAsync(id, "2099-01-01", "ana"));
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.ClasificarAsync(saliente, "2024-01-10", "ana"));
        await Assert.ThrowsAsync<NotFoundException>(() => Nuevo().Entrantes.ClasificarAsync(Guid.NewGuid(), "2024-01-10", "ana"));
        Assert.Null((await DetalleAsync(id)).FechaUltimaCarpeta);
    }

    [Fact]
    public async Task La_fecha_se_puede_corregir_mientras_la_solicitud_esta_abierta_y_cambia_de_pestana()
    {
        var id = await SembrarAsync("2020-05-01");

        await Nuevo().Entrantes.ClasificarAsync(id, "2024-01-10", "ana");

        Assert.Empty(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Archivo));
        Assert.Equal(id, Assert.Single(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Oficina43)).Id);
    }

    // Criterio 2: «Subida a Conaset» de una entrante = CambioDomSubidoConaset con FechaSubida de hoy, nunca SubidaConaset.
    [Fact]
    public async Task Subida_a_Conaset_de_una_entrante_da_CambioDomSubidoConaset_con_la_fecha_de_hoy_y_se_puede_deshacer()
    {
        var id = await SembrarAsync("2024-01-10");
        var circuito = Nuevo();

        var cambio = await circuito.Entrantes.SubidaConasetAsync(id, "ana");

        var detalle = await DetalleAsync(id);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, detalle.Estado);
        Assert.NotEqual(EstadoCarpeta.SubidaConaset, detalle.Estado);
        Assert.Equal(new DateOnly(2026, 5, 4), detalle.FechaSubida);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, cambio.EstadoNuevo);
        var cerrada = Assert.Single(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Cerradas));
        Assert.Equal(id, cerrada.Id);
        Assert.True(cerrada.Confirmable);
        Assert.Empty(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Oficina43));

        await circuito.Entrantes.DeshacerAsync(id, cambio, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(id)).Estado);
        Assert.Equal(id, Assert.Single(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Oficina43)).Id);
    }

    [Fact]
    public async Task No_se_sube_una_entrante_sin_fecha_ni_una_digital()
    {
        var sinFecha = await SembrarAsync();
        var digital = await SembrarAsync("2026-09-01");

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.SubidaConasetAsync(sinFecha, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.SubidaConasetAsync(digital, "ana"));
    }

    // Criterio 3: salida A abre la SolicitudF8 y el flujo F8 -> certificado existente funciona de punta a punta.
    [Fact]
    public async Task Salida_A_crea_una_SolicitudF8_abierta_y_el_flujo_F8_a_certificado_funciona()
    {
        var id = await SembrarAsync("2020-05-01");
        var circuito = Nuevo();

        var solicitudId = await circuito.Entrantes.NoEstaLaCarpetaAsync(id, "ana");

        Assert.Equal(EstadoCarpeta.NoExisteCarpeta, (await DetalleAsync(id)).Estado);
        await using (var ctx = _db.NewContext())
        {
            var f8 = await ctx.Set<SolicitudF8>().AsNoTracking().SingleAsync();
            Assert.Equal(solicitudId, f8.Id);
            Assert.Equal(id, f8.CarpetaId);
            Assert.True(f8.EstaAbierta);
        }
        Assert.Empty(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Archivo));
        Assert.Empty(_mail.Intentos);

        // F8 que no se genera -> Crear certificado y se abre el certificado.
        var resultado = await circuito.F8.MarcarNoSeGeneroAsync(solicitudId, "javiera", "no aparece");

        Assert.Equal(EstadoCarpeta.CrearCertificado, (await DetalleAsync(id)).Estado);
        var certificado = Assert.Single((await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery())).Items);
        Assert.Equal(resultado.CertificadoId, certificado.Id);
        Assert.Equal(TipoCertificado.SinCarpeta, certificado.Tipo);
    }

    [Fact]
    public async Task Salida_A_con_F8_generado_termina_subida_con_F8_y_se_puede_confirmar_a_la_comuna()
    {
        var id = await SembrarAsync("2024-01-10");
        var circuito = Nuevo();
        var solicitudId = await circuito.Entrantes.NoEstaLaCarpetaAsync(id, "ana");

        await circuito.F8.MarcarSubidoAsync(solicitudId, "javiera");

        Assert.Equal(EstadoCarpeta.SubidaConF8, (await DetalleAsync(id)).Estado);
        var cerrada = Assert.Single(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Cerradas));
        Assert.True(cerrada.Confirmable);
        var r = await Nuevo().Entrantes.ResponderAsync(id, TipoEnvioCorreoComuna.ConfirmacionSubida, null, null, null, "ana");
        Assert.Equal(ResultadoEnvio.Enviado, r.Envio.Resultado);
    }

    [Fact]
    public async Task Salida_A_no_se_ofrece_sin_fecha_ni_a_una_saliente()
    {
        var sinFecha = await SembrarAsync();
        var saliente = await SembrarSalienteAsync();

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.NoEstaLaCarpetaAsync(sinFecha, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.NoEstaLaCarpetaAsync(saliente, "ana"));
        await using var ctx = _db.NewContext();
        Assert.Empty(ctx.Set<SolicitudF8>());
    }

    // Criterio 4: salida B con texto final ({nombre}, {rut}), línea de comuna solo si se indicó, estado SinRegistro, envío en modo prueba.
    [Fact]
    public async Task Salida_B_arma_el_texto_con_nombre_y_rut_y_la_linea_de_comuna_solo_si_se_indico()
    {
        var id = await SembrarAsync();
        var dto = await Nuevo().Entrantes.ObtenerAsync(id);

        var con = await Nuevo().Entrantes.VistaPreviaAsync(id, TipoEnvioCorreoComuna.SinRegistro, "Viña del Mar");
        var sin = await Nuevo().Entrantes.VistaPreviaAsync(id, TipoEnvioCorreoComuna.SinRegistro, "  ");

        Assert.Contains($"la carpeta de {dto.NombreCompleto}, RUT {dto.Rut}", con.Cuerpo);
        Assert.Contains("Según nuestra consulta, la persona figura registrada en la comuna de Viña del Mar.", con.Cuerpo);
        Assert.DoesNotContain("Según nuestra consulta", sin.Cuerpo);
        Assert.DoesNotContain("{", con.Cuerpo);
        Assert.DoesNotContain("{", sin.Cuerpo);
        Assert.Empty(_mail.Intentos);
    }

    [Fact]
    public async Task Salida_B_envia_en_modo_prueba_y_deja_la_carpeta_en_CambioDomSinRegistro()
    {
        var id = await SembrarAsync();

        var r = await Nuevo().Entrantes.ResponderAsync(id, TipoEnvioCorreoComuna.SinRegistro, "Viña del Mar", null, null, "ana");

        Assert.True(r.Cerrada);
        Assert.Equal(ResultadoEnvio.Enviado, r.Envio.Resultado);
        Assert.True(r.Envio.EsPrueba);
        Assert.Equal(EstadoCarpeta.CambioDomSinRegistro, (await DetalleAsync(id)).Estado);
        var enviado = Assert.Single(_mail.Enviados);
        Assert.Equal(CasillaPrueba, enviado.Para);
        Assert.StartsWith("[PRUEBA] ", enviado.Asunto);
        Assert.Contains("QUILPUE", enviado.Cuerpo);
        var envio = Assert.Single(await EnviosAsync());
        Assert.Equal(TipoEnvioCorreoComuna.SinRegistro, envio.Tipo);
        Assert.True(envio.EsPrueba);
        Assert.Equal(id, Assert.Single(envio.Carpetas).CarpetaId);
        Assert.Contains("la comuna de Viña del Mar", envio.Cuerpo);
        var cerrada = Assert.Single(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.Cerradas));
        Assert.Equal(TipoEnvioCorreoComuna.SinRegistro, cerrada.UltimoCorreo);
        Assert.True(cerrada.UltimoCorreoEsPrueba);
        Assert.False(cerrada.Confirmable);
    }

    [Fact]
    public async Task El_texto_editado_antes_de_enviar_es_el_que_sale()
    {
        var id = await SembrarAsync("2024-01-10");

        await Nuevo().Entrantes.ResponderAsync(id, TipoEnvioCorreoComuna.SinRegistro, null, "Asunto corregido", "Texto corregido a mano.", "ana");

        var envio = Assert.Single(await EnviosAsync());
        Assert.Equal("Asunto corregido", envio.Asunto);
        Assert.Equal("Texto corregido a mano.", envio.Cuerpo);
        Assert.Contains("Texto corregido a mano.", Assert.Single(_mail.Enviados).Cuerpo);
        Assert.Equal(EstadoCarpeta.CambioDomSinRegistro, (await DetalleAsync(id)).Estado);
    }

    [Fact]
    public async Task Si_el_correo_falla_o_la_comuna_no_tiene_correo_la_solicitud_sigue_abierta()
    {
        var falla = await SembrarAsync();
        _mail.FallaCuando = _ => true;
        var r = await Nuevo().Entrantes.ResponderAsync(falla, TipoEnvioCorreoComuna.SinRegistro, null, null, null, "ana");
        Assert.False(r.Cerrada);
        Assert.Equal(ResultadoEnvio.Fallido, r.Envio.Resultado);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(falla)).Estado);

        _mail.FallaCuando = null;
        var sinCorreo = await SembrarAsync(comuna: "RENCA");
        var s = await Nuevo().Entrantes.ResponderAsync(sinCorreo, TipoEnvioCorreoComuna.SinRegistro, null, null, null, "ana");
        Assert.False(s.Cerrada);
        Assert.Equal(ResultadoEnvio.SinCorreo, s.Envio.Resultado);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(sinCorreo)).Estado);

        // Reintentar solo el que falló: ahora sale y cierra.
        var otra = await Nuevo().Entrantes.ResponderAsync(falla, TipoEnvioCorreoComuna.SinRegistro, null, null, null, "ana");
        Assert.True(otra.Cerrada);
        Assert.Equal(EstadoCarpeta.CambioDomSinRegistro, (await DetalleAsync(falla)).Estado);
    }

    [Fact]
    public async Task Sin_casilla_de_prueba_y_con_el_envio_real_apagado_no_sale_nada_y_queda_simulado()
    {
        var id = await SembrarAsync();

        var r = await Nuevo(real: false, casilla: null).Entrantes.ResponderAsync(id, TipoEnvioCorreoComuna.SinRegistro, null, null, null, "ana");

        Assert.True(r.Cerrada);
        Assert.True(r.Envio.Simulado);
        Assert.Empty(_mail.Intentos);
        Assert.StartsWith(EnvioCorreoComuna.PrefijoSimulado, Assert.Single(await EnviosAsync()).Nota);
    }

    // Salida C
    [Fact]
    public async Task Salida_C_envia_la_plantilla_digital_con_la_fecha_y_deja_la_carpeta_en_CambioDomCarpetaDigital()
    {
        var id = await SembrarAsync("2026-09-01");

        var vista = await Nuevo().Entrantes.VistaPreviaAsync(id, TipoEnvioCorreoComuna.Digital, null);
        Assert.Contains("su última carpeta es de fecha 01-09-2026", vista.Cuerpo);

        var r = await Nuevo().Entrantes.ResponderAsync(id, TipoEnvioCorreoComuna.Digital, null, null, null, "ana");

        Assert.True(r.Cerrada);
        Assert.Equal(EstadoCarpeta.CambioDomCarpetaDigital, (await DetalleAsync(id)).Estado);
        var envio = Assert.Single(await EnviosAsync());
        Assert.Equal(TipoEnvioCorreoComuna.Digital, envio.Tipo);
        Assert.Contains("01-09-2026", envio.Cuerpo);
        Assert.Equal(CasillaPrueba, Assert.Single(_mail.Enviados).Para);
    }

    // «Corregir clasificación» se ofrece antes del correo «sin registro» (una primera licencia mal clasificada).
    [Fact]
    public async Task Corregir_la_clasificacion_saca_la_solicitud_del_modulo_deja_el_motivo_y_se_puede_deshacer()
    {
        var id = await SembrarAsync();
        var circuito = Nuevo();

        var cambio = await circuito.Entrantes.CorregirClasificacionAsync(id, EstadoCarpeta.PrimeraLicencia, "Era su primera licencia", "ana");

        var detalle = await DetalleAsync(id);
        Assert.Equal(EstadoCarpeta.PrimeraLicencia, detalle.Estado);
        Assert.Null(detalle.Sentido);
        Assert.Contains(detalle.Historial, h => h.Campo == Carpeta.CampoCorreccionClasificacion && h.Nuevo == "Era su primera licencia");
        Assert.Empty(await Nuevo().Entrantes.ListarAsync(PestanaEntrantes.PorClasificar));
        Assert.Empty(_mail.Intentos);

        await circuito.Entrantes.DeshacerAsync(id, cambio, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(id)).Estado);
        Assert.Equal(SentidoCambioDomicilio.Entrante, (await DetalleAsync(id)).Sentido);
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.CorregirClasificacionAsync(id, EstadoCarpeta.PrimeraLicencia, " ", "ana"));
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.CorregirClasificacionAsync(id, EstadoCarpeta.Otorgado, "x", "ana"));
    }

    // «Ya se subió» = confirmación a la comuna (Tipo ConfirmacionSubida), texto formal por defecto, editable.
    [Fact]
    public async Task Ya_se_subio_envia_la_confirmacion_con_el_texto_por_defecto_sin_cambiar_el_estado()
    {
        var id = await SembrarAsync("2024-01-10");
        await Nuevo().Entrantes.SubidaConasetAsync(id, "ana");
        var dto = await Nuevo().Entrantes.ObtenerAsync(id);
        Assert.False(dto.Confirmada);

        var vista = await Nuevo().Entrantes.VistaPreviaAsync(id, TipoEnvioCorreoComuna.ConfirmacionSubida, null);
        Assert.Contains($"Estimados:\n\nJunto con saludar, respecto a su solicitud de la carpeta de {dto.NombreCompleto}, RUT {dto.Rut}, informamos que la carpeta fue subida a la plataforma.", vista.Cuerpo);
        Assert.Contains("Saludos cordiales,\nDepartamento de Licencias de Conducir\nMunicipalidad de Valparaíso", vista.Cuerpo);

        var r = await Nuevo().Entrantes.ResponderAsync(id, TipoEnvioCorreoComuna.ConfirmacionSubida, null, null, null, "ana");

        Assert.False(r.Cerrada);
        Assert.Equal(ResultadoEnvio.Enviado, r.Envio.Resultado);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, (await DetalleAsync(id)).Estado);
        var envio = Assert.Single(await EnviosAsync());
        Assert.Equal(TipoEnvioCorreoComuna.ConfirmacionSubida, envio.Tipo);
        Assert.True(envio.EsPrueba);
        Assert.Equal(vista.Cuerpo, envio.Cuerpo);
        var cerrada = await Nuevo().Entrantes.ObtenerAsync(id);
        Assert.True(cerrada.Confirmada);
        Assert.True(cerrada.UltimoCorreoEsPrueba);

        // Una segunda prueba no repite el correo.
        var otra = await Nuevo().Entrantes.ResponderAsync(id, TipoEnvioCorreoComuna.ConfirmacionSubida, null, null, null, "ana");
        Assert.Equal(ResultadoEnvio.YaEnviado, otra.Envio.Resultado);
        Assert.Single(_mail.Enviados);
    }

    [Fact]
    public async Task La_confirmacion_solo_aplica_a_una_entrante_ya_subida_y_el_texto_se_puede_editar()
    {
        var abierta = await SembrarAsync("2024-01-10");
        var saliente = await SembrarSalienteAsync();
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.ResponderAsync(abierta, TipoEnvioCorreoComuna.ConfirmacionSubida, null, null, null, "ana"));
        await Assert.ThrowsAsync<NotFoundException>(() => Nuevo().Entrantes.ResponderAsync(saliente, TipoEnvioCorreoComuna.ConfirmacionSubida, null, null, null, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.ResponderAsync(abierta, TipoEnvioCorreoComuna.Pedido, null, null, null, "ana"));

        await Nuevo().Entrantes.SubidaConasetAsync(abierta, "ana");
        await Nuevo().Entrantes.ResponderAsync(abierta, TipoEnvioCorreoComuna.ConfirmacionSubida, null, "Listo", "Ya está la carpeta.", "ana");

        var envio = Assert.Single(await EnviosAsync());
        Assert.Equal("Listo", envio.Asunto);
        Assert.Equal("Ya está la carpeta.", envio.Cuerpo);
    }

    // Criterio 6: la lupa de una entrante nunca crea envío ni pide la carpeta a nadie.
    [Fact]
    public async Task La_lupa_de_una_entrante_cicla_sus_tres_posiciones_sin_crear_envios_ni_correos()
    {
        var id = await SembrarAsync("2024-01-10");
        var circuito = Nuevo();

        await circuito.Entrantes.AvanzarLupaAsync(id, "ana");
        var enBusqueda = await Nuevo().Entrantes.ObtenerAsync(id);
        Assert.True(enBusqueda.EnBusqueda);
        Assert.False(enBusqueda.Encontrada);

        await circuito.Entrantes.AvanzarLupaAsync(id, "ana");
        var encontrada = await Nuevo().Entrantes.ObtenerAsync(id);
        Assert.False(encontrada.EnBusqueda);
        Assert.True(encontrada.Encontrada);

        await circuito.Entrantes.AvanzarLupaAsync(id, "ana");
        var limpia = await Nuevo().Entrantes.ObtenerAsync(id);
        Assert.False(limpia.EnBusqueda || limpia.Encontrada);

        Assert.Empty(await EnviosAsync());
        Assert.Empty(_mail.Intentos);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(id)).Estado);
    }

    [Fact]
    public async Task La_lupa_no_aplica_a_una_digital_ni_a_una_sin_fecha()
    {
        var digital = await SembrarAsync("2026-09-01");
        var sinFecha = await SembrarAsync();

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.AvanzarLupaAsync(digital, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.AvanzarLupaAsync(sinFecha, "ana"));
    }

    [Fact]
    public void El_servicio_de_entrantes_no_conoce_el_sistema_de_peticiones_ni_su_envio()
    {
        var tipos = typeof(EntrantesService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.FullName ?? string.Empty);

        Assert.DoesNotContain(tipos, t => t.Contains("PeticionSender", StringComparison.Ordinal) || t.Contains("PeticionRepo", StringComparison.Ordinal)
            || t.Contains("IMailSender", StringComparison.Ordinal));
    }

    // Plazo estimado: fecha del correo + PlazoEntranteDias días hábiles.
    [Fact]
    public async Task El_plazo_cuenta_desde_el_dia_que_llego_el_correo_y_es_un_estimado()
    {
        var id = await SembrarAsync(llego: new DateOnly(2026, 5, 1));

        var dto = await Nuevo().Entrantes.ObtenerAsync(id);

        Assert.Equal(new DateOnly(2026, 5, 1), dto.FechaCorreo);
        Assert.Equal(new DateOnly(2026, 5, 19), dto.Vence);
        Assert.Equal(15, dto.DiasRestantes);
        Assert.Equal(PlazoColor.Verde, dto.Plazo);

        _db.AdvanceClock(TimeSpan.FromDays(13));
        var amarillo = await Nuevo().Entrantes.ObtenerAsync(id);
        Assert.Equal(2, amarillo.DiasRestantes);
        Assert.Equal(PlazoColor.Amarillo, amarillo.Plazo);

        _db.AdvanceClock(TimeSpan.FromDays(5));
        var rojo = await Nuevo().Entrantes.ObtenerAsync(id);
        Assert.True(rojo.DiasRestantes < 0);
        Assert.Equal(PlazoColor.Rojo, rojo.Plazo);
    }

    [Fact]
    public async Task El_plazo_usa_la_configuracion_PlazoEntranteDias()
    {
        var id = await SembrarAsync(llego: new DateOnly(2026, 5, 1));
        var ctx = _db.NewContext();
        var gate = new UnidadDeTrabajoGate();
        var opciones = new CambioDomicilioOptions { PlazoEntranteDias = 5 };
        var servicio = new EntrantesService(
            new CarpetaRepository(ctx), new EntrantesRepository(ctx), new SolicitudF8Repository(ctx), Nuevo().Correo, opciones, _db.Clock, gate);

        Assert.Equal(new DateOnly(2026, 5, 8), (await servicio.ObtenerAsync(id)).Vence);
    }

    [Fact]
    public async Task El_plazo_entrante_descuenta_feriado_adicional_configurado()
    {
        var id = await SembrarAsync(llego: new DateOnly(2026, 5, 1));
        var ctx = _db.NewContext();
        var opciones = new CambioDomicilioOptions { PlazoEntranteDias = 5, FeriadosAdicionales = new HashSet<DateOnly> { new(2026, 5, 4) } };
        var servicio = new EntrantesService(new CarpetaRepository(ctx), new EntrantesRepository(ctx), new SolicitudF8Repository(ctx), Nuevo().Correo, opciones, _db.Clock, new UnidadDeTrabajoGate());
        Assert.Equal(new DateOnly(2026, 5, 11), (await servicio.ObtenerAsync(id)).Vence);
    }

    // PDF propio por sector (se reutiliza el documento del listado para búsqueda).
    [Fact]
    public async Task El_listado_para_imprimir_se_arma_por_sector_ordenado_por_fecha_y_sin_mezclar()
    {
        var vieja = await SembrarAsync("2019-03-02", nombre: "Ana", apellido: "Zapata");
        var antigua = await SembrarAsync("2021-11-20", nombre: "Beto", apellido: "Alvarez");
        var oficina = await SembrarAsync("2024-01-10");
        var digital = await SembrarAsync("2026-09-01");
        await SembrarAsync();
        await Nuevo().Entrantes.AvanzarLupaAsync(antigua, "ana");

        var archivo = await Nuevo().Entrantes.ObtenerListadoAsync(SectorFisico.Archivo);
        var of43 = await Nuevo().Entrantes.ObtenerListadoAsync(SectorFisico.Oficina43);
        var dig = await Nuevo().Entrantes.ObtenerListadoAsync(SectorFisico.Digital);
        var enBusqueda = await Nuevo().Entrantes.ObtenerListadoAsync(SectorFisico.Archivo, soloEnBusqueda: true);

        Assert.Equal([vieja, antigua], archivo.Ids);
        Assert.Equal("Archivo", archivo.Sector);
        Assert.StartsWith("NosPiden-Archivo-", archivo.Folio);
        Assert.Equal(2, archivo.Total);
        Assert.Equal("02/MAR/2019", archivo.Items[0].FechaUltimaCarpeta);
        Assert.Equal([oficina], of43.Ids);
        Assert.Equal("Oficina 43", of43.Sector);
        Assert.Equal([digital], dig.Ids);
        Assert.Equal("Digital", dig.Sector);
        Assert.Equal([antigua], enBusqueda.Ids);
        Assert.Contains("Nos piden carpetas", archivo.Titulo);
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Entrantes.ObtenerListadoAsync("OTRO"));
    }

    [Fact]
    public async Task Las_salientes_no_aparecen_en_ninguna_pestana_de_nos_piden()
    {
        await SembrarSalienteAsync();
        var entrante = await SembrarAsync();

        var circuito = Nuevo();
        var todas = new List<Guid>();
        foreach (var pestana in Enum.GetValues<PestanaEntrantes>())
        {
            todas.AddRange((await circuito.Entrantes.ListarAsync(pestana)).Select(f => f.Id));
        }

        Assert.Equal([entrante], todas);
    }

    [Fact]
    public async Task La_lista_de_descartados_se_lee_y_se_puede_quitar_una_entrada()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.CorreosDescartados.Add(new CorreoDescartadoRegistro("<a@x>", "Asunto uno", "alguien@example.test", Antes, "No se encontró un RUT válido", Antes));
            ctx.CorreosDescartados.Add(new CorreoDescartadoRegistro("<b@x>", "Asunto dos", "otro@example.test", Antes, "No se encontró un RUT válido", Antes.AddHours(1)));
            await ctx.SaveChangesAsync();
        }

        var lista = await Nuevo().Entrantes.ListarDescartadosAsync();
        Assert.Equal(["Asunto dos", "Asunto uno"], lista.Select(d => d.Asunto));
        Assert.Equal(2, (await Nuevo().Entrantes.ResumenAsync()).Descartados);

        Assert.True(await Nuevo().Entrantes.QuitarDescartadoAsync(lista[0].Id));
        Assert.False(await Nuevo().Entrantes.QuitarDescartadoAsync(lista[0].Id));
        Assert.Equal(["Asunto uno"], (await Nuevo().Entrantes.ListarDescartadosAsync()).Select(d => d.Asunto));
    }

    private sealed record Mensaje(string Para, string Asunto, string Cuerpo);

    private sealed class MailFalso : IMailSender
    {
        public Func<string, bool>? FallaCuando { get; set; }

        public List<string> Intentos { get; } = [];

        public List<Mensaje> Enviados { get; } = [];

        public bool IsConfigured => true;

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
        {
            Intentos.Add(toAddress);
            if (FallaCuando?.Invoke(toAddress) == true)
            {
                throw new InvalidOperationException("falla simulada");
            }
            Enviados.Add(new Mensaje(toAddress, subject, body));
            return Task.CompletedTask;
        }
    }
}
