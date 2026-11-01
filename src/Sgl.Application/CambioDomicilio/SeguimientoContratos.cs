using Sgl.Domain;

namespace Sgl.Application.CambioDomicilio;

/// <summary>Colour of the deadline in «Seguimiento»: green, yellow (3 days or less left, the last day included), red (overdue).</summary>
public enum PlazoColor
{
    Verde = 1,
    Amarillo = 2,
    Rojo = 3,
}

public enum FiltroSeguimiento
{
    Todas = 0,
    VencenHoy = 1,
    Vencidas = 2,

    /// <summary>Only carpetas whose every request mail was a TEST send (the comuna received nothing).</summary>
    SoloPrueba = 3,
}

/// <summary>A saliente in «Solicitado» as the repository reads it (the deadline is added by <see cref="SeguimientoService"/>).</summary>
/// <param name="Envios">Request mails (first one and reminders) that went out for it.</param>
/// <param name="SoloPrueba">It has mails and every one of them was a TEST send (nothing reached the comuna).</param>
/// <param name="EnvioPantallazoId">Mail of the carpeta that already has its screenshot (the latest one); null if none.</param>
/// <param name="PantallazoDePrueba">That screenshot belongs to a TEST mail.</param>
public sealed record SolicitadaDto(
    Guid Id,
    string Rut,
    string NombreCompleto,
    string? Comuna,
    DateOnly FechaCitacion,
    int Envios,
    DateTime? UltimoEnvioEn,
    bool SoloPrueba,
    Guid? EnvioPantallazoId,
    bool PantallazoDePrueba);

/// <summary>One row of «Seguimiento»: a saliente waiting for the other comuna, with its deadline.</summary>
/// <param name="Vence">Citation date + business days (Monday to Friday). It never moves, not even when the mail is repeated.</param>
/// <param name="DiasRestantes">Calendar days until <paramref name="Vence"/>; negative when overdue.</param>
public sealed record SeguimientoDto(
    Guid Id,
    string Rut,
    string NombreCompleto,
    string? Comuna,
    DateOnly FechaCitacion,
    DateOnly Vence,
    int DiasRestantes,
    PlazoColor Plazo,
    int Envios,
    DateTime? UltimoEnvioEn,
    bool SoloPrueba,
    Guid? EnvioPantallazoId,
    bool PantallazoDePrueba)
{
    public bool TienePantallazo => EnvioPantallazoId is not null;

    public bool Vencida => DiasRestantes < 0;

    public bool Cumple(FiltroSeguimiento filtro) => filtro switch
    {
        FiltroSeguimiento.VencenHoy => DiasRestantes == 0,
        FiltroSeguimiento.Vencidas => Vencida,
        FiltroSeguimiento.SoloPrueba => SoloPrueba,
        _ => true,
    };
}

public sealed record PersonaEnvioDto(Guid Id, string Rut, string NombreCompleto);

/// <summary>A mail that went out and still has no screenshot: one row per mail, not per person.</summary>
public sealed record PantallazoPendienteDto(
    Guid EnvioId,
    TipoEnvioCorreoComuna Tipo,
    string Comuna,
    string Destinatarios,
    string Asunto,
    DateTime EnviadoEn,
    bool EsPrueba,
    IReadOnlyList<PersonaEnvioDto> Personas);

/// <summary>Figures for the stage navigation: salientes waiting, overdue ones and mails without screenshot.</summary>
public sealed record ResumenSeguimiento(int Solicitadas, int Vencidas, int PantallazosPendientes);

/// <summary>A stored screenshot ready to be served.</summary>
public sealed record PantallazoArchivoDto(Stream Contenido, string ContentType);

/// <summary>Read side of «Seguimiento» and «Pantallazos pendientes». Implemented in Infrastructure over the circuit's shared DbContext.</summary>
public interface ISeguimientoRepository
{
    /// <summary>Open, not annulled salientes in «Solicitado».</summary>
    Task<IReadOnlyList<SolicitadaDto>> ListarSolicitadasAsync(CancellationToken ct = default);

    /// <summary>Request mails (first or repeated) that were sent for real or as a test, have no screenshot and left something to capture.</summary>
    Task<IReadOnlyList<PantallazoPendienteDto>> ListarPantallazosPendientesAsync(CancellationToken ct = default);

    Task<int> ContarPantallazosPendientesAsync(CancellationToken ct = default);

    /// <summary>The mail (tracked) a screenshot is attached to.</summary>
    Task<EnvioCorreoComuna?> ObtenerEnvioAsync(Guid envioId, CancellationToken ct = default);

    /// <summary>Stored file name of the screenshot of a mail; null when the mail has none or does not exist.</summary>
    Task<string?> ArchivoPantallazoAsync(Guid envioId, CancellationToken ct = default);

    /// <summary>Mail of the carpeta that has a screenshot (the latest one); null when none.</summary>
    Task<Guid?> EnvioConPantallazoDeCarpetaAsync(Guid carpetaId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    void DescartarCambios();
}

/// <summary>
/// Disk store of the pasted screenshots (port; adapter in Infrastructure). The file is an IMAGE of the sent mail: only PNG and
/// JPEG are accepted, checked by their signature and not by the name the browser sends.
/// </summary>
public interface IAlmacenPantallazos
{
    /// <summary>Largest accepted size, from <c>CambioDomicilio:PantallazoMaxKB</c>.</summary>
    long MaxBytes { get; }

    /// <summary>
    /// Validates and stores the image as <c>{envioId}.png</c> or <c>{envioId}.jpg</c> and returns that name. A rejected image
    /// leaves nothing on disk. Throws <see cref="DomainException"/> when the file is not a PNG/JPEG or exceeds <see cref="MaxBytes"/>.
    /// </summary>
    Task<string> GuardarAsync(Guid envioId, Stream contenido, CancellationToken ct = default);

    /// <summary>Opens the stored file of that mail; null when it is not there. The name must be the one this store produced for the mail.</summary>
    Task<Stream?> AbrirAsync(Guid envioId, string archivo, CancellationToken ct = default);

    /// <summary>Deletes a stored file; a missing file is not an error.</summary>
    void Eliminar(Guid envioId, string archivo);
}
