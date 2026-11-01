using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.CambioDomicilio;

/// <summary>
/// «Reclasificación»: the mandatory stage between a carpeta entering «Cambio de domicilio» (classified by a person, or created from
/// an email) and its next stage («Por pedir» / «Nos piden carpetas»). A person confirms it, or corrects the classification when it
/// was wrong. Every write goes through the circuit's gate.
/// </summary>
public sealed class ReclasificacionService(
    ICarpetaRepository carpetas,
    IReclasificacionRepository consulta,
    TimeProvider clock,
    UnidadDeTrabajoGate? gate = null)
{
    public const string MensajeNoPendiente = "La carpeta ya no está pendiente de reclasificación: actualice la lista.";

    /// <summary>Reason stored when the person corrects without writing one.</summary>
    public const string MotivoPorDefecto = "Corregida en Reclasificación";

    /// <summary>Where a wrongly classified saliente can go. Never «No existe carpeta», F8, certificate or oficio: those are not part of its flow.</summary>
    public static IReadOnlyList<EstadoCarpeta> DestinosSaliente { get; } =
    [
        EstadoCarpeta.PrimeraLicencia,
        EstadoCarpeta.SeEncuentraEnArchivos,
        EstadoCarpeta.SeEncuentraEnOf43,
        EstadoCarpeta.CanjeLicExtranjera,
    ];

    /// <summary>Where a wrongly classified entrante can go (the same corrections «Nos piden carpetas» already offers).</summary>
    public static IReadOnlyList<EstadoCarpeta> DestinosEntrante => EntrantesService.DestinosCorreccion;

    public static IReadOnlyList<EstadoCarpeta> DestinosDe(SentidoCambioDomicilio sentido) =>
        sentido == SentidoCambioDomicilio.Saliente ? DestinosSaliente : DestinosEntrante;

    private readonly UnidadDeTrabajoGate _gate = gate ?? new UnidadDeTrabajoGate();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<IReadOnlyList<ReclasificacionDto>> ListarAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => consulta.ListarAsync(ct), ct);

    public Task<int> ContarAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => consulta.ContarAsync(ct), ct);

    /// <summary>
    /// Confirms the chosen carpetas, all or none: when any of them is no longer pending (or does not exist) nothing is confirmed and
    /// the call fails. Returns how many were confirmed.
    /// </summary>
    public async Task<int> ConfirmarAsync(IReadOnlyCollection<Guid> ids, string autor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var elegidas = ids.Distinct().ToList();
        if (elegidas.Count == 0)
        {
            throw new DomainException("Elija al menos una carpeta para confirmar.");
        }

        await _gate.WaitAsync(ct);
        try
        {
            var cargadas = new List<Carpeta>(elegidas.Count);
            foreach (var id in elegidas)
            {
                var carpeta = await carpetas.GetAsync(id, ct);
                if (carpeta is not { PendienteReclasificacion: true, Anulada: false })
                {
                    throw new DomainException(MensajeNoPendiente);
                }

                cargadas.Add(carpeta);
            }

            var now = Now;
            foreach (var carpeta in cargadas)
            {
                carpeta.ConfirmarReclasificacion(autor, now);
            }

            await carpetas.SaveChangesAsync(ct);
            return cargadas.Count;
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

    /// <summary>Confirms every carpeta that is pending right now; returns how many (0 when there is none).</summary>
    public async Task<int> ConfirmarTodasAsync(string autor, CancellationToken ct = default)
    {
        var ids = (await ListarAsync(ct)).Select(f => f.Id).ToList();
        return ids.Count == 0 ? 0 : await ConfirmarAsync(ids, autor, ct);
    }

    /// <summary>
    /// The classification was wrong: the carpeta moves to <paramref name="destino"/> (and leaves «Reclasificación»). For Archivos /
    /// Of. 43 the last-carpeta date must say so. The reason is optional here; the history always keeps one.
    /// </summary>
    public async Task<CambioRealizado> CorregirAsync(
        Guid id, EstadoCarpeta destino, string? motivo, string? fechaUltimaCarpeta, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await carpetas.GetAsync(id, ct);
            if (carpeta is not { PendienteReclasificacion: true, Anulada: false, SentidoCambioDomicilio: { } sentido })
            {
                throw new DomainException(MensajeNoPendiente);
            }

            if (!DestinosDe(sentido).Contains(destino))
            {
                throw new DomainException(sentido == SentidoCambioDomicilio.Saliente
                    ? "Elija una clasificación válida: 1° licencia, Archivos / Of. 43 o canje de licencia extranjera."
                    : "Elija una clasificación válida: 1° licencia o canje de licencia extranjera.");
            }

            var punto = carpeta.PuntoRestauracion();
            carpeta.CorregirClasificacionCambioDom(
                destino, string.IsNullOrWhiteSpace(motivo) ? MotivoPorDefecto : motivo, fechaUltimaCarpeta, autor, Now);
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

    /// <summary>Undoes <see cref="CorregirAsync"/>: the carpeta goes back to «Reclasificación»; refused if the state changed again.</summary>
    public async Task DeshacerAsync(Guid id, CambioRealizado cambio, string autor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cambio);
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await carpetas.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");
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
}
