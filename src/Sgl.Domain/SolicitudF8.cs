namespace Sgl.Domain;

public enum EstadoSolicitudF8
{
    /// <summary>Requested, waiting for the F8 to be generated and uploaded.</summary>
    Solicitado = 1,

    /// <summary>The F8 was generated and uploaded; the carpeta moved to SubidaConF8.</summary>
    Subido = 2,

    /// <summary>The F8 could not be generated; the carpeta moved to Crear certificado and a certificate was opened.</summary>
    NoSeGenero = 3,
}

/// <summary>
/// A request to generate and upload an F8 for a carpeta (module "F8"). At most one request per carpeta can be open
/// (<see cref="EstadoSolicitudF8.Solicitado"/>) at a time; that rule is enforced by the service and by a unique
/// filtered index.
/// </summary>
public sealed class SolicitudF8
{
    public const int MaxObservacionLength = 500;

    private static readonly EstadoCarpeta[] EstadosOrigen =
    [
        EstadoCarpeta.NoExisteCarpeta,
        EstadoCarpeta.SeEncuentraEnArchivos,
        EstadoCarpeta.SeEncuentraEnOf43,
        EstadoCarpeta.CambioDomicilio,
        EstadoCarpeta.CambioDomicilioSolicitado,
    ];

    public Guid Id { get; private set; }

    public Guid CarpetaId { get; private set; }

    /// <summary>State the carpeta was in when the F8 was requested.</summary>
    public EstadoCarpeta EstadoOrigen { get; private set; }

    public string SolicitadoPor { get; private set; } = string.Empty;

    /// <summary>UTC instant of the request; shown in Chile time.</summary>
    public DateTime SolicitadoEn { get; private set; }

    public EstadoSolicitudF8 Estado { get; private set; }

    public string? ResueltoPor { get; private set; }

    public DateTime? ResueltoEn { get; private set; }

    public string? Observacion { get; private set; }

    public bool EstaAbierta => Estado == EstadoSolicitudF8.Solicitado;

    private SolicitudF8()
    {
    }

    /// <summary>States from which an F8 can be requested for a carpeta.</summary>
    public static bool PuedeSolicitarseDesde(EstadoCarpeta estado) => EstadosOrigen.Contains(estado);

    public static SolicitudF8 Solicitar(Guid carpetaId, EstadoCarpeta estadoOrigen, string autor, DateTime now)
    {
        if (carpetaId == Guid.Empty)
        {
            throw new DomainException("La carpeta es obligatoria.");
        }
        if (!PuedeSolicitarseDesde(estadoOrigen))
        {
            throw new DomainException("Solo se puede solicitar un F8 de una carpeta en Archivos, Of. 43, No existe carpeta o Cambio de domicilio.");
        }

        return new SolicitudF8
        {
            Id = Guid.NewGuid(),
            CarpetaId = carpetaId,
            EstadoOrigen = estadoOrigen,
            SolicitadoPor = Autores.Requerir(autor),
            SolicitadoEn = now,
            Estado = EstadoSolicitudF8.Solicitado,
        };
    }

    public void MarcarSubido(string autor, DateTime now, string? observacion = null) =>
        Resolver(EstadoSolicitudF8.Subido, autor, now, observacion);

    public void MarcarNoSeGenero(string autor, DateTime now, string? observacion = null) =>
        Resolver(EstadoSolicitudF8.NoSeGenero, autor, now, observacion);

    private void Resolver(EstadoSolicitudF8 destino, string autor, DateTime now, string? observacion)
    {
        if (!EstaAbierta)
        {
            throw new DomainException("La solicitud de F8 ya fue resuelta.");
        }
        var usuario = Autores.Requerir(autor);
        var nota = string.IsNullOrWhiteSpace(observacion) ? null : observacion.Trim();
        if (nota?.Length > MaxObservacionLength)
        {
            throw new DomainException($"La observación excede {MaxObservacionLength} caracteres.");
        }

        Estado = destino;
        ResueltoPor = usuario;
        ResueltoEn = now;
        Observacion = nota;
    }
}

/// <summary>Shared author validation for the request entities (same rule as the carpeta audit trail).</summary>
internal static class Autores
{
    public static string Requerir(string? autor)
    {
        if (string.IsNullOrWhiteSpace(autor))
        {
            throw new DomainException("El autor del cambio es obligatorio.");
        }

        var trimmed = autor.Trim();
        return trimmed.Length > Carpeta.MaxUsuarioLength
            ? throw new DomainException($"El autor excede {Carpeta.MaxUsuarioLength} caracteres.")
            : trimmed;
    }
}
