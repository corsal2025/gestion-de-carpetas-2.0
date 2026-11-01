using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Ews;
using Sgl.Application.Carpetas;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;
using Sgl.Infrastructure.CambioDomicilio;
using Sgl.Infrastructure.Persistence;
using Sgl.Web.Services;

namespace Sgl.Application.Tests;

/// <summary>E10: «Pedimos carpetas · Por pedir» (modo revisión, corregir, vista previa por comuna y envío masivo).</summary>
public sealed class PedimosCarpetasTests : IDisposable
{
    private const string CasillaPrueba = "buzon.prueba@example.test";
    private static readonly DateTime Antes = new(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sgl-pedimos-{Guid.NewGuid():N}");
    private readonly ComunaDirectory _directorio;
    private readonly MailFalso _mail = new();
    private int _rut = 11111111;

    public PedimosCarpetasTests()
    {
        Directory.CreateDirectory(_dir);
        var csv = Path.Combine(_dir, "comunas.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Comuna,ContactEmail,Domain",
            "\"MAIPU\",\"licencias@maipu.example.test\",\"maipu.example.test\"",
            "\"RENCA\",\"licencias@renca.example.test\",\"renca.example.test\""));
        _directorio = new ComunaDirectory(csv);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* archivo aún tomado por el proceso */ }
    }

    private sealed record Circuito(PedidosService Pedidos, CarpetaService Carpetas, CorreoComunasService Correo);

    /// <summary>One context and one gate for everything, like a scoped Blazor circuit.</summary>
    private Circuito Nuevo(bool real = false, string? casilla = CasillaPrueba)
    {
        var ctx = _db.NewContext();
        var gate = new UnidadDeTrabajoGate();
        var correo = new CorreoComunasService(
            ctx, _mail, _directorio, new AppOptions { TestModeEmail = casilla }, new CambioDomicilioOptions { EnvioReal = real },
            gate, _db.Clock, NullLogger<CorreoComunasService>.Instance);
        var pedidos = new PedidosService(
            new CarpetaRepository(ctx), new PedidosRepository(ctx), new DirectorioComunasAdapter(_directorio), correo, _db.Clock, gate);
        return new Circuito(pedidos, _db.NewServiceCompartido(ctx, gate), correo);
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

    private async Task<Guid> SembrarSalienteAsync(string? comuna, string nombre = "Persona", string apellido = "Ficticia", DateTime? clasificadaEn = null)
    {
        await using var ctx = _db.NewContext();
        var c = Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse(RutValido(_rut++)), nombre, apellido), Sede.AvArgentina, new DateOnly(2026, 5, 1),
            new TimeOnly(9, 0), TramiteAgenda.Renovacion, "setup", Antes);
        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, comuna, "setup", clasificadaEn ?? Antes);
        c.ConfirmarReclasificacion("setup", clasificadaEn ?? Antes);
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<Guid> SembrarEntranteAsync(string comuna)
    {
        await using var ctx = _db.NewContext();
        var c = Carpeta.RegistrarDesdeCorreo(
            new Ciudadano(Rut.Parse(RutValido(_rut++)), "Entrante", "Ficticia"), Sede.AvArgentina, new DateOnly(2026, 5, 1), comuna, false, "Sistema · correo", Antes);
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<CarpetaDetalleDto> DetalleAsync(Guid id) => await _db.NewService().ObtenerAsync(id);

    // Criterio 1: solo salientes en cambio de domicilio, nunca entrantes.
    [Fact]
    public async Task Por_pedir_lista_solo_salientes_en_cambio_de_domicilio_y_marca_las_de_dias_anteriores()
    {
        var hoy = _db.Clock.GetUtcNow().UtcDateTime;
        var vieja = await SembrarSalienteAsync("MAIPU", "Vieja", "Antes", Antes);
        var nueva = await SembrarSalienteAsync("RENCA", "Nueva", "Hoy", hoy);
        var entrante = await SembrarEntranteAsync("MAIPU");
        var solicitada = await SembrarSalienteAsync("MAIPU");
        await using (var ctx = _db.NewContext())
        {
            (await ctx.Carpetas.FindAsync(solicitada))!.CambiarEstado(EstadoCarpeta.CambioDomicilioSolicitado, "setup", Antes);
            await ctx.SaveChangesAsync();
        }
        var anulada = await SembrarSalienteAsync("MAIPU");
        await Nuevo().Carpetas.AnularAsync(anulada, "Duplicada", "ana");

        var lista = await Nuevo().Pedidos.ListarPorPedirAsync();

        Assert.Equal([vieja, nueva], lista.Select(f => f.Id));
        Assert.DoesNotContain(lista, f => f.Id == entrante);
        Assert.True(lista[0].DeDiaAnterior);
        Assert.False(lista[1].DeDiaAnterior);
        Assert.All(lista, f => Assert.False(f.Revisada));
        Assert.Equal(2, await Nuevo().Pedidos.ContarPorPedirAsync());
    }

    // Criterio 2: «Ya subida» -> CambioDomSubidoConaset, sin envío, deshacible.
    [Fact]
    public async Task Ya_subida_pasa_a_Cambio_dom_subido_a_Conaset_sin_enviar_y_se_puede_deshacer()
    {
        var id = await SembrarSalienteAsync("MAIPU");
        var circuito = Nuevo();

        var cambio = await circuito.Pedidos.YaSubidaAsync(id, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, cambio.EstadoNuevo);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, (await DetalleAsync(id)).Estado);
        Assert.Empty(_mail.Intentos);
        Assert.Empty(await circuito.Pedidos.ListarPorPedirAsync());

        await circuito.Pedidos.DeshacerAsync(id, cambio, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(id)).Estado);
        Assert.Equal([id], (await Nuevo().Pedidos.ListarPorPedirAsync()).Select(f => f.Id));
    }

    [Fact]
    public async Task Ya_subida_rechaza_una_entrante_y_no_la_cambia()
    {
        var entrante = await SembrarEntranteAsync("MAIPU");

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Pedidos.YaSubidaAsync(entrante, "ana"));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(entrante)).Estado);
    }

    [Fact]
    public async Task Pendiente_marca_la_carpeta_como_revisada_con_su_linea_en_el_historial_y_sigue_por_pedir()
    {
        var id = await SembrarSalienteAsync("MAIPU");

        await Nuevo().Pedidos.PendienteAsync(id, "ana");

        var fila = Assert.Single(await Nuevo().Pedidos.ListarPorPedirAsync());
        Assert.True(fila.Revisada);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(id)).Estado);
        Assert.Contains((await DetalleAsync(id)).Historial, h => h.Campo == Carpeta.CampoRevision && h.Usuario == "ana");
    }

    // Criterio 3: enviar 4 carpetas de 2 comunas = 2 envíos, las 4 en Solicitado con su historial.
    [Fact]
    public async Task Enviar_cuatro_carpetas_de_dos_comunas_son_dos_envios_y_las_cuatro_pasan_a_Solicitado_con_historial()
    {
        var ids = new[]
        {
            await SembrarSalienteAsync("MAIPU"), await SembrarSalienteAsync("MAIPU"),
            await SembrarSalienteAsync("RENCA"), await SembrarSalienteAsync("RENCA"),
        };

        var r = await Nuevo().Pedidos.EnviarAsync(ids, "ana");

        Assert.Equal(2, r.Envios.Count);
        Assert.All(r.Envios, e => Assert.Equal(ResultadoEnvio.Enviado, e.Resultado));
        Assert.Equal(4, r.CarpetasPedidas);
        Assert.Equal(2, _mail.Enviados.Count);
        foreach (var (id, comuna) in ids.Zip(new[] { "MAIPU", "MAIPU", "RENCA", "RENCA" }))
        {
            var d = await DetalleAsync(id);
            Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, d.Estado);
            Assert.Contains(d.Historial, h => h.Nuevo == $"Pedido por correo · {comuna}" && h.Usuario == "ana");
        }
        Assert.Empty(await Nuevo().Pedidos.ListarPorPedirAsync());
    }

    // Criterio 4: un envío fallido deja SOLO sus carpetas en Por pedir con la marca; se reintenta solo ese.
    [Fact]
    public async Task Un_envio_fallido_deja_solo_sus_carpetas_por_pedir_con_la_marca_y_se_reintenta_solo_ese()
    {
        var maipu = await SembrarSalienteAsync("MAIPU");
        var renca = await SembrarSalienteAsync("RENCA");
        var circuito = Nuevo();
        _mail.FallaCuando = _ => _mail.Intentos.Count == 2;

        var r = await circuito.Pedidos.EnviarAsync([maipu, renca], "ana");

        Assert.Equal(1, r.CarpetasPedidas);
        Assert.Contains(r.Envios, e => e.Resultado == ResultadoEnvio.Fallido);
        var porPedir = Assert.Single(await Nuevo().Pedidos.ListarPorPedirAsync());
        Assert.Equal(renca, porPedir.Id);
        Assert.True(porPedir.FalloEnvio);
        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await DetalleAsync(maipu)).Estado);

        _mail.FallaCuando = null;
        var antes = _mail.Enviados.Count;
        var reintento = await Nuevo().Pedidos.EnviarAsync([renca], "ana");

        Assert.Equal(1, reintento.CarpetasPedidas);
        Assert.Equal(antes + 1, _mail.Enviados.Count);
        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await DetalleAsync(renca)).Estado);
        Assert.Empty(await Nuevo().Pedidos.ListarPorPedirAsync());
    }

    // Criterio 5: corregir la clasificación la saca del módulo con motivo; corregir la comuna cambia el grupo.
    [Fact]
    public async Task Corregir_a_primera_licencia_la_saca_del_modulo_y_deja_el_motivo_y_se_puede_deshacer()
    {
        var id = await SembrarSalienteAsync("MAIPU");
        var circuito = Nuevo();

        var cambio = await circuito.Pedidos.CorregirClasificacionAsync(id, EstadoCarpeta.PrimeraLicencia, "Era primera licencia", null, "ana");

        var d = await DetalleAsync(id);
        Assert.Equal(EstadoCarpeta.PrimeraLicencia, d.Estado);
        Assert.Contains(d.Historial, h => h.Campo == Carpeta.CampoCorreccionClasificacion && h.Nuevo == "Era primera licencia");
        Assert.Empty(await circuito.Pedidos.ListarPorPedirAsync());

        await circuito.Pedidos.DeshacerAsync(id, cambio, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(id)).Estado);
        Assert.Equal([id], (await Nuevo().Pedidos.ListarPorPedirAsync()).Select(f => f.Id));
    }

    [Fact]
    public async Task Corregir_sin_motivo_se_rechaza_y_la_carpeta_no_se_mueve()
    {
        var id = await SembrarSalienteAsync("MAIPU");

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Pedidos.CorregirClasificacionAsync(id, EstadoCarpeta.CanjeLicExtranjera, "  ", null, "ana"));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(id)).Estado);
    }

    [Fact]
    public async Task Corregir_la_comuna_cambia_el_grupo_de_la_vista_previa()
    {
        var a = await SembrarSalienteAsync("MAIPU", "Ana", "Uno");
        var b = await SembrarSalienteAsync("MAIPU", "Beto", "Dos");
        var circuito = Nuevo();
        Assert.Equal(["MAIPU"], (await circuito.Pedidos.VistaPreviaAsync([a, b])).Select(g => g.Comuna));

        await circuito.Carpetas.GuardarComunaAsync(b, "Renca", "ana");

        var grupos = await circuito.Pedidos.VistaPreviaAsync([a, b]);
        Assert.Equal(["MAIPU", "RENCA"], grupos.Select(g => g.Comuna));
        Assert.Equal([b], grupos.Single(g => g.Comuna == "RENCA").Personas.Select(p => p.Id));
        Assert.Equal(["licencias@renca.example.test"], grupos.Single(g => g.Comuna == "RENCA").Correos);
    }

    // Vista previa: comunas sin correo marcadas y fuera del envío; completar el correo en el momento las habilita.
    [Fact]
    public async Task Una_comuna_sin_correo_queda_marcada_fuera_del_envio_y_sale_tras_completar_su_correo()
    {
        var conCorreo = await SembrarSalienteAsync("MAIPU");
        var sinCorreo = await SembrarSalienteAsync("Valdivia");
        var circuito = Nuevo();

        var grupos = await circuito.Pedidos.VistaPreviaAsync([conCorreo, sinCorreo]);
        Assert.True(grupos.Single(g => g.Comuna == "MAIPU").Enviable);
        var valdivia = grupos.Single(g => g.SinCorreo);
        Assert.Equal([sinCorreo], valdivia.Personas.Select(p => p.Id));

        var r = await circuito.Pedidos.EnviarAsync([conCorreo, sinCorreo], "ana");

        Assert.Equal(1, r.CarpetasPedidas);
        Assert.Contains(r.Envios, e => e.Resultado == ResultadoEnvio.SinCorreo);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(sinCorreo)).Estado);

        var alta = new DirectorioComunasAdapter(_directorio).AgregarCorreo(valdivia.Comuna, "licencias@valdivia.example.test");
        Assert.True(alta.Ok);

        Assert.True((await circuito.Pedidos.VistaPreviaAsync([sinCorreo])).Single().Enviable);
        var reintento = await circuito.Pedidos.EnviarAsync([sinCorreo], "ana");
        Assert.Equal(1, reintento.CarpetasPedidas);
        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await DetalleAsync(sinCorreo)).Estado);
    }

    // Criterio 6: con el envío real apagado nada sale a la comuna.
    [Fact]
    public async Task Con_el_envio_real_apagado_nada_sale_a_la_comuna_solo_a_la_direccion_de_prueba()
    {
        var a = await SembrarSalienteAsync("MAIPU");
        var b = await SembrarSalienteAsync("RENCA");

        var r = await Nuevo(real: false).Pedidos.EnviarAsync([a, b], "ana");

        Assert.Equal(2, r.CarpetasPedidas);
        Assert.All(r.Envios, e => Assert.True(e.EsPrueba));
        Assert.Equal(2, _mail.Enviados.Count);
        Assert.All(_mail.Enviados, m => Assert.Equal(CasillaPrueba, m.Para));
        Assert.All(_mail.Intentos, para => Assert.DoesNotContain("maipu", para));
        Assert.All(_mail.Intentos, para => Assert.DoesNotContain("renca", para));
    }

    [Fact]
    public async Task Enviar_rechaza_carpetas_que_no_estan_por_pedir_y_no_envia_nada()
    {
        var saliente = await SembrarSalienteAsync("MAIPU");
        var entrante = await SembrarEntranteAsync("MAIPU");

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Pedidos.EnviarAsync([saliente, entrante], "ana"));

        Assert.Empty(_mail.Intentos);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(saliente)).Estado);
    }

    [Fact]
    public async Task Una_carpeta_con_su_correo_ya_enviado_pero_sin_mover_pasa_a_Solicitado_sin_repetir_el_correo()
    {
        var id = await SembrarSalienteAsync("MAIPU");
        var circuito = Nuevo();
        await circuito.Correo.EnviarAsync(TipoEnvioCorreoComuna.Pedido, [id], "ana");
        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(id)).Estado);
        var enviados = _mail.Enviados.Count;

        var r = await circuito.Pedidos.EnviarAsync([id], "ana");

        Assert.Equal(enviados, _mail.Enviados.Count);
        Assert.Equal(1, r.CarpetasPedidas);
        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await DetalleAsync(id)).Estado);
    }

    [Fact]
    public void El_directorio_resuelve_el_nombre_y_los_correos_de_una_comuna_con_tildes_y_minusculas()
    {
        var adaptador = new DirectorioComunasAdapter(_directorio);

        Assert.Equal("MAIPU", adaptador.NombreDe("Maipú"));
        Assert.Equal(["licencias@maipu.example.test"], adaptador.CorreosDe("Maipú"));
        Assert.Empty(adaptador.CorreosDe("Valdivia"));
        Assert.Equal(string.Empty, adaptador.NombreDe("  "));
        Assert.False(adaptador.AgregarCorreo("Valdivia", "no-es-un-correo").Ok);
    }

    // Menú: grupo «Cambios de domicilio» con 3 submódulos y sin el enlace a /peticiones.
    [Fact]
    public void El_menu_tiene_el_grupo_Cambios_de_domicilio_con_sus_cuatro_submodulos_y_sin_peticiones()
    {
        var grupo = ModulosCatalogo.CambioDomicilio;
        Assert.Equal("Cambios de domicilio", grupo.Nombre);

        var hrefs = ModulosCatalogo.Todos.Where(m => m.Grupo == grupo && m.Bandeja is null).Select(m => m.Href).ToList();

        Assert.Equal(["cambio-domicilio/reclasificacion", "cambio-domicilio/nos-piden", "cambio-domicilio/pedimos", "cambio-domicilio/comunas"], hrefs);
        Assert.DoesNotContain(ModulosCatalogo.Todos, m => m.Href == "peticiones");
    }

    // La lupa ya no envía correo: el listado no usa el motor antiguo de peticiones.
    [FactConRepositorio]
    public void El_listado_no_usa_PeticionSender_ni_PeticionRepo()
    {
        var raiz = RaizRepositorio.Buscar()!;

        var texto = File.ReadAllText(Path.Combine(raiz, "src", "Sgl.Web", "Components", "Pages", "Carpetas", "Listado.razor"));

        Assert.DoesNotContain("PeticionSender", texto);
        Assert.DoesNotContain("PeticionRepo", texto);
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
