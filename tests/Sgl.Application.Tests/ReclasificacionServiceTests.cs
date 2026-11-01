using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Ews;
using PeticionCambioDomicilio.Comunas;
using Sgl.Application.CambioDomicilio;
using Sgl.Application.Carpetas;
using Sgl.Domain;
using Sgl.Infrastructure.CambioDomicilio;
using Sgl.Infrastructure.Persistence;
using Sgl.Web.Services;

namespace Sgl.Application.Tests;

/// <summary>Etapa «Reclasificación»: confirmar (todo o nada), corregir, deshacer y que «Por pedir» / «Nos piden» no vean lo pendiente.</summary>
public sealed class ReclasificacionServiceTests : IDisposable
{
    private static readonly DateTime Antes = new(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sgl-reclas-{Guid.NewGuid():N}");
    private readonly ComunaDirectory _directorio;
    private int _rut = 11111111;

    public ReclasificacionServiceTests()
    {
        Directory.CreateDirectory(_dir);
        var csv = Path.Combine(_dir, "comunas.csv");
        File.WriteAllText(csv, string.Join("\n", "Comuna,ContactEmail,Domain", "\"MAIPU\",\"licencias@maipu.example.test\",\"maipu.example.test\""));
        _directorio = new ComunaDirectory(csv);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* archivo aún tomado por el proceso */ }
    }

    private sealed record Circuito(ReclasificacionService Reclasificacion, PedidosService Pedidos, EntrantesService Entrantes, CarpetaService Carpetas);

    /// <summary>One context and one gate for everything, like a scoped Blazor circuit.</summary>
    private Circuito Nuevo()
    {
        var ctx = _db.NewContext();
        var gate = new UnidadDeTrabajoGate();
        var correo = new CorreoComunasService(
            ctx, new MailFalso(), _directorio, new AppOptions { TestModeEmail = "buzon.prueba@example.test" }, new CambioDomicilioOptions(),
            gate, _db.Clock, NullLogger<CorreoComunasService>.Instance);
        var carpetas = new CarpetaRepository(ctx);
        return new Circuito(
            new ReclasificacionService(carpetas, new ReclasificacionRepository(ctx), _db.Clock, gate),
            new PedidosService(carpetas, new PedidosRepository(ctx), new DirectorioComunasAdapter(_directorio), correo, _db.Clock, gate),
            new EntrantesService(carpetas, new EntrantesRepository(ctx), new SolicitudF8Repository(ctx), correo, new CambioDomicilioOptions(), _db.Clock, gate),
            _db.NewServiceCompartido(ctx, gate));
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

    private async Task<Guid> SembrarSalienteAsync(bool confirmada = false, string apellido = "Ficticia")
    {
        await using var ctx = _db.NewContext();
        var c = Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse(RutValido(_rut++)), "Persona", apellido), Sede.AvArgentina, new DateOnly(2026, 5, 1),
            new TimeOnly(9, 0), TramiteAgenda.Renovacion, "setup", Antes);
        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, "MAIPU", "setup", Antes);
        if (confirmada)
        {
            c.ConfirmarReclasificacion("setup", Antes);
        }
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<Guid> SembrarEntranteAsync(bool confirmada = false)
    {
        await using var ctx = _db.NewContext();
        var c = Carpeta.RegistrarDesdeCorreo(
            new Ciudadano(Rut.Parse(RutValido(_rut++)), "Entrante", "Ficticia"), Sede.AvArgentina, new DateOnly(2026, 5, 1), "MAIPU", false, "Sistema · correo", Antes);
        if (confirmada)
        {
            c.ConfirmarReclasificacion("setup", Antes);
        }
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<CarpetaDetalleDto> DetalleAsync(Guid id) => await _db.NewService().ObtenerAsync(id);

    private async Task<bool> PendienteAsync(Guid id)
    {
        await using var ctx = _db.NewContext();
        return (await ctx.Carpetas.AsNoTracking().SingleAsync(c => c.Id == id)).PendienteReclasificacion;
    }

    [Fact]
    public async Task Lista_las_pendientes_con_su_sentido_y_deja_fuera_confirmadas_y_anuladas()
    {
        var saliente = await SembrarSalienteAsync();
        var entrante = await SembrarEntranteAsync();
        await SembrarSalienteAsync(confirmada: true);
        var anulada = await SembrarSalienteAsync();
        await Nuevo().Carpetas.AnularAsync(anulada, "Duplicada", "ana");

        var circuito = Nuevo();
        var lista = await circuito.Reclasificacion.ListarAsync();

        Assert.Equal(2, lista.Count);
        Assert.Equal(SentidoCambioDomicilio.Saliente, lista.Single(f => f.Id == saliente).Sentido);
        Assert.Equal(SentidoCambioDomicilio.Entrante, lista.Single(f => f.Id == entrante).Sentido);
        Assert.Equal(2, await circuito.Reclasificacion.ContarAsync());
    }

    // Criterio 4
    [Fact]
    public async Task Por_pedir_y_Nos_piden_excluyen_las_pendientes_y_las_incluyen_tras_confirmar()
    {
        var saliente = await SembrarSalienteAsync();
        var entrante = await SembrarEntranteAsync();
        var circuito = Nuevo();

        Assert.Empty(await circuito.Pedidos.ListarPorPedirAsync());
        Assert.Equal(0, await circuito.Pedidos.ContarPorPedirAsync());
        Assert.Empty(await circuito.Entrantes.ListarAsync(PestanaEntrantes.PorClasificar));
        Assert.Equal(0, (await circuito.Entrantes.ResumenAsync()).PorClasificar);

        await circuito.Reclasificacion.ConfirmarAsync([saliente, entrante], "ana");

        var despues = Nuevo();
        Assert.Equal([saliente], (await despues.Pedidos.ListarPorPedirAsync()).Select(f => f.Id));
        Assert.Equal([entrante], (await despues.Entrantes.ListarAsync(PestanaEntrantes.PorClasificar)).Select(f => f.Id));
        Assert.Empty(await despues.Reclasificacion.ListarAsync());
    }

    [Fact]
    public async Task Confirmar_conserva_estado_y_fechas_y_deja_historial()
    {
        var id = await SembrarSalienteAsync();
        var antes = await DetalleAsync(id);

        var n = await Nuevo().Reclasificacion.ConfirmarAsync([id], "javiera");

        Assert.Equal(1, n);
        var despues = await DetalleAsync(id);
        Assert.Equal(antes.Estado, despues.Estado);
        Assert.Equal(antes.FechaEstadoActual, despues.FechaEstadoActual);
        Assert.Equal(antes.FechaCitacion, despues.FechaCitacion);
        Assert.False(await PendienteAsync(id));
        Assert.Contains(despues.Historial, h => h.Campo == Carpeta.CampoReclasificacion && h.Nuevo == "confirmada" && h.Usuario == "javiera");
    }

    // Criterio 5
    [Fact]
    public async Task Confirmar_tres_con_una_no_pendiente_no_confirma_ninguna_y_lanza()
    {
        var a = await SembrarSalienteAsync();
        var b = await SembrarEntranteAsync();
        var yaConfirmada = await SembrarSalienteAsync(confirmada: true);

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Reclasificacion.ConfirmarAsync([a, yaConfirmada, b], "ana"));

        Assert.True(await PendienteAsync(a));
        Assert.True(await PendienteAsync(b));
        Assert.Equal(2, await Nuevo().Reclasificacion.ContarAsync());
    }

    [Fact]
    public async Task Confirmar_sin_ids_o_con_una_carpeta_inexistente_lanza()
    {
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Reclasificacion.ConfirmarAsync([], "ana"));
        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Reclasificacion.ConfirmarAsync([Guid.NewGuid()], "ana"));
    }

    [Fact]
    public async Task Confirmar_todas_confirma_las_pendientes_y_con_ninguna_devuelve_cero()
    {
        await SembrarSalienteAsync();
        await SembrarEntranteAsync();

        Assert.Equal(2, await Nuevo().Reclasificacion.ConfirmarTodasAsync("ana"));
        Assert.Equal(0, await Nuevo().Reclasificacion.ContarAsync());
        Assert.Equal(0, await Nuevo().Reclasificacion.ConfirmarTodasAsync("ana"));
    }

    [Fact]
    public async Task Las_acciones_de_Por_pedir_y_Nos_piden_rechazan_una_carpeta_pendiente()
    {
        var saliente = await SembrarSalienteAsync();
        var entrante = await SembrarEntranteAsync();
        var circuito = Nuevo();

        await Assert.ThrowsAsync<DomainException>(() => circuito.Pedidos.YaSubidaAsync(saliente, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => circuito.Pedidos.PendienteAsync(saliente, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => circuito.Pedidos.EnviarAsync([saliente], "ana"));
        await Assert.ThrowsAsync<DomainException>(() => circuito.Entrantes.ClasificarAsync(entrante, "10-01-2024", "ana"));
        await Assert.ThrowsAsync<DomainException>(() => circuito.Entrantes.SubidaConasetAsync(entrante, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => circuito.Entrantes.NoEstaLaCarpetaAsync(entrante, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => circuito.Entrantes.CorregirClasificacionAsync(entrante, EstadoCarpeta.PrimeraLicencia, "x", "ana"));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(saliente)).Estado);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(entrante)).Estado);
    }

    // Criterio 6
    [Theory]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.CrearCertificado)]
    [InlineData(EstadoCarpeta.SubidaConOficio)]
    [InlineData(EstadoCarpeta.SubidaConF8)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    public async Task Corregir_una_saliente_a_un_destino_que_no_es_suyo_lanza_y_no_la_mueve(EstadoCarpeta destino)
    {
        var id = await SembrarSalienteAsync();

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Reclasificacion.CorregirAsync(id, destino, "x", null, "ana"));

        Assert.Equal(EstadoCarpeta.CambioDomicilio, (await DetalleAsync(id)).Estado);
        Assert.True(await PendienteAsync(id));
    }

    // Criterio 6
    [Fact]
    public async Task Corregir_una_saliente_a_primera_licencia_la_mueve_y_deja_el_sentido_en_null()
    {
        var id = await SembrarSalienteAsync();

        var cambio = await Nuevo().Reclasificacion.CorregirAsync(id, EstadoCarpeta.PrimeraLicencia, null, null, "javiera");

        Assert.Equal(EstadoCarpeta.PrimeraLicencia, cambio.EstadoNuevo);
        var d = await DetalleAsync(id);
        Assert.Equal(EstadoCarpeta.PrimeraLicencia, d.Estado);
        Assert.Null(d.Sentido);
        Assert.False(await PendienteAsync(id));
        Assert.Contains(d.Historial, h => h.Campo == Carpeta.CampoCorreccionClasificacion && h.Nuevo == ReclasificacionService.MotivoPorDefecto);
        Assert.Empty(await Nuevo().Reclasificacion.ListarAsync());
    }

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, "15-03-2019")]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43, "10-01-2024")]
    public async Task Corregir_una_saliente_a_archivos_u_of43_se_decide_por_la_fecha(EstadoCarpeta destino, string fecha)
    {
        var id = await SembrarSalienteAsync();

        await Nuevo().Reclasificacion.CorregirAsync(id, destino, "Ya tenía carpeta", fecha, "javiera");

        Assert.Equal(destino, (await DetalleAsync(id)).Estado);
    }

    [Fact]
    public async Task Corregir_una_entrante_acepta_solo_primera_licencia_y_canje()
    {
        var id = await SembrarEntranteAsync();
        var circuito = Nuevo();

        await Assert.ThrowsAsync<DomainException>(() => circuito.Reclasificacion.CorregirAsync(id, EstadoCarpeta.SeEncuentraEnArchivos, "x", "15-03-2019", "ana"));
        await Assert.ThrowsAsync<DomainException>(() => circuito.Reclasificacion.CorregirAsync(id, EstadoCarpeta.NoExisteCarpeta, "x", null, "ana"));
        await circuito.Reclasificacion.CorregirAsync(id, EstadoCarpeta.CanjeLicExtranjera, "Es canje", null, "ana");

        Assert.Equal(EstadoCarpeta.CanjeLicExtranjera, (await DetalleAsync(id)).Estado);
    }

    [Fact]
    public async Task Corregir_una_carpeta_ya_confirmada_lanza()
    {
        var id = await SembrarSalienteAsync(confirmada: true);

        await Assert.ThrowsAsync<DomainException>(() => Nuevo().Reclasificacion.CorregirAsync(id, EstadoCarpeta.PrimeraLicencia, "x", null, "ana"));
    }

    // Criterio 9
    [Fact]
    public async Task Deshacer_la_correccion_devuelve_la_carpeta_a_pendiente()
    {
        var id = await SembrarSalienteAsync();
        var circuito = Nuevo();
        var cambio = await circuito.Reclasificacion.CorregirAsync(id, EstadoCarpeta.PrimeraLicencia, "Era 1° licencia", null, "ana");
        Assert.False(await PendienteAsync(id));

        await circuito.Reclasificacion.DeshacerAsync(id, cambio, "ana");

        var d = await DetalleAsync(id);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, d.Estado);
        Assert.Equal(SentidoCambioDomicilio.Saliente, d.Sentido);
        Assert.True(await PendienteAsync(id));
        Assert.Equal([id], (await Nuevo().Reclasificacion.ListarAsync()).Select(f => f.Id));
        Assert.Empty(await Nuevo().Pedidos.ListarPorPedirAsync());
    }

    [Fact]
    public async Task Deshacer_se_rechaza_si_la_carpeta_cambio_de_estado_otra_vez()
    {
        var id = await SembrarSalienteAsync();
        var circuito = Nuevo();
        var cambio = await circuito.Reclasificacion.CorregirAsync(id, EstadoCarpeta.PrimeraLicencia, null, null, "ana");
        await circuito.Carpetas.ForzarEstadoAsync(id, EstadoCarpeta.CanjeLicExtranjera, "ana");

        await Assert.ThrowsAsync<DomainException>(() => circuito.Reclasificacion.DeshacerAsync(id, cambio, "ana"));
    }

    // Puerta obligatoria: las rutas de Listado («Subido», «Mover a…») y Detalle («Cambiar estado») no la saltan.
    [Fact]
    public async Task Las_rutas_genericas_de_estado_rechazan_una_pendiente_y_la_aceptan_confirmada()
    {
        var id = await SembrarSalienteAsync();
        var circuito = Nuevo();

        await Assert.ThrowsAsync<DomainException>(() => circuito.Carpetas.ForzarEstadoAsync(id, EstadoCarpeta.CambioDomSubidoConaset, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => circuito.Carpetas.ForzarEstadoAsync(id, EstadoCarpeta.CambioDomicilioSolicitado, "ana"));
        await Assert.ThrowsAsync<DomainException>(() =>
            circuito.Carpetas.CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.CambioDomicilioSolicitado, "ana")));
        await Assert.ThrowsAsync<DomainException>(() =>
            circuito.Carpetas.CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.CambioDomSubidoConaset, "ana")));

        var pendiente = await DetalleAsync(id);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, pendiente.Estado);
        Assert.True(pendiente.PendienteReclasificacion);

        await circuito.Reclasificacion.ConfirmarAsync([id], "ana");
        await circuito.Carpetas.ForzarEstadoAsync(id, EstadoCarpeta.CambioDomSubidoConaset, "ana");

        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, (await DetalleAsync(id)).Estado);
    }

    [Theory]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.SubidaConF8)]
    [InlineData(EstadoCarpeta.SubidaConOficio)]
    [InlineData(EstadoCarpeta.CrearCertificado)]
    [InlineData(EstadoCarpeta.SubidaConaset)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43)]
    public async Task Una_entrante_pendiente_no_sale_por_las_rutas_genericas_de_estado(EstadoCarpeta destino)
    {
        var id = await SembrarEntranteAsync();
        var circuito = Nuevo();

        await Assert.ThrowsAsync<DomainException>(() => circuito.Carpetas.ForzarEstadoAsync(id, destino, "ana"));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            circuito.Carpetas.CambiarEstadoAsync(new CambiarEstadoCommand(id, destino, "ana")));

        var pendiente = await DetalleAsync(id);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, pendiente.Estado);
        Assert.True(pendiente.PendienteReclasificacion);
    }

    [Fact]
    public async Task Una_entrante_pendiente_se_corrige_a_primera_licencia_o_canje_y_se_deshace()
    {
        var id = await SembrarEntranteAsync();
        var circuito = Nuevo();

        var cambio = await circuito.Reclasificacion.CorregirAsync(id, EstadoCarpeta.CanjeLicExtranjera, null, null, "ana");
        Assert.Equal(EstadoCarpeta.CanjeLicExtranjera, (await DetalleAsync(id)).Estado);

        await circuito.Reclasificacion.DeshacerAsync(id, cambio, "ana");
        var vuelta = await DetalleAsync(id);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, vuelta.Estado);
        Assert.True(vuelta.PendienteReclasificacion);

        await circuito.Carpetas.ForzarEstadoAsync(id, EstadoCarpeta.PrimeraLicencia, "ana");
        Assert.Equal(EstadoCarpeta.PrimeraLicencia, (await DetalleAsync(id)).Estado);
    }

    [Fact]
    public async Task El_listado_informa_si_la_carpeta_esta_pendiente_de_reclasificacion()
    {
        var pendiente = await SembrarSalienteAsync();
        var confirmada = await SembrarSalienteAsync(confirmada: true);

        var lista = await _db.NewService().ListarAsync(new ListarCarpetasQuery { Estado = EstadoCarpeta.CambioDomicilio, Take = 50 });

        Assert.True(lista.Items.Single(c => c.Id == pendiente).PendienteReclasificacion);
        Assert.False(lista.Items.Single(c => c.Id == confirmada).PendienteReclasificacion);
    }

    private sealed class MailFalso : IMailSender
    {
        public bool IsConfigured => true;

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
