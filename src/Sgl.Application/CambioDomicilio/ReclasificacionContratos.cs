using Sgl.Domain;

namespace Sgl.Application.CambioDomicilio;

/// <summary>
/// One carpeta waiting in «Reclasificación»: it just entered «Cambio de domicilio» and a person still has to confirm that the
/// classification is right.
/// </summary>
/// <param name="Sentido">Saliente (a person of our agenda) or entrante (another comuna asked by email).</param>
/// <param name="FechaCitacion">Appointment date of a saliente; date the email arrived for an entrante.</param>
/// <param name="LlegadaEn">Chile date on which the carpeta entered the stage.</param>
public sealed record ReclasificacionDto(
    Guid Id,
    SentidoCambioDomicilio Sentido,
    string Rut,
    string NombreCompleto,
    string? Comuna,
    Sede Sede,
    DateOnly FechaCitacion,
    DateOnly LlegadaEn,
    string? FechaUltimaCarpeta);

/// <summary>Read side of «Reclasificación». Implemented in Infrastructure over the circuit's shared DbContext.</summary>
public interface IReclasificacionRepository
{
    /// <summary>Open, not annulled carpetas in «Cambio de domicilio» still pending of confirmation, oldest first.</summary>
    Task<IReadOnlyList<ReclasificacionDto>> ListarAsync(CancellationToken ct = default);

    Task<int> ContarAsync(CancellationToken ct = default);
}
