using Sgl.Domain;

namespace Sgl.Application.Carpetas;

public sealed record RegistrarCarpetaCommand(
    string Rut,
    string Nombre,
    string Apellido,
    Sede Sede,
    DateOnly FechaCitacion,
    string Autor,
    ClaseLicencia Clases = ClaseLicencia.Ninguna,
    bool ConfirmarDuplicado = false);

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
    int Skip = 0,
    int? Take = null,
    Bandeja? Bandeja = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null)
{

    public ListarCarpetasQuery Normalized() => this with
    {
        Texto = string.IsNullOrWhiteSpace(Texto) ? null : Texto.Trim(),
        TipoTramite = string.IsNullOrWhiteSpace(TipoTramite) ? null : TipoTramite.Trim(),
        CajaArchivo = string.IsNullOrWhiteSpace(CajaArchivo) ? null : CajaArchivo.Trim(),
        Comuna = string.IsNullOrWhiteSpace(Comuna) ? null : Comuna.Trim(),
        Skip = Math.Max(0, Skip),
        Take = Take is { } t ? Math.Max(0, t) : null,
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
    Decision Decision,
    bool PendienteBusqueda = false)
{
    /// <summary>When the carpeta was registered or imported (only filled by ranged listings).</summary>
    public DateTime? FechaRegistro { get; init; }

    /// <summary>Other open carpetas of the same citizen (only filled by ranged listings).</summary>
    public int? OtrasAbiertas { get; init; }

    /// <summary>Values derived from the audit trail (only filled by ranged listings).</summary>
    public IndicadoresCarpeta? Indicadores { get; init; }

    public Bandeja Bandeja => Carpeta.ClasificarBandeja(Estado, Decision, CajaArchivo, PendienteBusqueda);
}

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
    IReadOnlyList<HistorialDto> Historial,
    ClaseLicencia Clases = ClaseLicencia.Ninguna);

/// <summary>A contiguous range of a filtered list plus the total count of the whole filtered set.</summary>
public sealed record ListResult<T>(IReadOnlyList<T> Items, int Total);

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
    int? Take,
    Bandeja? Bandeja = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null);
