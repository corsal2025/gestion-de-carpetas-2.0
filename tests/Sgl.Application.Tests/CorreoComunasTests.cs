using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Ews;
using Sgl.Application;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;
using Sgl.Infrastructure;
using Sgl.Infrastructure.CambioDomicilio;

namespace Sgl.Application.Tests;

/// <summary>E9: registro de envíos por comuna, modo prueba, directorio de comunas y plantillas B y C.</summary>
public sealed class CorreoComunasTests : IDisposable
{
    private const string CasillaPrueba = "buzon.prueba@example.test";
    private static readonly DateTime Antes = new(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Ruts = ["12345678-5", "18785387-7", "18586259-3", "11111111-1", "22222222-2"];

    private readonly TestDb _db = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sgl-comunas-{Guid.NewGuid():N}");
    private readonly ComunaDirectory _directorio;
    private readonly MailFalso _mail = new();
    private readonly LogCapturado _log = new();
    private int _rut;

    public CorreoComunasTests()
    {
        Directory.CreateDirectory(_dir);
        var csv = Path.Combine(_dir, "comunas.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Comuna,ContactEmail,Domain",
            "\"MAIPU\",\"uno@maipu.example.test\",\"maipu.example.test\"",
            "\"MAIPU\",\"dos@maipu.example.test\",\"maipu.example.test\"",
            "\"RENCA\",\"licencias@renca.example.test\",\"renca.example.test\""));
        _directorio = new ComunaDirectory(csv);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* archivo aún tomado por el proceso */ }
    }

    private CorreoComunasService Servicio(
        bool real = true, string? casillaPrueba = null, IMailSender? mail = null, ILogger<CorreoComunasService>? log = null) =>
        new(_db.NewContext(), mail ?? _mail, _directorio, new AppOptions { TestModeEmail = casillaPrueba },
            new CambioDomicilioOptions { EnvioReal = real }, new UnidadDeTrabajoGate(), _db.Clock, log ?? _log);

    private async Task<Guid> SembrarAsync(string? comuna, string nombre = "Zacarias", string apellido = "Quimbaya", bool entrante = false)
    {
        await using var ctx = _db.NewContext();
        var persona = new Ciudadano(Rut.Parse(Ruts[_rut++]), nombre, apellido);
        Carpeta c;
        if (entrante)
        {
            c = Carpeta.RegistrarDesdeCorreo(persona, Sede.AvArgentina, new DateOnly(2026, 5, 1), comuna, false, "Sistema · correo", Antes);
        }
        else
        {
            c = Carpeta.CrearDesdeAgenda(persona, Sede.AvArgentina, new DateOnly(2026, 5, 1), new TimeOnly(9, 0), TramiteAgenda.Renovacion, "setup", Antes);
            c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, comuna, "setup", Antes);
        }
        c.ConfirmarReclasificacion("setup", Antes);
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<List<EnvioCorreoComuna>> EnviosAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.EnviosCorreoComuna.AsNoTracking().Include(e => e.Carpetas).OrderBy(e => e.Comuna).ToListAsync();
    }

    // Criterio 1: EnvioReal=false -> casilla de prueba, [PRUEBA], cabecera con la comuna; sin casilla no sale nada.
    [Fact]
    public async Task Con_el_envio_real_apagado_el_correo_va_a_la_casilla_de_prueba_con_cabecera_y_marca_de_prueba()
    {
        var id = await SembrarAsync("MAIPU");

        var r = Assert.Single(await Servicio(real: false, casillaPrueba: CasillaPrueba).EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));

        Assert.Equal(ResultadoEnvio.Enviado, r.Resultado);
        Assert.True(r.EsPrueba);
        Assert.False(r.Simulado);
        var m = Assert.Single(_mail.Enviados);
        Assert.Equal(CasillaPrueba, m.Para);
        Assert.Equal($"[PRUEBA] {EmailTemplate.Subject()}", m.Asunto);
        Assert.StartsWith("*** CORREO DE PRUEBA", m.Cuerpo);
        Assert.Contains("Comuna destino real: MAIPU", m.Cuerpo);
        Assert.Contains("uno@maipu.example.test", m.Cuerpo);
        var envio = Assert.Single(await EnviosAsync());
        Assert.True(envio.EsPrueba);
        Assert.Equal(EstadoEnvioCorreoComuna.Enviado, envio.Estado);
        Assert.Equal(EmailTemplate.Subject(), envio.Asunto);
    }

    [Fact]
    public async Task Con_el_envio_real_apagado_y_sin_casilla_no_sale_nada_y_se_registra_simulado()
    {
        var id = await SembrarAsync("MAIPU");

        var r = Assert.Single(await Servicio(real: false, casillaPrueba: null).EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));

        Assert.Empty(_mail.Enviados);
        Assert.Empty(_mail.Intentos);
        Assert.Equal(ResultadoEnvio.Enviado, r.Resultado);
        Assert.True(r.EsPrueba);
        Assert.True(r.Simulado);
        var envio = Assert.Single(await EnviosAsync());
        Assert.True(envio.EsPrueba);
        Assert.Equal(EstadoEnvioCorreoComuna.Enviado, envio.Estado);
        Assert.Contains("simulado", envio.Nota);
    }

    [Fact]
    public async Task Con_casilla_de_prueba_configurada_nada_sale_a_la_comuna_aunque_el_envio_real_este_encendido()
    {
        var id = await SembrarAsync("MAIPU");

        var r = Assert.Single(await Servicio(real: true, casillaPrueba: CasillaPrueba).EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));

        Assert.True(r.EsPrueba);
        Assert.Equal(CasillaPrueba, Assert.Single(_mail.Enviados).Para);
    }

    // Criterio 2: un correo por comuna, asunto fijo, cuerpo sin clase.
    [Fact]
    public async Task Tres_carpetas_de_dos_comunas_son_dos_envios_con_tres_enlaces_asunto_fijo_y_sin_clase()
    {
        var a = await SembrarAsync("MAIPU", "Zacarias", "Quimbaya");
        var b = await SembrarAsync("MAIPU", "Ludovico", "Parrales");
        var c = await SembrarAsync("RENCA", "Eufrasia", "Toledo");

        var res = await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [a, b, c], "ana");

        Assert.Equal(2, res.Count);
        Assert.All(res, x => Assert.Equal(ResultadoEnvio.Enviado, x.Resultado));
        Assert.Equal(2, _mail.Enviados.Count);
        Assert.All(_mail.Enviados, m => Assert.Equal(EmailTemplate.Subject(), m.Asunto));
        Assert.All(_mail.Enviados, m => Assert.DoesNotContain("Clase", m.Cuerpo));
        var maipu = _mail.Enviados.Single(m => m.Para.Contains("maipu"));
        Assert.Equal("uno@maipu.example.test; dos@maipu.example.test", maipu.Para);
        Assert.Contains("Zacarias Quimbaya".ToUpperInvariant(), maipu.Cuerpo);
        Assert.Contains("Ludovico Parrales".ToUpperInvariant(), maipu.Cuerpo);
        Assert.DoesNotContain("EUFRASIA", maipu.Cuerpo);
        var envios = await EnviosAsync();
        Assert.Equal(2, envios.Count);
        Assert.Equal(3, envios.Sum(e => e.Carpetas.Count));
        Assert.Equal(2, envios.Single(e => e.Comuna == "MAIPU").Carpetas.Count);
        Assert.All(envios, e => Assert.False(e.EsPrueba));
        Assert.All(envios, e => Assert.Equal(TipoEnvioCorreoComuna.Pedido, e.Tipo));
    }

    [Fact]
    public async Task Un_nombre_de_comuna_con_tipeo_se_agrupa_con_la_comuna_del_directorio()
    {
        var a = await SembrarAsync("MAIPU");
        var b = await SembrarAsync("maipú");

        var res = await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [a, b], "ana");

        Assert.Single(res);
        Assert.Single(_mail.Enviados);
    }

    // Criterio 3: si falla un correo los demás salen y se reintenta solo ese.
    [Fact]
    public async Task Si_el_correo_de_una_comuna_falla_las_otras_salen_y_el_reintento_solo_manda_la_fallida()
    {
        var a = await SembrarAsync("MAIPU");
        var b = await SembrarAsync("RENCA");
        _mail.FallaPara = para => para.Contains("renca") ? new EwsSendException("detalle interno licencias@renca.example.test") : null;

        var primera = await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [a, b], "ana");

        Assert.Equal(ResultadoEnvio.Enviado, primera.Single(x => x.Comuna == "MAIPU").Resultado);
        Assert.Equal(ResultadoEnvio.Fallido, primera.Single(x => x.Comuna == "RENCA").Resultado);
        var envios = await EnviosAsync();
        Assert.Equal(EstadoEnvioCorreoComuna.Enviado, envios.Single(e => e.Comuna == "MAIPU").Estado);
        var fallido = envios.Single(e => e.Comuna == "RENCA");
        Assert.Equal(EstadoEnvioCorreoComuna.Fallido, fallido.Estado);
        Assert.DoesNotContain("renca.example", fallido.Error);

        _mail.FallaPara = null;
        _mail.Enviados.Clear();
        var segunda = await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [a, b], "ana");

        Assert.Equal(ResultadoEnvio.YaEnviado, segunda.Single(x => x.Comuna == "MAIPU").Resultado);
        Assert.Equal(ResultadoEnvio.Enviado, segunda.Single(x => x.Comuna == "RENCA").Resultado);
        var reintento = Assert.Single(_mail.Enviados);
        Assert.Contains("renca", reintento.Para);
        var despues = await EnviosAsync();
        Assert.Equal(3, despues.Count);
        Assert.Single(despues, e => e.Comuna == "MAIPU");
        Assert.Contains(despues, e => e.Comuna == "RENCA" && e.Estado == EstadoEnvioCorreoComuna.Enviado);
    }

    // Criterio 4: doble clic concurrente sobre la misma comuna = un solo envío.
    [Fact]
    public async Task Dos_envios_simultaneos_de_la_misma_comuna_mandan_un_solo_correo()
    {
        var id = await SembrarAsync("MAIPU");
        var dentro = new TaskCompletionSource();
        var liberar = new TaskCompletionSource();
        _mail.Antes = async () =>
        {
            dentro.TrySetResult();
            await liberar.Task;
        };

        var primero = Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana");
        await dentro.Task;
        var segundo = Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana");
        await Task.Delay(150);
        liberar.SetResult();
        var resultados = (await Task.WhenAll(primero, segundo)).Select(x => Assert.Single(x)).ToList();

        Assert.Single(_mail.Intentos);
        Assert.Single(resultados, x => x.Resultado == ResultadoEnvio.Enviado);
        Assert.Single(resultados, x => x.Resultado == ResultadoEnvio.YaEnviado);
        Assert.Single(await EnviosAsync());
    }

    [Fact]
    public async Task Una_reserva_Enviando_de_otro_operador_bloquea_el_envio_y_una_vencida_no()
    {
        var id = await SembrarAsync("MAIPU");
        await using (var ctx = _db.NewContext())
        {
            ctx.EnviosCorreoComuna.Add(EnvioCorreoComuna.Reservar(
                TipoEnvioCorreoComuna.Pedido, "MAIPU", "uno@maipu.example.test", "asunto", "cuerpo", "otro", false, [id], _db.Clock.GetUtcNow().UtcDateTime));
            await ctx.SaveChangesAsync();
        }

        var bloqueado = Assert.Single(await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));
        Assert.Equal(ResultadoEnvio.YaEnviado, bloqueado.Resultado);
        Assert.Empty(_mail.Intentos);

        _db.AdvanceClock(TimeSpan.FromHours(1));
        var libre = Assert.Single(await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));
        Assert.Equal(ResultadoEnvio.Enviado, libre.Resultado);
        Assert.Contains(await EnviosAsync(), e => e.Estado == EstadoEnvioCorreoComuna.Fallido);
    }

    [Fact]
    public async Task Un_reenvio_manda_otra_vez_la_carpeta_ya_pedida()
    {
        var id = await SembrarAsync("MAIPU");
        await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana");

        var r = Assert.Single(await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Reenvio, [id], "ana"));

        Assert.Equal(ResultadoEnvio.Enviado, r.Resultado);
        Assert.Equal(2, _mail.Enviados.Count);
        Assert.Equal([TipoEnvioCorreoComuna.Pedido, TipoEnvioCorreoComuna.Reenvio], (await EnviosAsync()).Select(e => e.Tipo).OrderBy(t => t).ToList());
    }

    [Fact]
    public async Task Tras_un_envio_de_prueba_el_envio_real_de_la_misma_carpeta_si_sale()
    {
        var id = await SembrarAsync("MAIPU");
        await Servicio(real: false, casillaPrueba: CasillaPrueba).EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana");

        var real = Assert.Single(await Servicio(real: true).EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));

        Assert.Equal(ResultadoEnvio.Enviado, real.Resultado);
        Assert.Equal("uno@maipu.example.test; dos@maipu.example.test", _mail.Enviados.Last().Para);
        var repetido = Assert.Single(await Servicio(real: true).EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));
        Assert.Equal(ResultadoEnvio.YaEnviado, repetido.Resultado);
    }

    // Criterio 5: comuna sin correo.
    [Fact]
    public async Task Una_comuna_sin_correo_no_sale_y_tras_agregarlo_el_reintento_sale()
    {
        var id = await SembrarAsync("PEUMO");

        var r = Assert.Single(await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));

        Assert.Equal(ResultadoEnvio.SinCorreo, r.Resultado);
        Assert.Contains("sin correo", r.Mensaje);
        Assert.Empty(_mail.Intentos);
        Assert.Empty(await EnviosAsync());

        Assert.True(_directorio.AddOrUpdate("PEUMO", "licencias@peumo.example.test").Ok);
        var reintento = Assert.Single(await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));

        Assert.Equal(ResultadoEnvio.Enviado, reintento.Resultado);
        Assert.Equal("licencias@peumo.example.test", Assert.Single(_mail.Enviados).Para);
    }

    [Fact]
    public async Task Una_carpeta_sin_comuna_no_sale()
    {
        var id = await SembrarAsync(null, entrante: true);

        var r = Assert.Single(await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));

        Assert.Equal(ResultadoEnvio.SinComuna, r.Resultado);
        Assert.Empty(_mail.Intentos);
    }

    [Fact]
    public async Task Con_el_transporte_sin_configurar_no_sale_ni_se_registra_nada()
    {
        var id = await SembrarAsync("MAIPU");
        _mail.Configurado = false;

        var r = Assert.Single(await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana"));

        Assert.Equal(ResultadoEnvio.CorreoNoConfigurado, r.Resultado);
        Assert.Empty(await EnviosAsync());
    }

    [Fact]
    public async Task Una_carpeta_inexistente_lanza_NotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [Guid.NewGuid()], "ana"));
    }

    [Fact]
    public async Task Enviar_sin_carpetas_o_con_un_tipo_de_plantilla_lanza_DomainException()
    {
        await Assert.ThrowsAsync<DomainException>(() => Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [], "ana"));
        var id = await SembrarAsync("MAIPU");
        await Assert.ThrowsAsync<DomainException>(() => Servicio().EnviarAsync(TipoEnvioCorreoComuna.SinRegistro, [id], "ana"));
    }

    // Criterio 6: plantillas B y C editables y persistidas; el render reemplaza los marcadores.
    [Fact]
    public async Task Las_plantillas_traen_el_texto_aprobado_y_se_pueden_editar_y_persistir()
    {
        var b = await Servicio().ObtenerPlantillaAsync("SinRegistro");
        var c = await Servicio().ObtenerPlantillaAsync("Digital");

        Assert.Contains("no registra antecedentes en la Municipalidad de Valparaíso", b.Cuerpo);
        Assert.Contains("o tratarse de una primera licencia otorgada por su municipalidad, caso en que no procede un cambio de domicilio.", b.Cuerpo);
        Assert.Contains("{comuna_sgl}", b.Cuerpo);
        Assert.Contains("su última carpeta es de fecha {fecha}, posterior a julio de 2026. Desde esa fecha todas las carpetas se encuentran digitalizadas en SGL.", c.Cuerpo);
        Assert.Contains("Departamento de Licencias de Conducir", c.Cuerpo);

        await Servicio().GuardarPlantillaAsync("Digital", "Asunto editado", "Texto editado de {nombre}, RUT {rut}, fecha {fecha}.");

        var releida = await Servicio().ObtenerPlantillaAsync("Digital");
        Assert.Equal("Asunto editado", releida.Asunto);
        Assert.Equal("Texto editado de {nombre}, RUT {rut}, fecha {fecha}.", releida.Cuerpo);
        Assert.Equal(b, await Servicio().ObtenerPlantillaAsync("SinRegistro"));
        await Assert.ThrowsAsync<DomainException>(() => Servicio().GuardarPlantillaAsync("Digital", " ", "x"));
        await Assert.ThrowsAsync<DomainException>(() => Servicio().GuardarPlantillaAsync("Otra", "a", "b"));
    }

    [Fact]
    public async Task El_render_reemplaza_los_marcadores_y_la_linea_de_comuna_desaparece_si_viene_vacia()
    {
        var con = await Servicio().VistaPreviaAsync("SinRegistro", "ZACARIAS QUIMBAYA", "12.345.678-5", null, "Quilpué");
        var sin = await Servicio().VistaPreviaAsync("SinRegistro", "ZACARIAS QUIMBAYA", "12.345.678-5", null, "  ");

        Assert.Contains("la carpeta de ZACARIAS QUIMBAYA, RUT 12.345.678-5, informamos", con.Cuerpo);
        Assert.Contains("la persona figura registrada en la comuna de Quilpué.", con.Cuerpo);
        Assert.DoesNotContain("{", con.Cuerpo);
        Assert.DoesNotContain("registrada en la comuna", sin.Cuerpo);
        Assert.DoesNotContain("{", sin.Cuerpo);
        Assert.DoesNotContain("\n\n\n", sin.Cuerpo);
        Assert.EndsWith("Municipalidad de Valparaíso", sin.Cuerpo);

        var digital = await Servicio().VistaPreviaAsync("Digital", "N", "1-9", "15-08-2026", null);
        Assert.Contains("su última carpeta es de fecha 15-08-2026, posterior", digital.Cuerpo);
    }

    [Fact]
    public async Task Enviar_la_plantilla_sin_registro_manda_un_correo_a_la_comuna_que_pidio_y_lo_registra()
    {
        var id = await SembrarAsync("MAIPU", "Zacarias", "Quimbaya", entrante: true);

        var r = await Servicio().EnviarPlantillaAsync(new SolicitudCorreoPlantilla(TipoEnvioCorreoComuna.SinRegistro, id, "ana", ComunaSgl: "Quilpué"));

        Assert.Equal(ResultadoEnvio.Enviado, r.Resultado);
        var m = Assert.Single(_mail.Enviados);
        Assert.Equal("uno@maipu.example.test; dos@maipu.example.test", m.Para);
        Assert.Contains("la carpeta de ZACARIAS QUIMBAYA, RUT 12.345.678-5", m.Cuerpo);
        Assert.Contains("comuna de Quilpué.", m.Cuerpo);
        var envio = Assert.Single(await EnviosAsync());
        Assert.Equal(TipoEnvioCorreoComuna.SinRegistro, envio.Tipo);
        Assert.Equal(id, Assert.Single(envio.Carpetas).CarpetaId);
    }

    [Fact]
    public async Task Enviar_la_plantilla_digital_usa_la_fecha_de_la_ultima_carpeta_y_exige_que_exista()
    {
        var id = await SembrarAsync("RENCA", entrante: true);
        await Assert.ThrowsAsync<DomainException>(() =>
            Servicio().EnviarPlantillaAsync(new SolicitudCorreoPlantilla(TipoEnvioCorreoComuna.Digital, id, "ana")));
        Assert.Empty(_mail.Intentos);

        var r = await Servicio().EnviarPlantillaAsync(new SolicitudCorreoPlantilla(TipoEnvioCorreoComuna.Digital, id, "ana", Fecha: "15-08-2026"));

        Assert.Equal(ResultadoEnvio.Enviado, r.Resultado);
        Assert.Contains("su última carpeta es de fecha 15-08-2026", Assert.Single(_mail.Enviados).Cuerpo);
    }

    [Fact]
    public async Task La_confirmacion_de_subida_usa_su_plantilla_editable_y_no_se_repite_para_la_misma_carpeta()
    {
        // E13: la confirmación tiene un texto formal por defecto (plantilla «ConfirmacionSubida»); un texto explícito lo reemplaza.
        var sinTexto = await SembrarAsync("RENCA", entrante: true);
        var porDefecto = await Servicio().EnviarPlantillaAsync(new SolicitudCorreoPlantilla(TipoEnvioCorreoComuna.ConfirmacionSubida, sinTexto, "ana"));
        Assert.Equal(ResultadoEnvio.Enviado, porDefecto.Resultado);
        Assert.Contains("la carpeta fue subida a la plataforma", Assert.Single(_mail.Enviados).Cuerpo);
        _mail.Enviados.Clear();

        var id = await SembrarAsync("RENCA", entrante: true);
        var solicitud = new SolicitudCorreoPlantilla(TipoEnvioCorreoComuna.ConfirmacionSubida, id, "ana", Asunto: "Carpeta subida", Cuerpo: "Se subió la carpeta.");
        var r = await Servicio().EnviarPlantillaAsync(solicitud);
        var otra = await Servicio().EnviarPlantillaAsync(solicitud);

        Assert.Equal(ResultadoEnvio.Enviado, r.Resultado);
        Assert.Equal(ResultadoEnvio.YaEnviado, otra.Resultado);
        Assert.Equal("Carpeta subida", Assert.Single(_mail.Enviados).Asunto);
    }

    // Interruptor.
    [Fact]
    public async Task El_envio_real_nace_apagado_se_guarda_en_la_base_y_manda_sobre_el_valor_de_appsettings()
    {
        var apagado = await Servicio(real: false).ObtenerModoAsync();
        Assert.False(apagado.EnvioReal);
        Assert.True(apagado.EsPrueba);

        await Servicio(real: false).EstablecerEnvioRealAsync(true);

        Assert.True((await Servicio(real: false).ObtenerModoAsync()).EnvioReal);
        Assert.False((await Servicio(real: false).ObtenerModoAsync()).EsPrueba);
        var porDefecto = await new CorreoComunasService(
            _db.NewContext(), _mail, _directorio, new AppOptions(), new CambioDomicilioOptions(), new UnidadDeTrabajoGate(), _db.Clock, _log)
            .ObtenerModoAsync();
        Assert.True(porDefecto.EnvioReal);

        await Servicio().EstablecerEnvioRealAsync(false);
        Assert.False((await Servicio(real: true).ObtenerModoAsync()).EnvioReal);
        Assert.True((await Servicio(real: true, casillaPrueba: CasillaPrueba).ObtenerModoAsync()).EsPrueba);
    }

    [Fact]
    public void Sin_configurar_el_envio_real_esta_apagado_y_los_plazos_traen_su_valor()
    {
        var o = new CambioDomicilioOptions();

        Assert.False(o.EnvioReal);
        Assert.Equal(15, o.PlazoSalienteDias);
        Assert.Equal(12, o.PlazoEntranteDias);
        Assert.Equal(5120, o.PantallazoMaxKB);
    }

    private static CambioDomicilioOptions ResolverOpciones(Dictionary<string, string?> valores)
    {
        valores["ConnectionStrings:Sgl"] = "Data Source=:memory:";
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var servicios = new ServiceCollection().AddSglInfrastructure(config);
        Assert.Contains(servicios, d => d.ServiceType == typeof(ICorreoComunas) && d.ImplementationType == typeof(CorreoComunasService));
        using var proveedor = servicios.BuildServiceProvider();
        return proveedor.GetRequiredService<CambioDomicilioOptions>();
    }

    [Fact]
    public void La_configuracion_vacia_deja_el_envio_real_apagado_y_los_plazos_por_defecto()
    {
        var o = ResolverOpciones([]);

        Assert.False(o.EnvioReal);
        Assert.Equal(15, o.PlazoSalienteDias);
        Assert.Equal(12, o.PlazoEntranteDias);
        Assert.Equal("../../datos/pantallazos", o.CarpetaPantallazos);
        Assert.Equal(5120, o.PantallazoMaxKB);
    }

    [Fact]
    public void La_seccion_CambioDomicilio_se_lee_y_un_valor_invalido_falla_al_arrancar()
    {
        var o = ResolverOpciones(new()
        {
            ["CambioDomicilio:EnvioReal"] = "true",
            ["CambioDomicilio:PlazoSalienteDias"] = "10",
            ["CambioDomicilio:PlazoEntranteDias"] = "8",
            ["CambioDomicilio:CarpetaPantallazos"] = "otra/carpeta",
            ["CambioDomicilio:PantallazoMaxKB"] = "100",
        });

        Assert.True(o.EnvioReal);
        Assert.Equal(10, o.PlazoSalienteDias);
        Assert.Equal(8, o.PlazoEntranteDias);
        Assert.Equal("otra/carpeta", o.CarpetaPantallazos);
        Assert.Equal(100, o.PantallazoMaxKB);
        Assert.Throws<InvalidOperationException>(() => ResolverOpciones(new() { ["CambioDomicilio:PlazoSalienteDias"] = "cero" }));
        Assert.Throws<InvalidOperationException>(() => ResolverOpciones(new() { ["CambioDomicilio:PantallazoMaxKB"] = "0" }));
    }

    // Criterio 7: ningún log lleva RUT, nombre ni cuerpo.
    [Fact]
    public async Task Los_logs_no_contienen_rut_nombre_ni_cuerpo_ni_mensajes_de_la_excepcion()
    {
        var a = await SembrarAsync("MAIPU", "Zacarias", "Quimbaya");
        var b = await SembrarAsync("RENCA", "Eufrasia", "Toledo");
        var c = await SembrarAsync("PEUMO", "Ludovico", "Parrales");
        var d = await SembrarAsync("MAIPU", "Casimira", "Bustos", entrante: true);
        _mail.FallaPara = para => para.Contains("renca") ? new EwsSendException("secreto 18785387-7 EUFRASIA TOLEDO") : null;

        await Servicio().EnviarAsync(TipoEnvioCorreoComuna.Pedido, [a, b, c], "ana");
        await Servicio(real: false).EnviarAsync(TipoEnvioCorreoComuna.Pedido, [a], "ana");
        await Servicio().EnviarPlantillaAsync(new SolicitudCorreoPlantilla(TipoEnvioCorreoComuna.SinRegistro, d, "ana", ComunaSgl: "Quilpué"));

        Assert.NotEmpty(_log.Entradas);
        var todo = string.Join("\n", _log.Entradas.Select(e => e.Texto + " " + e.Excepcion));
        foreach (var prohibido in new[]
                 {
                     "12345678", "12.345.678", "18785387", "18.785.387", "18586259", "ZACARIAS", "Zacarias", "QUIMBAYA", "Quimbaya",
                     "EUFRASIA", "Eufrasia", "TOLEDO", "Ludovico", "Casimira", "Estimados", "Junto con saludar", "secreto",
                     "example.test",
                 })
        {
            Assert.DoesNotContain(prohibido, todo);
        }
    }

    // ---- Fakes ----

    private sealed record Mensaje(string Para, string Asunto, string Cuerpo);

    private sealed class MailFalso : IMailSender
    {
        private readonly object _candado = new();

        public bool Configurado { get; set; } = true;

        public Func<string, Exception?>? FallaPara { get; set; }

        public Func<Task>? Antes { get; set; }

        /// <summary>Every call, failed or not.</summary>
        public List<string> Intentos { get; } = [];

        public List<Mensaje> Enviados { get; } = [];

        public bool IsConfigured => Configurado;

        public async Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
        {
            lock (_candado) { Intentos.Add(toAddress); }
            if (Antes is not null)
            {
                await Antes();
            }
            if (FallaPara?.Invoke(toAddress) is { } falla)
            {
                throw falla;
            }
            lock (_candado) { Enviados.Add(new Mensaje(toAddress, subject, body)); }
        }
    }

    private sealed record Entrada(LogLevel Nivel, string Texto, Exception? Excepcion);

    private sealed class LogCapturado : ILogger<CorreoComunasService>
    {
        public List<Entrada> Entradas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entradas) { Entradas.Add(new Entrada(logLevel, formatter(state, exception), exception)); }
        }
    }
}
