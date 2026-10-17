using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Solicitudes;

/// <summary>
/// Use cases of the F8 module. All repositories must share one unit of work (one scoped DbContext): each use case is
/// a single save, so the request, the carpeta state change and the certificate it may open commit together.
/// </summary>
public sealed class SolicitudesF8Service(
    ICarpetaRepository carpetas,
    ISolicitudF8Repository solicitudes,
    ICertificadoRepository certificados,
    IFirmantesRepository firmantes,
    TimeProvider clock,
    INotificadorCertificado? notificador = null)
{
    // Same rationale as CarpetaService: the circuit shares this service and its DbContext, so writes (and the discard
    // on failure) are serialized.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly EnvioCertificadoDirector _envio = new(carpetas, certificados, firmantes, notificador, clock);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// "Solicitar F8" (trays). Creates the request unless one is already open for the carpeta, in which case nothing
    /// is created and the result says so.
    /// </summary>
    public async Task<SolicitarF8Resultado> SolicitarF8Async(Guid carpetaId, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(carpetaId, ct);
            var resultado = await SolicitudF8Creacion.SolicitarAsync(solicitudes, carpeta.Id, carpeta.Estado, autor, Now, ct);
            if (resultado.Creada)
            {
                await carpetas.SaveChangesAsync(ct);
            }
            return resultado;
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

    /// <summary>The F8 was generated and uploaded: closes the request and sends the carpeta to SubidaConF8 (audited).</summary>
    public async Task MarcarSubidoAsync(Guid solicitudId, string autor, string? observacion = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var solicitud = await LoadAsync(solicitudId, ct);
            var carpeta = await LoadActivaAsync(solicitud.CarpetaId, ct);
            RequireNoFinal(carpeta, "Subida con F8");
            var now = Now;
            solicitud.MarcarSubido(autor, now, observacion);
            carpeta.ForzarEstado(EstadoCarpeta.SubidaConF8, autor, now);
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

    /// <summary>
    /// The F8 could not be generated: closes the request as NoSeGenero, sends the carpeta to Crear certificado and
    /// opens a SinCarpeta certificate linked to the request (or reuses the one already open). The notification to
    /// the director goes out after the commit; a failing adapter never undoes the resolution.
    /// </summary>
    public async Task<NoSeGeneroResultado> MarcarNoSeGeneroAsync(Guid solicitudId, string autor, string? observacion = null, CancellationToken ct = default)
    {
        Guid certificadoId;
        bool yaExistia;
        await _gate.WaitAsync(ct);
        try
        {
            var solicitud = await LoadAsync(solicitudId, ct);
            var carpeta = await LoadActivaAsync(solicitud.CarpetaId, ct);
            RequireNoFinal(carpeta, "Crear certificado / oficio");
            var now = Now;
            solicitud.MarcarNoSeGenero(autor, now, observacion);
            carpeta.ForzarEstado(EstadoCarpeta.CrearCertificado, autor, now);

            var abierto = await certificados.GetAbiertoAsync(carpeta.Id, TipoCertificado.SinCarpeta, ct);
            yaExistia = abierto is not null;
            if (abierto is null)
            {
                abierto = SolicitudCertificado.CrearDesdeF8(carpeta.Id, solicitud.Id, autor, now);
                await certificados.AddAsync(abierto, ct);
            }

            await carpetas.SaveChangesAsync(ct);
            certificadoId = abierto.Id;
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

        if (yaExistia)
        {
            return new NoSeGeneroResultado(certificadoId, true, false, null);
        }

        var envio = await _envio.EnviarAsync(certificadoId, _gate, ct);
        return new NoSeGeneroResultado(certificadoId, false, envio.Enviado, envio.Error);
    }

    public async Task<ListResult<SolicitudF8Dto>> ListarAsync(ListarSolicitudesF8Query query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (rut, nombre) = TextoBusqueda.Parse(query.Texto);
        var (skip, take) = ListadoSolicitudes.Acotar(query.Skip, query.Take);
        var (items, total) = await solicitudes.ListarAsync(new FiltroSolicitudesF8(query.Estado, rut, nombre, skip, take), ct);
        return new ListResult<SolicitudF8Dto>(items, total);
    }

    /// <summary>Requests per state for the menu badge; every state is present (zero when empty).</summary>
    public async Task<IReadOnlyDictionary<EstadoSolicitudF8, int>> ContarPorEstadoAsync(CancellationToken ct = default)
    {
        var cuentas = await solicitudes.ContarPorEstadoAsync(ct);
        return Enum.GetValues<EstadoSolicitudF8>().ToDictionary(e => e, e => cuentas.GetValueOrDefault(e));
    }

    /// <summary>A stale row must not drag a carpeta whose process already ended (Otorgado / Denegado) back into a tray.</summary>
    private static void RequireNoFinal(Carpeta carpeta, string destino)
    {
        if (EstadoTransitions.IsFinal(carpeta.Estado) || carpeta.Decision != Decision.Pendiente)
        {
            throw new DomainException(
                $"La carpeta ya está en un estado final ({carpeta.Estado}): no se puede enviar a «{destino}».");
        }
    }

    private async Task<SolicitudF8> LoadAsync(Guid id, CancellationToken ct) =>
        await solicitudes.GetAsync(id, ct) ?? throw new NotFoundException($"Solicitud de F8 {id} no encontrada.");

    private async Task<Carpeta> LoadActivaAsync(Guid id, CancellationToken ct)
    {
        var carpeta = await carpetas.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");
        return carpeta.Anulada
            ? throw new DomainException("La carpeta está anulada: restáurela desde «Anuladas» antes de modificarla.")
            : carpeta;
    }
}

/// <summary>The one place that decides "create the F8 request unless one is already open" (button and automatic path).</summary>
internal static class SolicitudF8Creacion
{
    public static async Task<SolicitarF8Resultado> SolicitarAsync(
        ISolicitudF8Repository solicitudes, Guid carpetaId, EstadoCarpeta estadoOrigen, string autor, DateTime now, CancellationToken ct)
    {
        var abierta = await solicitudes.GetAbiertaPorCarpetaAsync(carpetaId, ct);
        if (abierta is not null)
        {
            return new SolicitarF8Resultado(abierta.Id, false, true, "Ya existe una solicitud de F8 abierta para esta carpeta.");
        }

        var nueva = SolicitudF8.Solicitar(carpetaId, estadoOrigen, autor, now);
        await solicitudes.AddAsync(nueva, ct);
        return new SolicitarF8Resultado(nueva.Id, true, false, null);
    }
}
