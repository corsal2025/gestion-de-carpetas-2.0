using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>Business options bound from the <c>Licencias</c> configuration section.</summary>
public sealed class LicenciasOptions
{
    public const string SectionName = "Licencias";

    /// <summary>From this date on, a new carpeta must declare at least one license class. Null disables the rule.</summary>
    public DateOnly? ClaseObligatoriaDesde { get; init; }

    public bool ClaseEsObligatoria(DateOnly hoy) => ClaseObligatoriaDesde is { } desde && hoy >= desde;
}

/// <summary>An open carpeta (no final decision) of a citizen.</summary>
public sealed record CarpetaAbiertaDto(
    Guid Id,
    Sede Sede,
    DateOnly FechaCitacion,
    EstadoCarpeta Estado,
    ClaseLicencia Clases);

/// <summary>Result of checking a new registration against the citizen's open carpetas.</summary>
public sealed record VerificacionDuplicadoDto(
    IReadOnlyList<CarpetaAbiertaDto> Bloqueantes,
    IReadOnlyList<CarpetaAbiertaDto> Advertencias)
{
    public static readonly VerificacionDuplicadoDto Vacia = new([], []);

    public bool Bloquea => Bloqueantes.Count > 0;

    public bool RequiereConfirmacion => !Bloquea && Advertencias.Count > 0;

    /// <summary>
    /// An open carpeta sharing a class blocks; an open carpeta without class or with a
    /// different class only warns.
    /// </summary>
    public static VerificacionDuplicadoDto Clasificar(IReadOnlyList<CarpetaAbiertaDto> abiertas, ClaseLicencia clases)
    {
        ArgumentNullException.ThrowIfNull(abiertas);
        var bloqueantes = abiertas.Where(c => ClasesLicencia.Overlaps(c.Clases, clases)).ToList();
        var advertencias = abiertas.Where(c => !ClasesLicencia.Overlaps(c.Clases, clases)).ToList();
        return new VerificacionDuplicadoDto(bloqueantes, advertencias);
    }
}

/// <summary>Raised when a registration collides with an open carpeta of the same citizen.</summary>
public sealed class CarpetaDuplicadaException(VerificacionDuplicadoDto verificacion)
    : Exception(verificacion.Bloquea
        ? "El ciudadano ya tiene una carpeta abierta con la misma clase de licencia."
        : "El ciudadano ya tiene una carpeta abierta. Confirme para continuar.")
{
    public VerificacionDuplicadoDto Verificacion { get; } = verificacion;

    public bool Bloqueante => Verificacion.Bloquea;
}

public sealed record ListarDuplicadosQuery(
    string? Texto = null,
    Sede? Sede = null,
    int Skip = 0,
    int? Take = null)
{
    public ListarDuplicadosQuery Normalized() => this with
    {
        Texto = string.IsNullOrWhiteSpace(Texto) ? null : Texto.Trim(),
        Skip = Math.Max(0, Skip),
        Take = Take is { } t ? Math.Max(0, t) : null,
    };
}

/// <summary>One citizen with more than one open carpeta, pending review.</summary>
public sealed record DuplicadoGrupoDto(Guid CiudadanoId, string Rut, string NombreCompleto, int CarpetasAbiertas);

/// <summary>Full picture of one open carpeta, used to compare a citizen's duplicates side by side.</summary>
public sealed record CarpetaDuplicadoDetalleDto(
    Guid Id,
    Sede Sede,
    DateOnly FechaCitacion,
    DateOnly? FechaSubida,
    EstadoCarpeta Estado,
    Decision Decision,
    ClaseLicencia Clases,
    string? TipoTramite,
    string? FechaUltimaCarpeta,
    string? CajaArchivo,
    bool PendienteBusqueda,
    DateTime? PendienteBusquedaDesde,
    string? IdoneidadMoral,
    string? Comuna,
    DateTime? UltimoMovimientoFecha,
    string? UltimoMovimientoPor);

public sealed record DuplicadoSearch(string? RutFragment, string? NameFragment, Sede? Sede, int Skip, int? Take);

/// <summary>Persistence port for the duplicate review queue.</summary>
public interface IDuplicadoRepository
{
    Task<(IReadOnlyList<DuplicadoGrupoDto> Items, int Total)> SearchAsync(DuplicadoSearch search, CancellationToken ct = default);

    Task<IReadOnlyList<CarpetaAbiertaDto>> GetCarpetasAbiertasAsync(Guid ciudadanoId, CancellationToken ct = default);

    Task<IReadOnlyList<CarpetaDuplicadoDetalleDto>> GetDetalleCarpetasAbiertasAsync(Guid ciudadanoId, CancellationToken ct = default);

    Task<RevisionDuplicado?> GetRevisionAsync(Guid ciudadanoId, CancellationToken ct = default);

    Task AddRevisionAsync(RevisionDuplicado revision, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
