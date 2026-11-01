using Sgl.Application.Carpetas;
using Sgl.Application.Otorgamiento;
using Sgl.Application.Seguridad;
using Sgl.Domain;

namespace Sgl.Application.Tests;

/// <summary>E8: lo que la dirección del caso cambia en Otorgamiento y en F8 (la sincronización y la migración tienen sus propios archivos).</summary>
public sealed class CambioDomicilioSentidoTests : IDisposable
{
    private static readonly DateTime Antes = new(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = new();
    private int _rut;

    public void Dispose() => _db.Dispose();

    private sealed class PermisoTotal : IPermisos
    {
        public bool PuedeOtorgar() => true;
    }

    private static readonly string[] Ruts = ["12345678-5", "18785387-7", "18586259-3", "11111111-1", "22222222-2"];

    private Ciudadano Persona() => new(Rut.Parse(Ruts[_rut++]), "Persona", "Ficticia");

    private async Task<Guid> SembrarEntranteAsync(EstadoCarpeta estado)
    {
        await using var ctx = _db.NewContext();
        var c = Carpeta.RegistrarDesdeCorreo(Persona(), Sede.AvArgentina, new DateOnly(2026, 5, 1), "Viña del Mar", false, "Sistema · correo", Antes);
        c.ConfirmarReclasificacion("setup", Antes);
        if (estado != EstadoCarpeta.CambioDomicilio)
        {
            c.ForzarEstado(estado, "setup", Antes);
        }
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<Guid> SembrarSalienteAsync(EstadoCarpeta estado)
    {
        await using var ctx = _db.NewContext();
        var c = Carpeta.CrearDesdeAgenda(Persona(), Sede.AvArgentina, new DateOnly(2026, 5, 1), new TimeOnly(9, 0), TramiteAgenda.Renovacion, "setup", Antes);
        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, "Viña del Mar", "setup", Antes);
        c.ConfirmarReclasificacion("setup", Antes);
        if (estado != EstadoCarpeta.CambioDomicilio)
        {
            c.ForzarEstado(estado, "setup", Antes);
        }
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    // Criterio 6
    [Theory]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset)]
    [InlineData(EstadoCarpeta.SubidaConF8)]
    [InlineData(EstadoCarpeta.SubidaConOficio)]
    public async Task Una_entrante_subida_no_aparece_en_Para_otorgar_y_OtorgamientoService_la_rechaza(EstadoCarpeta estado)
    {
        var entrante = await SembrarEntranteAsync(estado);
        var saliente = await SembrarSalienteAsync(EstadoCarpeta.CambioDomSubidoConaset);
        var servicio = _db.NewOtorgamientoService(new PermisoTotal());

        var lista = await servicio.ListarAsync(new FiltroParaOtorgar(), 100);

        Assert.Equal([saliente], lista.Items.Select(i => i.Id).ToList());
        Assert.Equal(1, await servicio.ContarAsync());
        await Assert.ThrowsAsync<DomainException>(() => servicio.OtorgarAsync(entrante, "matias"));
        await Assert.ThrowsAsync<DomainException>(() => servicio.DenegarAsync(entrante, "matias"));
        await Assert.ThrowsAsync<DomainException>(() => servicio.AsignarResolucionAsync(entrante, Resolucion.EsperaExamen, "matias"));

        await servicio.OtorgarAsync(saliente, "matias");
        var detalle = await _db.NewService().ObtenerAsync(entrante);
        Assert.Equal(estado, detalle.Estado);
        Assert.Equal(Decision.Pendiente, detalle.Decision);
    }

    // Criterio 7
    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    public async Task Pedir_F8_para_una_saliente_lanza_DomainException_y_no_crea_solicitud(EstadoCarpeta estado)
    {
        var id = await SembrarSalienteAsync(estado);
        var servicio = _db.NewF8Service();

        await Assert.ThrowsAsync<DomainException>(() => servicio.SolicitarF8Async(id, "ana"));

        Assert.Empty((await _db.NewF8Service().ListarAsync(new Sgl.Application.Solicitudes.ListarSolicitudesF8Query(null, null))).Items);
    }

    [Fact]
    public async Task Pedir_F8_para_una_entrante_funciona()
    {
        var id = await SembrarEntranteAsync(EstadoCarpeta.CambioDomicilio);

        var r = await _db.NewF8Service().SolicitarF8Async(id, "ana");

        Assert.True(r.Creada);
        var fila = Assert.Single((await _db.NewF8Service().ListarAsync(new Sgl.Application.Solicitudes.ListarSolicitudesF8Query(null, null))).Items);
        Assert.Equal(id, fila.CarpetaId);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, fila.EstadoOrigen);
    }

    [Fact]
    public async Task El_detalle_ofrece_a_la_saliente_solo_sus_siguientes_estados_y_expone_la_direccion()
    {
        var id = await SembrarSalienteAsync(EstadoCarpeta.CambioDomicilio);
        var entrante = await SembrarEntranteAsync(EstadoCarpeta.CambioDomicilio);

        var detalle = await _db.NewService().ObtenerAsync(id);
        var detalleEntrante = await _db.NewService().ObtenerAsync(entrante);

        Assert.Equal(SentidoCambioDomicilio.Saliente, detalle.Sentido);
        Assert.Equal([EstadoCarpeta.CambioDomicilioSolicitado, EstadoCarpeta.CambioDomSubidoConaset], detalle.SiguientesEstados);
        Assert.Equal(SentidoCambioDomicilio.Entrante, detalleEntrante.Sentido);
        Assert.DoesNotContain(EstadoCarpeta.CambioDomicilioSolicitado, detalleEntrante.SiguientesEstados);
        Assert.Contains(EstadoCarpeta.CambioDomSinRegistro, detalleEntrante.SiguientesEstados);
        Assert.Contains(EstadoCarpeta.CambioDomCarpetaDigital, detalleEntrante.SiguientesEstados);
    }

    [Fact]
    public async Task El_forzado_de_una_saliente_a_F8_es_rechazado_por_el_servicio_sin_cambiar_nada()
    {
        var id = await SembrarSalienteAsync(EstadoCarpeta.CambioDomicilioSolicitado);

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.SubidaConF8, "ana"));

        Assert.Equal(EstadoCarpeta.CambioDomicilioSolicitado, (await _db.NewService().ObtenerAsync(id)).Estado);
    }
}
