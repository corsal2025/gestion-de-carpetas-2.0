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
    string Autor);

public sealed record ListarCarpetasQuery(
    string? Texto = null,
    Sede? Sede = null,
    EstadoCarpeta? Estado = null,
    int Page = 1,
    int PageSize = ListarCarpetasQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public ListarCarpetasQuery Normalized() => this with
    {
        Texto = string.IsNullOrWhiteSpace(Texto) ? null : Texto.Trim(),
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
    EstadoCarpeta Estado,
    Decision Decision,
    string? IdoneidadMoral,
    IReadOnlyList<EstadoCarpeta> SiguientesEstados,
    IReadOnlyList<HistorialDto> Historial);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => Total == 0 ? 1 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>Search criteria already normalized by the application layer, ready for the repository.</summary>
public sealed record CarpetaSearch(string? RutFragment, string? NameFragment, Sede? Sede, EstadoCarpeta? Estado, int Skip, int Take);
