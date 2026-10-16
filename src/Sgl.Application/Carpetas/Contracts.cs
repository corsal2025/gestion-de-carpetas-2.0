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
    string? Comuna = null,
    string? Caja = null,
    bool CambiarCaja = false,
    ClaseLicencia? Clases = null);

/// <summary>
/// Manual classification of one or more "Por clasificar" carpetas (applied atomically). A class is mandatory.
/// A null <see cref="Destino"/> means the default action "Buscar carpeta física": the destination is computed per
/// carpeta from the entered (or already stored) last-folder date.
/// </summary>
public sealed record ClasificarCommand(
    IReadOnlyList<Guid> CarpetaIds,
    EstadoCarpeta? Destino,
    ClaseLicencia Clases,
    string? FechaUltimaCarpeta,
    string? Comuna,
    string Autor);

/// <summary>What classification did to one carpeta (resolved destination, e.g. computed from the date).</summary>
public sealed record ClasificacionAplicada(Guid CarpetaId, EstadoCarpeta Destino);

public sealed record ClasificarResultado(IReadOnlyList<ClasificacionAplicada> Aplicadas)
{
    /// <summary>Ids that landed in the given state (to offer the search listing right away).</summary>
    public IReadOnlyList<Guid> IdsEn(EstadoCarpeta destino) =>
        Aplicadas.Where(a => a.Destino == destino).Select(a => a.CarpetaId).ToList();
}

/// <summary>
/// Red alert for a "Por clasificar" row: the same RUT already has an open (not cancelled, not archived,
/// no final decision) carpeta. The most recent one is reported.
/// </summary>
public sealed record AlertaDuplicadoDto(Guid CarpetaId, DateOnly FechaCitacion, Sede Sede, EstadoCarpeta Estado)
{
    /// <summary>How many other open carpetas the RUT has (two rows of the same RUT in "Por clasificar" alert each other).</summary>
    public int Total { get; init; } = 1;

    /// <summary>The citizen the alert belongs to (needed to mark the case as reviewed).</summary>
    public Guid CiudadanoId { get; init; }

    /// <summary>All of them, most recent first.</summary>
    public IReadOnlyList<CarpetaAbiertaDto> Todas { get; init; } = [];
}

/// <summary>A cancelled (soft-deleted) carpeta, shown only in the "Anuladas" view.</summary>
public sealed record CarpetaAnuladaDto(
    Guid Id,
    string Rut,
    string NombreCompleto,
    Sede Sede,
    DateOnly FechaCitacion,
    EstadoCarpeta Estado,
    string Motivo,
    DateTime AnuladaEn,
    string AnuladaPor);

/// <summary>Hint only: other carpetas of the same citizen. Not definitive (older history may be missing).</summary>
public sealed record TramitePrevioDto(int Cantidad, Sede UltimaSede, DateOnly UltimaFecha);

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
    DateOnly? Hasta = null,
    IReadOnlyList<Guid>? Ids = null)
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
    /// <summary>When the carpeta entered its current stage (UTC; only filled by listings).</summary>
    public DateTime? FechaEstadoActual { get; init; }

    /// <summary>Chile date the process closed (upload / certificate / final decision), when recorded.</summary>
    public DateOnly? FechaCierre { get; init; }

    /// <summary>When the physical-search listing including it was marked as printed (UTC).</summary>
    public DateTime? FechaImpresion { get; init; }

    /// <summary>When the carpeta was registered or imported (only filled by ranged listings).</summary>
    public DateTime? FechaRegistro { get; init; }

    /// <summary>Other open carpetas of the same citizen (only filled by ranged listings).</summary>
    public int? OtrasAbiertas { get; init; }

    /// <summary>Values derived from the audit trail (only filled by ranged listings).</summary>
    public IndicadoresCarpeta? Indicadores { get; init; }

    /// <summary>Appointment time from the daily agenda (agenda-imported carpetas only).</summary>
    public TimeOnly? HoraCita { get; init; }

    /// <summary>Trámite declared in the daily agenda (agenda-imported carpetas only).</summary>
    public TramiteAgenda? TramiteAgenda { get; init; }

    /// <summary>Other carpetas of the same citizen (only filled for "Por clasificar" ranged listings).</summary>
    public TramitePrevioDto? TramitePrevio { get; init; }

    /// <summary>Open carpeta of the same RUT (only filled for "Por clasificar" ranged listings).</summary>
    public AlertaDuplicadoDto? AlertaDuplicado { get; init; }

    public Bandeja Bandeja => Carpeta.ClasificarBandeja(Estado, Decision, CajaArchivo);
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
    ClaseLicencia Clases = ClaseLicencia.Ninguna,
    bool Anulada = false,
    string? MotivoAnulacion = null)
{
    public DateTime? FechaEstadoActual { get; init; }

    public DateOnly? FechaCierre { get; init; }

    public DateTime? FechaImpresion { get; init; }
}

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
    DateOnly? Hasta = null,
    IReadOnlyList<Guid>? Ids = null);
