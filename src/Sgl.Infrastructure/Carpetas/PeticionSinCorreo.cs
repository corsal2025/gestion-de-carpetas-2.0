using Microsoft.Extensions.Logging;
using Sgl.Application.Carpetas;

namespace Sgl.Infrastructure.Carpetas;

/// <summary>
/// Placeholder adapter until the Dirección / Secretaría mail is configured: sends nothing and says so in the log.
/// Replace its registration in DependencyInjection with the real mail adapter.
/// </summary>
public sealed class PeticionSinCorreo(ILogger<PeticionSinCorreo> logger) : INotificadorPeticion
{
    public Task<bool> EnviarAsync(PeticionCertificado peticion, CancellationToken ct = default)
    {
        logger.LogInformation("Petición registrada sin correo (envío automático aún no configurado): carpeta {Carpeta}.", peticion.CarpetaId);
        return Task.FromResult(false);
    }
}
