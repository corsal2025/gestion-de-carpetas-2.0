using Microsoft.Extensions.Logging;
using Sgl.Application.Solicitudes;

namespace Sgl.Infrastructure.Carpetas;

/// <summary>
/// Test-mode adapter until the real mail (EWS) is configured: sends nothing and says so in the log. It always returns
/// false, so <c>EnviadoADirectorEn</c> stays empty. Replace its registration in DependencyInjection with the real one.
/// </summary>
public sealed class CertificadoSinCorreo(ILogger<CertificadoSinCorreo> logger) : INotificadorCertificado
{
    public Task<bool> EnviarAsync(NotificacionCertificado notificacion, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Solicitud de certificado {Certificado} ({Tipo}) registrada sin correo (modo prueba: no se envió nada al director).",
            notificacion.CertificadoId,
            notificacion.Tipo);
        return Task.FromResult(false);
    }
}
