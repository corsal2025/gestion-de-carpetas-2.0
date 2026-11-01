using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Otorgamiento;

/// <summary>One row of the «Para otorgar» tray: a carpeta already uploaded (or 1° Licencia / Canje) with no final decision yet.</summary>
public sealed record ParaOtorgarDto(
    Guid Id,
    string Rut,
    string NombreCompleto,
    Sede Sede,
    DateOnly FechaCitacion,
    DateOnly? FechaSubida,
    EstadoCarpeta Estado,
    string? TipoTramite,
    Resolucion? Resolucion,
    bool F8Pendiente = false,
    bool CertificadoPendiente = false,
    bool RespaldoPendiente = false);

/// <summary>Filters of the tray: sede and upload-date range (both ends included).</summary>
public sealed record FiltroParaOtorgar(Sede? Sede = null, DateOnly? SubidaDesde = null, DateOnly? SubidaHasta = null);

/// <summary>Read side of «Otorgamiento». Implemented in Infrastructure over the circuit's shared DbContext.</summary>
public interface IOtorgamientoRepository
{
    /// <summary>Carpetas waiting for a decision (see <see cref="Carpeta.ParaOtorgarExpresion"/>), oldest upload first, plus the total of the filtered set.</summary>
    Task<ListResult<ParaOtorgarDto>> ListarAsync(FiltroParaOtorgar filtro, int limite, CancellationToken ct = default);

    /// <summary>How many carpetas wait for a decision, without filters (menu counter).</summary>
    Task<int> ContarAsync(CancellationToken ct = default);
}
