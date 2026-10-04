using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>Persistence port for carpetas. Implemented in Infrastructure.</summary>
public interface ICarpetaRepository
{
    Task<Carpeta?> GetAsync(Guid id, CancellationToken ct = default);

    Task<Ciudadano?> FindCiudadanoAsync(Rut rut, CancellationToken ct = default);

    Task AddAsync(Carpeta carpeta, CancellationToken ct = default);

    Task<(IReadOnlyList<CarpetaResumenDto> Items, int Total)> SearchAsync(CarpetaSearch search, CancellationToken ct = default);

    Task<EstadisticasGlobalesDto> GetEstadisticasAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CajaResumenDto>> GetCajasAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CarpetaResumenDto>> GetCarpetasByCajaAsync(string caja, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTiposTramiteAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
