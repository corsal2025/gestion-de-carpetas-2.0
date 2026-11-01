using Sgl.Application.Carpetas;
using Sgl.Application.Seguridad;
using Sgl.Domain;

namespace Sgl.Application.Otorgamiento;

/// <summary>
/// «Otorgamiento»: the final decision of each carpeta (Otorgada, Denegada) and its intermediate outcome (Espera examen,
/// Clase pendiente, Para denegar). Every write checks <see cref="IPermisos.PuedeOtorgar"/> first and goes through the
/// circuit's gate; undoing is also restricted to who may decide.
/// </summary>
public sealed class OtorgamientoService(
    ICarpetaRepository carpetas,
    IOtorgamientoRepository consulta,
    IPermisos permisos,
    TimeProvider clock,
    UnidadDeTrabajoGate? gate = null)
{
    public const string MensajeSinPermiso = "No tiene permiso para otorgar.";

    private const int LimiteMaximo = 5000;

    private readonly UnidadDeTrabajoGate _gate = gate ?? new UnidadDeTrabajoGate();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Whether the current user sees the decision buttons.</summary>
    public bool PuedeOtorgar() => permisos.PuedeOtorgar();

    public Task<ListResult<ParaOtorgarDto>> ListarAsync(FiltroParaOtorgar filtro, int limite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        return _gate.LeerAsync(() => consulta.ListarAsync(filtro, Math.Clamp(limite, 1, LimiteMaximo), ct), ct);
    }

    public Task<int> ContarAsync(CancellationToken ct = default) => _gate.LeerAsync(() => consulta.ContarAsync(ct), ct);

    /// <returns>The values before the decision, to undo it with <see cref="DeshacerAsync"/>.</returns>
    public Task<PuntoRestauracionEstado> OtorgarAsync(Guid id, string autor, CancellationToken ct = default) =>
        EscribirAsync(id, (c, now) => c.Otorgar(autor, now), ct);

    public Task<PuntoRestauracionEstado> DenegarAsync(Guid id, string autor, CancellationToken ct = default) =>
        EscribirAsync(id, (c, now) => c.Denegar(autor, now), ct);

    public Task<PuntoRestauracionEstado> AsignarResolucionAsync(Guid id, Resolucion resolucion, string autor, CancellationToken ct = default) =>
        EscribirAsync(id, (c, now) => c.AsignarResolucion(resolucion, autor, now), ct);

    /// <summary>Undoes a decision or resolution: back to Pendiente, no grant date. Refused when the state changed meanwhile.</summary>
    public async Task DeshacerAsync(Guid id, PuntoRestauracionEstado punto, string autor, CancellationToken ct = default)
    {
        ExigirPermiso();
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await CargarActivaAsync(id, ct);
            carpeta.RestaurarEstado(punto, punto.Estado, autor, Now, permiteDeshacerDecision: true);
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

    private async Task<PuntoRestauracionEstado> EscribirAsync(Guid id, Action<Carpeta, DateTime> accion, CancellationToken ct)
    {
        ExigirPermiso();
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await CargarActivaAsync(id, ct);
            var punto = carpeta.PuntoRestauracion();
            accion(carpeta, Now);
            await carpetas.SaveChangesAsync(ct);
            return punto;
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            carpetas.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ExigirPermiso()
    {
        if (!permisos.PuedeOtorgar())
        {
            throw new DomainException(MensajeSinPermiso);
        }
    }

    private async Task<Carpeta> CargarActivaAsync(Guid id, CancellationToken ct)
    {
        var carpeta = await carpetas.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");
        return carpeta.Anulada
            ? throw new DomainException("La carpeta está anulada: restáurela desde «Anuladas» antes de modificarla.")
            : carpeta;
    }
}
