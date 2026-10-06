using Sgl.Domain;

namespace Sgl.Application.Carpetas;

public sealed record RegistrarCarpetaCommand(
    string Rut,
    string Nombre,
    string Apellido,
    Sede Sede,
    DateOnly FechaCitacion,
    string Autor);

public sealed record CambiarEstadoCommand(Guid CarpetaId, EstadoCarpeta NuevoEstado, string Autor);

public sealed record EditarCarpetaCommand(
    Guid CarpetaId,
    Sede Sede,
    DateOnly FechaCitacion,
    DateOnly? FechaSubida,
    string? IdoneidadMoral,
    string Autor,
    string? Rut = null,
    string? FechaUltimaCarpeta = null,
    string? Comuna = null);

public sealed record AsignarCajaCommand(Guid CarpetaId, string? NuevaCaja, string Autor);

public sealed record AsignarCajaLoteCommand(IReadOnlyList<Guid> CarpetaIds, string NuevaCaja, string Autor);

public sealed record ListarCarpetasQuery(
    string? Texto = null,
    Sede? Sede = null,
    EstadoCarpeta? Estado = null,
    string? TipoTramite = null,
    string? CajaArchivo = null,
    bool? SinCaja = null,
    string? Comuna = null,
    int Page = 1,
    int PageSize = ListarCarpetasQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public ListarCarpetasQuery Normalized() => this with
    {
        Texto = string.IsNullOrWhiteSpace(Texto) ? null : Texto.Trim(),
        TipoTramite = string.IsNullOrWhiteSpace(TipoTramite) ? null : TipoTramite.Trim(),
        CajaArchivo = string.IsNullOrWhiteSpace(CajaArchivo) ? null : CajaArchivo.Trim(),
        Comuna = string.IsNullOrWhiteSpace(Comuna) ? null : Comuna.Trim(),
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize, 1, MaxPageSize),
    };
}

public sealed record CarpetaResumenDto(
    Guid Id,
    string Rut,
    string NombreCompleto,
    Sede Sede,
    DateOnly FechaCitacion,
    DateOnly? FechaSubida,
    string? FechaUltimaCarpeta,
    string? Comuna,
    string? IdoneidadMoral,
    string? TipoTramite,
    string? CajaArchivo,
    EstadoCarpeta Estado,
    Decision Decision);

public sealed record HistorialDto(DateTime Fecha, string Usuario, string Campo, string? Anterior, string? Nuevo);

public sealed record CarpetaDetalleDto(
    Guid Id,
    string Rut,
    string Nombre,
    string Apellido,
    Sede Sede,
    DateOnly FechaCitacion,
    DateOnly? FechaSubida,
    string? FechaUltimaCarpeta,
    string? Comuna,
    string? IdoneidadMoral,
    string? TipoTramite,
    string? CajaArchivo,
    EstadoCarpeta Estado,
    Decision Decision,
    IReadOnlyList<EstadoCarpeta> SiguientesEstados,
    IReadOnlyList<HistorialDto> Historial);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => Total == 0 ? 1 : (int)Math.Ceiling(Total / (double)PageSize);
}

public sealed record SedeMetricDto(
    Sede Sede,
    string NombreSede,
    int Total,
    int Otorgadas,
    int Denegadas,
    int Alertadas,
    int SubidasConaset,
    int PrimeraLicencia,
    int CambioDomicilio);

public sealed record EstadisticasGlobalesDto(
    int TotalCarpetas,
    int TotalOtorgadas,
    int TotalDenegadas,
    int TotalAlertadas,
    IReadOnlyList<SedeMetricDto> Sedes);

public sealed record CajaResumenDto(string Caja, int TotalCarpetas);

/// <summary>Search criteria already normalized by the application layer, ready for the repository.</summary>
public sealed record CarpetaSearch(
    string? RutFragment,
    string? NameFragment,
    Sede? Sede,
    EstadoCarpeta? Estado,
    string? TipoTramite,
    string? CajaArchivo,
    bool? SinCaja,
    string? Comuna,
    int Skip,
    int Take);
