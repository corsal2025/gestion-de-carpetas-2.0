using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>What the automatic request mail to Dirección and Secretaría carries.</summary>
public sealed record PeticionCertificado(
    Guid CarpetaId,
    string Rut,
    string NombreCompleto,
    Sede Sede,
    DateOnly FechaCitacion,
    string SolicitadoPor,
    DateTime FechaUtc);

/// <summary>
/// Port for the request mail. The adapter (SMTP / Outlook / Graph) lives in Infrastructure; until it is configured a
/// no-mail adapter records nothing and returns false, so the request is still stored and audited.
/// </summary>
public interface INotificadorPeticion
{
    /// <returns>True when the mail was actually sent.</returns>
    Task<bool> EnviarAsync(PeticionCertificado peticion, CancellationToken ct = default);
}
