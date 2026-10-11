using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>Persistence port for carpetas. Implemented in Infrastructure.</summary>
public interface ICarpetaRepository
{
    Task<Carpeta?> GetAsync(Guid id, CancellationToken ct = default);

    Task<Ciudadano?> FindCiudadanoAsync(Rut rut, CancellationToken ct = default);

    /// <summary>Open carpetas (no final decision) of a citizen, in any sede.</summary>
    Task<IReadOnlyList<CarpetaAbiertaDto>> GetCarpetasAbiertasAsync(Guid ciudadanoId, CancellationToken ct = default);

    Task AddAsync(Carpeta carpeta, CancellationToken ct = default);

    Task<(IReadOnlyList<CarpetaResumenDto> Items, int Total)> SearchAsync(CarpetaSearch search, CancellationToken ct = default);

    /// <summary>Folder count per tray for the given filters (tray filter ignored), in one grouped query.</summary>
    Task<IReadOnlyDictionary<Bandeja, int>> CountByBandejaAsync(CarpetaSearch search, CancellationToken ct = default);

    /// <summary>Audit entries of the given carpetas, used to compute tray indicators for a visible range.</summary>
    Task<IReadOnlyList<MovimientoDto>> GetMovimientosAsync(IReadOnlyCollection<Guid> carpetaIds, CancellationToken ct = default);

    Task<EstadisticasGlobalesDto> GetEstadisticasAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CajaResumenDto>> GetCajasAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CarpetaResumenDto>> GetCarpetasByCajaAsync(string caja, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTiposTramiteAsync(CancellationToken ct = default);

    /// <summary>For each given carpeta, a summary of the other carpetas of the same citizen (absent when none).</summary>
    Task<IReadOnlyDictionary<Guid, TramitePrevioDto>> GetTramitesPreviosAsync(IReadOnlyCollection<Guid> carpetaIds, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
