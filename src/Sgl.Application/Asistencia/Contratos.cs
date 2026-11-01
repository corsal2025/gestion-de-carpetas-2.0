using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Asistencias;

/// <summary>One attendance row as the screen shows it. <see cref="Hora"/> is Chile local time.</summary>
public sealed record AsistenciaDto(
    Guid Id, TipoAsistencia Tipo, string Rut, string? Nombre, Sede Sede, DateOnly Fecha, DateTime Hora, string Autor, Guid? CarpetaId);

/// <summary>Today's appointment of the searched person.</summary>
public sealed record CitaHoyDto(Guid CarpetaId, TimeOnly? Hora, Sede Sede, string? Tramite, Bandeja Bandeja);

/// <summary>Everything the screen shows after searching one RUT.</summary>
public sealed record BusquedaAsistenciaDto(
    string Rut,
    bool Existe,
    string? Nombre,
    CitaHoyDto? CitaHoy,
    IReadOnlyList<ResultadoBusquedaGlobalDto> Otras,
    AsistenciaDto? Marcada,
    AsistenciaDto? PorAgregar)
{
    /// <summary>The person already has an open carpeta: "Agregar como extra" would duplicate it and is refused.</summary>
    public bool TieneCarpetaAbierta { get; init; }
}

/// <summary>Counters of the day: appointments of today (no extras, no cancelled), who came, extras.</summary>
public sealed record ContadoresAsistenciaDto(int AgendadasHoy, int Vinieron, int Extras);

public sealed record ResumenAsistenciaDto(
    ContadoresAsistenciaDto Contadores, IReadOnlyList<AsistenciaDto> Marcados, IReadOnlyList<AsistenciaDto> PorAgregar);

/// <summary><paramref name="Nombre"/> is only needed when the RUT is not in the system yet.</summary>
public sealed record AgregarExtraCommand(string Rut, string? Nombre, Sede Sede, string Tramite, string Autor);

/// <summary>Name, today's appointment, today's mark and pending "por agregar" row of one RUT.</summary>
public sealed record ContextoRutDto(string? Nombre, bool Existe, CitaHoyDto? CitaHoy, AsistenciaDto? Marcada, AsistenciaDto? PorAgregar);

/// <summary>Trámites offered for an extra; the ones that match the agenda also fill <see cref="TramiteAgenda"/>.</summary>
public static class TramitesExtra
{
    public static readonly IReadOnlyList<(string Texto, TramiteAgenda? Agenda)> Opciones =
    [
        ("Renovación", TramiteAgenda.Renovacion),
        ("Primera vez, Extensión", TramiteAgenda.PrimeraVezExtension),
        ("Duplicado", null),
        ("Cambio de domicilio", null),
        ("Canje", null),
        ("Otro", null),
    ];

    public static TramiteAgenda? AgendaDe(string? texto) =>
        Opciones.FirstOrDefault(o => string.Equals(o.Texto, texto?.Trim(), StringComparison.OrdinalIgnoreCase)).Agenda;
}

/// <summary>Persistence port of the attendance record. It shares the circuit's DbContext with the carpeta repository.</summary>
public interface IAsistenciaRepository
{
    Task<Asistencia?> GetAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Asistencia asistencia, CancellationToken ct = default);

    Task<ContextoRutDto> GetContextoAsync(string rut, DateOnly hoy, CancellationToken ct = default);

    /// <summary>Tracked rows still pending "por agregar" of that RUT (to annul them when resolved).</summary>
    Task<IReadOnlyList<Asistencia>> GetPorAgregarVigentesAsync(string rut, CancellationToken ct = default);

    Task<ResumenAsistenciaDto> GetResumenAsync(DateOnly hoy, CancellationToken ct = default);
}
