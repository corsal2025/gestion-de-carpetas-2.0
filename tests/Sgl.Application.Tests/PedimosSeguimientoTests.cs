using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Ews;
using Sgl.Application.Carpetas;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;
using Sgl.Infrastructure.CambioDomicilio;
using Sgl.Infrastructure.Persistence;
using Sgl.Web.Services;

namespace Sgl.Application.Tests;

/// <summary>E11: pantallazos pendientes, seguimiento por plazo, volver a pedir y subir prueba.</summary>
public sealed class PedimosSeguimientoTests : IDisposable
{
    private const string CasillaPrueba = "buzon.prueba@example.test";
    private static readonly DateTime Antes = new(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sgl-seguimiento-{Guid.NewGuid():N}");
    private readonly ComunaDirectory _directorio;
    private readonly MailFalso _mail = new();
    private int _rut = 12121212;

    public PedimosSeguimientoTests()
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

    private sealed record Circuito(PedidosService Pedidos, SeguimientoService Seguimiento, CarpetaService Carpetas);

    /// <summary>One context and one gate for everything, like a scoped Blazor circuit.</summary>
    private Circuito Nuevo(bool real = false, string? casilla = CasillaPrueba)
    {
        var ctx = _db.NewContext();
        var gate = new UnidadDeTrabajoGate();
        var opciones = new CambioDomicilioOptions { EnvioReal = real, PantallazoMaxKB = 1, CarpetaPantallazos = Path.Combine(_dir, "almacen") };
        var correo = new CorreoComunasService(
            ctx, _mail, _directorio, new AppOptions { TestModeEmail = casilla }, opciones, gate, _db.Clock,
            NullLogger<CorreoComunasService>.Instance);
        var pedidos = new PedidosService(
            new CarpetaRepository(ctx), new PedidosRepository(ctx), new DirectorioComunasAdapter(_directorio), correo, _db.Clock, gate);
        var seguimiento = new SeguimientoService(
            new CarpetaRepository(ctx), new SeguimientoRepository(ctx), new AlmacenPantallazos(opciones, _dir),
            correo, opciones, _db.Clock, gate);
        return new Circuito(pedidos, seguimiento, _db.NewServiceCompartido(ctx, gate));
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

    /// <summary>Citation date whose deadline (15 business days later) falls on <paramref name="vence"/>.</summary>
    private static DateOnly CitaParaVencer(DateOnly vence)
    {
        for (var cita = vence.AddDays(-30); cita < vence; cita = cita.AddDays(1))
        {
            if (cita.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && DeadlineCalculator.AddBusinessDays(cita, 15) == vence)
            {
                return cita;
            }
        }
        throw new InvalidOperationException("Sin cita para ese vencimiento.");
    }

    private DateOnly Hoy => HoraChile.Fecha(_db.Clock.GetUtcNow().UtcDateTime);

    private async Task<Guid> SembrarSalienteAsync(string? comuna, DateOnly? cita = null, string nombre = "Persona", string apellido = "Ficticia")
    {
        await using var ctx = _db.NewContext();
        var c = Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse(RutValido(_rut++)), nombre, apellido), Sede.AvArgentina, cita ?? new DateOnly(2026, 5, 1),
            new TimeOnly(9, 0), TramiteAgenda.Renovacion, "setup", Antes);
        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, comuna, "setup", Antes);
        c.ConfirmarReclasificacion("setup", Antes);
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    /// <summary>A saliente already mailed (moved to «Solicitado» through the real send).</summary>
    private async Task<Guid> SembrarSolicitadaAsync(Circuito c, string comuna, DateOnly? cita = null, string apellido = "Ficticia")
    {
        var id = await SembrarSalienteAsync(comuna, cita, apellido: apellido);
        var r = await c.Pedidos.EnviarAsync([id], "ana");
        Assert.Equal(1, r.CarpetasPedidas);
        return id;
    }

    private async Task<CarpetaDetalleDto> DetalleAsync(Guid id) => await _db.NewService().ObtenerAsync(id);

    private async Task<List<EnvioCorreoComuna>> EnviosAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.EnviosCorreoComuna.AsNoTracking().Include(e => e.Carpetas).ToListAsync();
    }

    private static MemoryStream Png(int relleno = 64) =>
        new([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[relleno]]);

    private static MemoryStream Jpeg(int relleno = 64) => new([0xFF, 0xD8, 0xFF, 0xE0, .. new byte[relleno]]);

    private async Task<Guid> EnvioDeAsync(Guid carpetaId) =>
        (await EnviosAsync()).Where(e => e.Carpetas.Any(l => l.CarpetaId == carpetaId)).OrderBy(e => e.EnviadoEn).Last().Id;

    // Criterio 1: cita viernes 2026-10-30 -> vence 2026-11-20; el reenvío no lo mueve; orden ascendente por vencimiento.
    [Fact]
    public async Task Cita_del_viernes_30_de_octubre_vence_el_20_de_noviembre_el_reenvio_no_lo_cambia_y_se_ordena_por_vencimiento()
    {
        var c = Nuevo();
        var tarde = await SembrarSolicitadaAsync(c, "MAIPU", new DateOnly(2026, 10, 30), "Tarde");
        var pronto = await SembrarSolicitadaAsync(c, "RENCA", new DateOnly(2026, 10, 2), "Pronto");

        var filas = await c.Seguimiento.ListarAsync();

        Assert.Equal([pronto, tarde], filas.Select(f => f.Id));
        Assert.Equal(new DateOnly(2026, 11, 20), filas.Single(f => f.Id == tarde).Vence);
        Assert.Equal(filas.OrderBy(f => f.Vence).Select(f => f.Id), filas.Select(f => f.Id));

        var reenvio = await c.Seguimiento.VolverAPedirAsync(tarde, "ana");

        Assert.Equal(ResultadoEnvio.Enviado, reenvio.Resultado);
        var despues = (await Nuevo().Seguimiento.ListarAsync()).Single(f => f.Id == tarde);
        Assert.Equal(new DateOnly(2026, 11, 20), despues.Vence);
        Assert.Equal(2, despues.Envios);
    }

    // Criterio 7 (parte servicio): colores verde / amarillo (<= 3 días) / rojo (vencida) y filtros.
    [Fact]
    public async Task Los_colores_del_plazo_y_los_filtros_vencen_hoy_y_vencidas_funcionan()
    {
        var c = Nuevo();
        var verde = await SembrarSolicitadaAsync(c, "MAIPU", CitaParaVencer(Hoy.AddDays(14)), "Verde");
        var cuatro = await SembrarSolicitadaAsync(c, "MAIPU", CitaParaVencer(Hoy.AddDays(4)), "Cuatro");
        var tres = await SembrarSolicitadaAsync(c, "MAIPU", CitaParaVencer(Hoy.AddDays(3)), "Tres");
        var hoy = await SembrarSolicitadaAsync(c, "MAIPU", CitaParaVencer(Hoy), "Hoy");
        var pasada = await SembrarSolicitadaAsync(c, "MAIPU", CitaParaVencer(Hoy.AddDays(-4)), "Pasada");

        var todas = (await c.Seguimiento.ListarAsync()).ToDictionary(f => f.Id);

        Assert.Equal(PlazoColor.Verde, todas[verde].Plazo);
        Assert.Equal(PlazoColor.Verde, todas[cuatro].Plazo);
        Assert.Equal(PlazoColor.Amarillo, todas[tres].Plazo);
        Assert.Equal(PlazoColor.Amarillo, todas[hoy].Plazo);
        Assert.Equal(0, todas[hoy].DiasRestantes);
        Assert.Equal(PlazoColor.Rojo, todas[pasada].Plazo);
        Assert.True(todas[pasada].Vencida);
        Assert.Equal([hoy], (await c.Seguimiento.ListarAsync(FiltroSeguimiento.VencenHoy)).Select(f => f.Id));
        Assert.Equal([pasada], (await c.Seguimiento.ListarAsync(FiltroSeguimiento.Vencidas)).Select(f => f.Id));
        Assert.Equal(5, (await c.Seguimiento.ListarAsync(FiltroSeguimiento.SoloPrueba)).Count);
    }

    [Fact]
    public async Task El_seguimiento_solo_lista_salientes_solicitadas_y_con_el_RUT_formateado()
    {
        var c = Nuevo();
        var solicitada = await SembrarSolicitadaAsync(c, "MAIPU");
        await SembrarSalienteAsync("MAIPU");

        var fila = Assert.Single(await c.Seguimiento.ListarAsync());

        Assert.Equal(solicitada, fila.Id);
        Assert.Contains('-', fila.Rut);
        Assert.Equal("MAIPÚ", fila.Comuna);
    }

    // Criterio 2: un pantallazo pegado una vez queda en todas las carpetas del envío y no bloquea el seguimiento.
    [Fact]
    public async Task Un_pantallazo_pegado_una_vez_queda_asociado_a_todas_las_carpetas_del_envio_y_no_bloquea_el_seguimiento()
    {
        var c = Nuevo();
        var a = await SembrarSalienteAsync("MAIPU", apellido: "Uno");
        var b = await SembrarSalienteAsync("MAIPU", apellido: "Dos");
        var d = await SembrarSalienteAsync("MAIPU", apellido: "Tres");
        var otra = await SembrarSalienteAsync("RENCA", apellido: "Otra");
        await c.Pedidos.EnviarAsync([a, b, d, otra], "ana");

        var pendientes = await c.Seguimiento.ListarPantallazosPendientesAsync();
        Assert.Equal(2, pendientes.Count);
        Assert.Equal(2, await c.Seguimiento.ContarPantallazosPendientesAsync());
        var filaMaipu = pendientes.Single(p => p.Comuna == "MAIPU");
        Assert.Equal(3, filaMaipu.Personas.Count);
        Assert.True(filaMaipu.EsPrueba);
        Assert.Equal(TipoEnvioCorreoComuna.Pedido, filaMaipu.Tipo);

        await c.Seguimiento.AdjuntarPantallazoAsync(filaMaipu.EnvioId, Png());

        Assert.Equal([pendientes.Single(p => p.Comuna == "RENCA").EnvioId], (await Nuevo().Seguimiento.ListarPantallazosPendientesAsync()).Select(p => p.EnvioId));
        Assert.Equal(1, await Nuevo().Seguimiento.ContarPantallazosPendientesAsync());
        var seguimiento = (await Nuevo().Seguimiento.ListarAsync()).ToDictionary(f => f.Id);
        Assert.Equal(4, seguimiento.Count);
        Assert.All(new[] { a, b, d }, id => Assert.Equal(filaMaipu.EnvioId, seguimiento[id].EnvioPantallazoId));
        Assert.Null(seguimiento[otra].EnvioPantallazoId);
        foreach (var id in new[] { a, b, d, otra })
        {
            Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await DetalleAsync(id)).Estado);
        }
        var envio = (await EnviosAsync()).Single(e => e.Id == filaMaipu.EnvioId);
        Assert.Equal($"{filaMaipu.EnvioId}.png", envio.PantallazoArchivo);
        Assert.NotNull(envio.PantallazoEn);
    }

    [Fact]
    public async Task Un_segundo_pantallazo_reemplaza_al_primero_y_borra_el_archivo_anterior_si_cambia_el_formato()
    {
        var c = Nuevo();
        var id = await SembrarSolicitadaAsync(c, "MAIPU");
        var envioId = await EnvioDeAsync(id);

        await c.Seguimiento.AdjuntarPantallazoAsync(envioId, Png());
        var viejo = Path.Combine(_dir, "almacen", $"{envioId}.png");
        Assert.True(File.Exists(viejo));

        await Nuevo().Seguimiento.AdjuntarPantallazoAsync(envioId, Jpeg());

        Assert.False(File.Exists(viejo));
        Assert.True(File.Exists(Path.Combine(_dir, "almacen", $"{envioId}.jpg")));
        Assert.Equal($"{envioId}.jpg", (await EnviosAsync()).Single(e => e.Id == envioId).PantallazoArchivo);
        var abierto = await Nuevo().Seguimiento.AbrirPantallazoAsync(envioId);
        Assert.Equal("image/jpeg", abierto!.ContentType);
        await abierto.Contenido.DisposeAsync();
    }

    [Fact]
    public async Task Un_archivo_rechazado_no_cambia_el_pantallazo_ya_guardado()
    {
        var c = Nuevo();
        var id = await SembrarSolicitadaAsync(c, "MAIPU");
        var envioId = await EnvioDeAsync(id);
        await c.Seguimiento.AdjuntarPantallazoAsync(envioId, Png());

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Seguimiento.AdjuntarPantallazoAsync(envioId, new MemoryStream("GIF89a-no-es-png"u8.ToArray())));

        Assert.Equal($"{envioId}.png", (await EnviosAsync()).Single(e => e.Id == envioId).PantallazoArchivo);
        Assert.True(File.Exists(Path.Combine(_dir, "almacen", $"{envioId}.png")));
    }

    [Fact]
    public async Task No_se_adjunta_pantallazo_a_un_correo_fallido_ni_a_un_envio_que_no_existe()
    {
        var c = Nuevo();
        var id = await SembrarSalienteAsync("MAIPU");
        _mail.FallaCuando = _ => true;
        await c.Pedidos.EnviarAsync([id], "ana");
        var fallido = Assert.Single(await EnviosAsync());
        Assert.Equal(EstadoEnvioCorreoComuna.Fallido, fallido.Estado);
        Assert.Empty(await c.Seguimiento.ListarPantallazosPendientesAsync());

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Seguimiento.AdjuntarPantallazoAsync(fallido.Id, Png()));
        await Assert.ThrowsAsync<NotFoundException>(() => Nuevo().Seguimiento.AdjuntarPantallazoAsync(Guid.NewGuid(), Png()));

        Assert.False(Directory.Exists(Path.Combine(_dir, "almacen")) && Directory.EnumerateFiles(Path.Combine(_dir, "almacen")).Any());
    }

    [Fact]
    public async Task Un_envio_simulado_sin_casilla_de_prueba_no_aparece_en_pantallazos_pendientes_porque_no_hay_correo_que_capturar()
    {
        var c = Nuevo(casilla: null);
        var id = await SembrarSalienteAsync("MAIPU");

        var r = await c.Pedidos.EnviarAsync([id], "ana");

        Assert.True(Assert.Single(r.Envios).Simulado);
        Assert.Empty(await c.Seguimiento.ListarPantallazosPendientesAsync());
        Assert.Equal(0, await c.Seguimiento.ContarPantallazosPendientesAsync());
    }

    [Fact]
    public async Task Un_reenvio_tambien_pide_su_propio_pantallazo()
    {
        var c = Nuevo();
        var id = await SembrarSolicitadaAsync(c, "MAIPU");
        var primero = await EnvioDeAsync(id);
        await c.Seguimiento.AdjuntarPantallazoAsync(primero, Png());
        Assert.Empty(await c.Seguimiento.ListarPantallazosPendientesAsync());

        await c.Seguimiento.VolverAPedirAsync(id, "ana");

        var fila = Assert.Single(await Nuevo().Seguimiento.ListarPantallazosPendientesAsync());
        Assert.Equal(TipoEnvioCorreoComuna.Reenvio, fila.Tipo);
        Assert.Equal([id], fila.Personas.Select(p => p.Id));
    }

    // «Ya subida» desde el seguimiento.
    [Fact]
    public async Task Ya_subida_pasa_a_Cambio_dom_subido_a_Conaset_sale_del_seguimiento_y_se_puede_deshacer()
    {
        var c = Nuevo();
        var id = await SembrarSolicitadaAsync(c, "MAIPU");

        var cambio = await c.Seguimiento.YaSubidaAsync(id, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, cambio.EstadoNuevo);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, (await DetalleAsync(id)).Estado);
        Assert.Empty(await c.Seguimiento.ListarAsync());

        await c.Seguimiento.DeshacerAsync(id, cambio, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await DetalleAsync(id)).Estado);
        Assert.Equal([id], (await Nuevo().Seguimiento.ListarAsync()).Select(f => f.Id));
    }

    [Fact]
    public async Task Ya_subida_rechaza_una_carpeta_que_no_esta_en_Solicitado()
    {
        var porPedir = await SembrarSalienteAsync("MAIPU");

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Seguimiento.YaSubidaAsync(porPedir, "ana"));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(porPedir)).Estado);
    }

    // Volver a pedir: un envío Reenvio solo de esa carpeta, queda en el historial y no reinicia el plazo.
    [Fact]
    public async Task Volver_a_pedir_envia_un_Reenvio_solo_de_esa_carpeta_queda_en_el_historial_y_no_cambia_el_estado()
    {
        var c = Nuevo();
        var a = await SembrarSalienteAsync("MAIPU", apellido: "Uno");
        var b = await SembrarSalienteAsync("MAIPU", apellido: "Dos");
        await c.Pedidos.EnviarAsync([a, b], "ana");
        _mail.Enviados.Clear();

        var r = await c.Seguimiento.VolverAPedirAsync(a, "ana");

        Assert.Equal(ResultadoEnvio.Enviado, r.Resultado);
        Assert.Equal([a], r.CarpetaIds);
        Assert.Single(_mail.Enviados);
        Assert.Equal(CasillaPrueba, _mail.Enviados[0].Para);
        var envios = await EnviosAsync();
        var reenvio = Assert.Single(envios, e => e.Tipo == TipoEnvioCorreoComuna.Reenvio);
        Assert.Equal([a], reenvio.Carpetas.Select(l => l.CarpetaId));
        Assert.Equal(2, envios.Count);
        var detalle = await DetalleAsync(a);
        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, detalle.Estado);
        Assert.Contains(detalle.Historial, h => h.Campo == Carpeta.CampoPedido && h.Nuevo!.StartsWith("Vuelto a pedir por correo · MAIPU") && h.Usuario == "ana");
        Assert.DoesNotContain((await DetalleAsync(b)).Historial, h => h.Nuevo!.StartsWith("Vuelto a pedir"));
    }

    [Fact]
    public async Task Volver_a_pedir_con_el_mail_caido_no_registra_historial_y_se_puede_reintentar()
    {
        var c = Nuevo();
        var id = await SembrarSolicitadaAsync(c, "MAIPU");
        _mail.FallaCuando = _ => true;

        var r = await c.Seguimiento.VolverAPedirAsync(id, "ana");

        Assert.Equal(ResultadoEnvio.Fallido, r.Resultado);
        Assert.DoesNotContain((await DetalleAsync(id)).Historial, h => h.Nuevo!.StartsWith("Vuelto a pedir"));

        _mail.FallaCuando = null;
        Assert.Equal(ResultadoEnvio.Enviado, (await Nuevo().Seguimiento.VolverAPedirAsync(id, "ana")).Resultado);
    }

    [Fact]
    public async Task Volver_a_pedir_rechaza_una_carpeta_que_no_esta_en_Solicitado_y_no_envia_nada()
    {
        var porPedir = await SembrarSalienteAsync("MAIPU");

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Seguimiento.VolverAPedirAsync(porPedir, "ana"));

        Assert.Empty(_mail.Intentos);
    }

    // Criterio 6: subir prueba sin pantallazo da error; con pantallazo -> CambioDomSubidoCorreo, deshacible.
    [Fact]
    public async Task Subir_prueba_sin_pantallazo_da_error_y_con_pantallazo_pasa_a_subido_con_correo_y_se_puede_deshacer()
    {
        var c = Nuevo(real: true, casilla: null);
        var id = await SembrarSolicitadaAsync(c, "MAIPU");

        var ex = await Assert.ThrowsAsync<DomainException>(() => c.Seguimiento.SubirPruebaAsync(id, "ana"));
        Assert.Contains("pantallazo", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await DetalleAsync(id)).Estado);

        await c.Seguimiento.AdjuntarPantallazoAsync(await EnvioDeAsync(id), Png());
        _db.AdvanceClock(TimeSpan.FromDays(40));
        var cambio = await Nuevo(real: true, casilla: null).Seguimiento.SubirPruebaAsync(id, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomSubidoCorreo, cambio.EstadoNuevo);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoCorreo, (await DetalleAsync(id)).Estado);
        Assert.Empty(await Nuevo(real: true, casilla: null).Seguimiento.ListarAsync());

        await Nuevo(real: true, casilla: null).Seguimiento.DeshacerAsync(id, cambio, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await DetalleAsync(id)).Estado);
    }

    // Auditoría: el pantallazo de un envío de prueba (casilla de prueba o simulado) no es prueba ante la plataforma.
    [Fact]
    public async Task Subir_prueba_no_acepta_el_pantallazo_de_un_envio_de_prueba()
    {
        var c = Nuevo();
        var id = await SembrarSolicitadaAsync(c, "MAIPU");
        await c.Seguimiento.AdjuntarPantallazoAsync(await EnvioDeAsync(id), Png());

        var ex = await Assert.ThrowsAsync<DomainException>(() => Nuevo().Seguimiento.SubirPruebaAsync(id, "ana"));

        Assert.Contains("pantallazo", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await DetalleAsync(id)).Estado);
    }

    [Fact]
    public async Task Subir_prueba_acepta_el_pantallazo_de_un_envio_real()
    {
        var c = Nuevo(real: true, casilla: null);
        var id = await SembrarSolicitadaAsync(c, "MAIPU");
        await c.Seguimiento.AdjuntarPantallazoAsync(await EnvioDeAsync(id), Png());
        _db.AdvanceClock(TimeSpan.FromDays(40));

        await Nuevo(real: true, casilla: null).Seguimiento.SubirPruebaAsync(id, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomSubidoCorreo, (await DetalleAsync(id)).Estado);
    }

    [Fact]
    public async Task El_pantallazo_de_un_correo_sirve_de_prueba_para_todas_las_personas_de_ese_correo()
    {
        var c = Nuevo(real: true, casilla: null);
        var a = await SembrarSalienteAsync("MAIPU", apellido: "Uno");
        var b = await SembrarSalienteAsync("MAIPU", apellido: "Dos");
        await c.Pedidos.EnviarAsync([a, b], "ana");
        await c.Seguimiento.AdjuntarPantallazoAsync(await EnvioDeAsync(a), Png());

        _db.AdvanceClock(TimeSpan.FromDays(40));

        await Nuevo(real: true, casilla: null).Seguimiento.SubirPruebaAsync(a, "ana");
        await Nuevo(real: true, casilla: null).Seguimiento.SubirPruebaAsync(b, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomSubidoCorreo, (await DetalleAsync(a)).Estado);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoCorreo, (await DetalleAsync(b)).Estado);
    }

    [Theory]
    [InlineData(-4)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Subir_prueba_solo_permite_un_plazo_ya_vencido(int diasHastaVencer)
    {
        var c = Nuevo(real: true, casilla: null);
        // Find a business-day deadline relative to today, including holidays.
        var cita = Hoy.AddDays(-45);
        while (DeadlineCalculator.AddBusinessDays(cita, 15).DayNumber < Hoy.DayNumber + diasHastaVencer)
        {
            cita = cita.AddDays(1);
        }
        var vence = DeadlineCalculator.AddBusinessDays(cita, 15);
        var id = await SembrarSolicitadaAsync(c, "MAIPU", cita);
        await c.Seguimiento.AdjuntarPantallazoAsync(await EnvioDeAsync(id), Png());

        if (vence < Hoy)
        {
            await c.Seguimiento.SubirPruebaAsync(id, "ana");
            Assert.Equal(EstadoCarpeta.CambioDomSubidoCorreo, (await DetalleAsync(id)).Estado);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<DomainException>(() => c.Seguimiento.SubirPruebaAsync(id, "ana"));
            Assert.Contains("plazo", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await DetalleAsync(id)).Estado);
        }
    }

    // Insignia PRUEBA: cuántas carpetas tienen SOLO envíos de prueba (aviso al activar el envío real).
    [Fact]
    public async Task Cuenta_las_carpetas_que_solo_tienen_envios_de_prueba_y_un_reenvio_real_la_saca_de_la_cuenta()
    {
        var c = Nuevo();
        var a = await SembrarSolicitadaAsync(c, "MAIPU", apellido: "Uno");
        var b = await SembrarSolicitadaAsync(c, "RENCA", apellido: "Dos");
        var filas = (await c.Seguimiento.ListarAsync()).ToDictionary(f => f.Id);
        Assert.True(filas[a].SoloPrueba);
        Assert.True(filas[b].SoloPrueba);
        Assert.Equal(2, await c.Seguimiento.ContarSoloPruebaAsync());

        var real = Nuevo(real: true, casilla: null);
        var r = await real.Seguimiento.VolverAPedirAsync(a, "ana");

        Assert.False(r.EsPrueba);
        Assert.Contains("licencias@maipu.example.test", _mail.Intentos);
        Assert.Equal(1, await Nuevo().Seguimiento.ContarSoloPruebaAsync());
        var despues = (await Nuevo().Seguimiento.ListarAsync()).ToDictionary(f => f.Id);
        Assert.False(despues[a].SoloPrueba);
        Assert.True(despues[b].SoloPrueba);
    }

    [Fact]
    public async Task El_resumen_cuenta_las_solicitadas_las_vencidas_y_los_correos_sin_pantallazo()
    {
        var c = Nuevo();
        await SembrarSolicitadaAsync(c, "MAIPU", CitaParaVencer(Hoy.AddDays(14)), "Verde");
        await SembrarSolicitadaAsync(c, "RENCA", CitaParaVencer(Hoy.AddDays(-4)), "Pasada");

        var resumen = await c.Seguimiento.ResumenAsync();

        Assert.Equal(new ResumenSeguimiento(2, 1, 2), resumen);
    }

    [Fact]
    public async Task Una_solicitada_sin_ningun_envio_registrado_no_cuenta_como_solo_prueba()
    {
        var id = await SembrarSalienteAsync("MAIPU");
        await using (var ctx = _db.NewContext())
        {
            (await ctx.Carpetas.FindAsync(id))!.CambiarEstado(EstadoCarpeta.CambioDomicilioSolicitado, "setup", Antes);
            await ctx.SaveChangesAsync();
        }

        var fila = Assert.Single(await Nuevo().Seguimiento.ListarAsync());

        Assert.False(fila.SoloPrueba);
        Assert.Equal(0, fila.Envios);
        Assert.Equal(0, await Nuevo().Seguimiento.ContarSoloPruebaAsync());
    }

    // Criterio 5: endpoint GET /cambio-domicilio/pantallazos/{envioId}.
    private static async Task<(int Estado, DefaultHttpContext Contexto, byte[] Cuerpo)> LlamarEndpointAsync(
        SeguimientoService seguimiento, Guid envioId, string? origin)
    {
        var contexto = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        contexto.Request.Host = new HostString("localhost", 5187);
        if (origin is not null)
        {
            contexto.Request.Headers.Origin = origin;
        }
        var cuerpo = new MemoryStream();
        contexto.Response.Body = cuerpo;

        var resultado = await PantallazosEndpoint.ServirAsync(contexto, envioId, seguimiento, CancellationToken.None);
        await resultado.ExecuteAsync(contexto);
        return (contexto.Response.StatusCode, contexto, cuerpo.ToArray());
    }

    [Fact]
    public async Task El_endpoint_rechaza_un_origen_ajeno_con_403_y_sin_leer_el_archivo()
    {
        var c = Nuevo();
        var id = await SembrarSolicitadaAsync(c, "MAIPU");
        var envioId = await EnvioDeAsync(id);
        await c.Seguimiento.AdjuntarPantallazoAsync(envioId, Png());

        var (estado, _, cuerpo) = await LlamarEndpointAsync(c.Seguimiento, envioId, "http://evil.example.test");

        Assert.Equal(403, estado);
        Assert.Empty(cuerpo);
    }

    [Fact]
    public async Task El_endpoint_entrega_el_pantallazo_al_origen_propio_con_cabeceras_de_privacidad_y_nombre_generico()
    {
        var c = Nuevo();
        var id = await SembrarSolicitadaAsync(c, "MAIPU");
        var envioId = await EnvioDeAsync(id);
        await c.Seguimiento.AdjuntarPantallazoAsync(envioId, Png(10));

        foreach (var origin in new string?[] { "http://localhost:5187", null })
        {
            var (estado, contexto, cuerpo) = await LlamarEndpointAsync(Nuevo().Seguimiento, envioId, origin);

            Assert.Equal(200, estado);
            Assert.Equal(Png(10).ToArray(), cuerpo);
            var h = contexto.Response.Headers;
            Assert.Equal("no-store", h.CacheControl.ToString());
            Assert.Equal("nosniff", h["X-Content-Type-Options"].ToString());
            Assert.Equal("image/png", contexto.Response.ContentType);
            var disposicion = h.ContentDisposition.ToString();
            Assert.StartsWith("attachment", disposicion);
            Assert.Contains("pantallazo.png", disposicion);
            Assert.DoesNotContain(envioId.ToString(), disposicion);
            Assert.DoesNotContain("MAIPU", disposicion, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task El_endpoint_da_404_si_el_envio_no_existe_no_tiene_pantallazo_o_el_archivo_se_perdio()
    {
        var c = Nuevo();
        var id = await SembrarSolicitadaAsync(c, "MAIPU");
        var envioId = await EnvioDeAsync(id);

        Assert.Equal(404, (await LlamarEndpointAsync(c.Seguimiento, Guid.NewGuid(), null)).Estado);
        Assert.Equal(404, (await LlamarEndpointAsync(c.Seguimiento, envioId, null)).Estado);

        await c.Seguimiento.AdjuntarPantallazoAsync(envioId, Png());
        File.Delete(Path.Combine(_dir, "almacen", $"{envioId}.png"));

        var (estado, contexto, _) = await LlamarEndpointAsync(Nuevo().Seguimiento, envioId, null);
        Assert.Equal(404, estado);
        Assert.Equal("no-store", contexto.Response.Headers.CacheControl.ToString());
    }

    private sealed class MailFalso : IMailSender
    {
        public Func<string, bool>? FallaCuando { get; set; }

        public List<string> Intentos { get; } = [];

        public List<(string Para, string Asunto)> Enviados { get; } = [];

        public bool IsConfigured => true;

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
        {
            Intentos.Add(toAddress);
            if (FallaCuando?.Invoke(toAddress) == true)
            {
                return Task.FromException(new InvalidOperationException("falla simulada"));
            }

            Enviados.Add((toAddress, subject));
            return Task.CompletedTask;
        }
    }
}
