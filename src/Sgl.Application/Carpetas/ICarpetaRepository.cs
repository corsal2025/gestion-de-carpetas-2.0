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

    /// <summary>Ids of every match, in list order (no paging): lets big trays page by primary key afterwards.</summary>
    Task<IReadOnlyList<Guid>> SearchIdsAsync(CarpetaSearch search, CancellationToken ct = default);

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

    /// <summary>For each given carpeta, the most recent other open (not archived) carpeta of the same citizen, if any.</summary>
    Task<IReadOnlyDictionary<Guid, AlertaDuplicadoDto>> GetAlertasDuplicadoAsync(IReadOnlyCollection<Guid> carpetaIds, CancellationToken ct = default);

    /// <summary>Cancelled carpetas, most recently cancelled first, optionally filtered by RUT/name fragments.</summary>
    Task<IReadOnlyList<CarpetaAnuladaDto>> GetAnuladasAsync(string? rutFragment, string? nameFragment, CancellationToken ct = default);

    /// <summary>Open carpetas of a citizen that are not archived in a real box (cancelled ones excluded).</summary>
    Task<IReadOnlyList<CarpetaAbiertaDto>> GetCarpetasAbiertasSinArchivarAsync(Guid ciudadanoId, CancellationToken ct = default);

    /// <summary>Drops every tracked, unsaved change (unit-of-work boundary after a failed operation).</summary>
    void DescartarCambios();

    Task SaveChangesAsync(CancellationToken ct = default);
}
