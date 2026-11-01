using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.CambioDomicilio;

/// <summary>
/// «Pedimos carpetas · Por pedir»: the salientes a person of our agenda asked for. They are checked one by one in Conaset
/// (already uploaded / still pending / correct), previewed per comuna and mailed in one go (one mail per comuna). Every
/// write goes through the circuit's gate; the mail itself is sent OUTSIDE the gate because the mail service takes it too
/// (the gate is not reentrant).
/// </summary>
public sealed class PedidosService(
    ICarpetaRepository carpetas,
    IPedidosRepository consulta,
    IDirectorioComunas directorio,
    ICorreoComunas correo,
    TimeProvider clock,
    UnidadDeTrabajoGate? gate = null)
{
    public const string MensajeNoEstaPorPedir = "La carpeta ya no está por pedir: actualice la lista.";

    private readonly UnidadDeTrabajoGate _gate = gate ?? new UnidadDeTrabajoGate();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<IReadOnlyList<PorPedirDto>> ListarPorPedirAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => consulta.ListarPorPedirAsync(clock.HoyChile(), ct), ct);

    public Task<int> ContarPorPedirAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => consulta.ContarPorPedirAsync(ct), ct);

    /// <summary>The person is already uploaded in Conaset: «Cambio dom. subido a Conaset», no mail. Undo with <see cref="DeshacerAsync"/>.</summary>
    public Task<CambioRealizado> YaSubidaAsync(Guid id, string autor, CancellationToken ct = default) =>
        EscribirAsync(id, (c, now) =>
        {
            c.CambiarEstado(EstadoCarpeta.CambioDomSubidoConaset, autor, now);
            c.RegistrarEvento(Carpeta.CampoRevision, null, "Ya subida: la otra comuna la subió a Conaset, no se envía correo", autor, now);
        }, ct);

    /// <summary>Still not uploaded: it stays in «Por pedir», marked as reviewed and ready for the send.</summary>
    public async Task PendienteAsync(Guid id, string autor, CancellationToken ct = default) =>
        await EscribirAsync(id, (c, now) => c.MarcarRevisada(autor, now), ct);

    /// <summary>The classification was wrong: the user picks the right one and the carpeta moves there, with the reason in its history.</summary>
    public Task<CambioRealizado> CorregirClasificacionAsync(
        Guid id, EstadoCarpeta destino, string motivo, string? fechaUltimaCarpeta, string autor, CancellationToken ct = default) =>
        EscribirAsync(id, (c, now) => c.CorregirClasificacionCambioDom(destino, motivo, fechaUltimaCarpeta, autor, now), ct);

    /// <summary>Undoes <see cref="YaSubidaAsync"/> or <see cref="CorregirClasificacionAsync"/>; refused if the state changed again.</summary>
    public async Task DeshacerAsync(Guid id, CambioRealizado cambio, string autor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cambio);
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await CargarAsync(id, ct);
            carpeta.RestaurarEstado(cambio.Punto, cambio.EstadoNuevo, autor, Now);
            await carpetas.SaveChangesAsync(ct);
        }
        catch
        {
            carpetas.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The mails that would go out for the chosen carpetas, grouped by comuna.</summary>
    public async Task<IReadOnlyList<GrupoPedidoDto>> VistaPreviaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var elegidas = ids.ToHashSet();
        var filas = (await ListarPorPedirAsync(ct)).Where(f => elegidas.Contains(f.Id));
        return filas
            .GroupBy(f => directorio.NombreDe(f.Comuna), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key.Length == 0)
            .ThenBy(g => g.Key, StringComparer.CurrentCulture)
            .Select(g => new GrupoPedidoDto(
                g.Key,
                g.Key.Length == 0 ? [] : directorio.CorreosDe(g.Key),
                g.Select(f => new PersonaPedidoDto(f.Id, f.Rut, f.NombreCompleto, f.Revisada, f.FalloEnvio)).ToList()))
            .ToList();
    }

    /// <summary>
    /// Mails the chosen carpetas, one mail per comuna; a failed or «sin correo» comuna does not stop the others. Each carpeta whose
    /// request mail went out moves to «Solicitado» («Pedido por correo · comuna»); the rest stay in «Por pedir».
    /// A carpeta whose mail was recorded but never moved (a crash in between) is moved without mailing it again.
    /// </summary>
    public async Task<ResultadoPedidos> EnviarAsync(IReadOnlyCollection<Guid> ids, string autor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var elegidas = ids.Distinct().ToList();
        if (elegidas.Count == 0)
        {
            throw new DomainException("Elija al menos una carpeta para enviar.");
        }

        var enLista = (await ListarPorPedirAsync(ct)).Select(f => f.Id).ToHashSet();
        if (elegidas.Any(id => !enLista.Contains(id)))
        {
            throw new DomainException(MensajeNoEstaPorPedir);
        }

        // Outside the gate: the mail service takes it too.
        var envios = await correo.EnviarAsync(TipoEnvioCorreoComuna.Pedido, elegidas, autor, ct);
        var candidatas = envios
            .Where(e => e.Resultado is ResultadoEnvio.Enviado or ResultadoEnvio.YaEnviado)
            .SelectMany(e => e.CarpetaIds)
            .Distinct()
            .ToList();
        return new ResultadoPedidos(envios, await PasarASolicitadoAsync(candidatas, autor));
    }

    /// <summary>Critical phase: the mails already left, so the move is not cancelled halfway.</summary>
    private async Task<int> PasarASolicitadoAsync(IReadOnlyCollection<Guid> candidatas, string autor)
    {
        if (candidatas.Count == 0)
        {
            return 0;
        }

        await _gate.WaitAsync();
        try
        {
            var comunas = await consulta.ComunasPedidasAsync(candidatas);
            var movidas = 0;
            foreach (var id in candidatas)
            {
                var carpeta = await carpetas.GetAsync(id);
                if (carpeta is { Estado: EstadoCarpeta.CambioDomicilio, SentidoCambioDomicilio: SentidoCambioDomicilio.Saliente, PendienteReclasificacion: false }
                    && comunas.TryGetValue(id, out var comuna))
                {
                    carpeta.MarcarPedidaPorCorreo(comuna, autor, Now);
                    movidas++;
                }
            }

            await carpetas.SaveChangesAsync();
            return movidas;
        }
        catch
        {
            carpetas.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CambioRealizado> EscribirAsync(Guid id, Action<Carpeta, DateTime> accion, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await CargarAsync(id, ct);
            if (carpeta.Estado != EstadoCarpeta.CambioDomicilio || carpeta.SentidoCambioDomicilio != SentidoCambioDomicilio.Saliente
                || carpeta.PendienteReclasificacion)
            {
                throw new DomainException(MensajeNoEstaPorPedir);
            }

            var punto = carpeta.PuntoRestauracion();
            accion(carpeta, Now);
            await carpetas.SaveChangesAsync(ct);
            return new CambioRealizado(punto, carpeta.Estado);
        }
        catch
        {
            carpetas.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Carpeta> CargarAsync(Guid id, CancellationToken ct) =>
        await carpetas.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");
}
