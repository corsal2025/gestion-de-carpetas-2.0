using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Solicitudes;

internal sealed record EnvioCertificadoResultado(bool Enviado, string? Error)
{
    public static EnvioCertificadoResultado NoEnviado { get; } = new(false, null);
}

/// <summary>
/// Hands a freshly created certificate request to the notification port and records the sending when (and only when)
/// the adapter really sent it. The port is called outside the caller's write gate (a mail can be slow); the gate is
/// taken again only to store <c>EnviadoADirectorEn</c>. A failing adapter never undoes the request.
/// </summary>
internal sealed class EnvioCertificadoDirector(
    ICarpetaRepository carpetas,
    ICertificadoRepository certificados,
    IFirmantesRepository firmantes,
    INotificadorCertificado? notificador,
    TimeProvider clock)
{
    public const string ErrorGenerico = "No se pudo enviar el correo al director.";

    public async Task<EnvioCertificadoResultado> EnviarAsync(Guid certificadoId, SemaphoreSlim gate, CancellationToken ct)
    {
        if (notificador is null)
        {
            return EnvioCertificadoResultado.NoEnviado;
        }

        var notificacion = await CargarNotificacionAsync(certificadoId, gate, ct);
        bool enviado;
        try
        {
            enviado = await notificador.EnviarAsync(notificacion, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The adapter logs the cause; the UI only gets a generic text (no internals, addresses or bodies).
            return new EnvioCertificadoResultado(false, ErrorGenerico);
        }

        if (!enviado)
        {
            return EnvioCertificadoResultado.NoEnviado;
        }

        await gate.WaitAsync(ct);
        try
        {
            var certificado = await certificados.GetAsync(certificadoId, ct)
                ?? throw new NotFoundException($"Certificado {certificadoId} no encontrado.");
            certificado.RegistrarEnvioADirector(clock.GetUtcNow().UtcDateTime);
            await carpetas.SaveChangesAsync(ct);
            return new EnvioCertificadoResultado(true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            carpetas.DescartarCambios();
            return new EnvioCertificadoResultado(true, "El correo se envió pero no se pudo registrar el envío.");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<NotificacionCertificado> CargarNotificacionAsync(Guid certificadoId, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var certificado = await certificados.GetAsync(certificadoId, ct)
                ?? throw new NotFoundException($"Certificado {certificadoId} no encontrado.");
            var carpeta = await carpetas.GetAsync(certificado.CarpetaId, ct)
                ?? throw new NotFoundException($"Carpeta {certificado.CarpetaId} no encontrada.");
            return new NotificacionCertificado(
                certificado.Id,
                carpeta.Id,
                certificado.Tipo,
                certificado.Origen,
                carpeta.Ciudadano.Rut.Formatted,
                carpeta.Ciudadano.NombreCompleto.ToUpperInvariant(),
                carpeta.Sede,
                certificado.SolicitadoPor,
                certificado.SolicitadoEn,
                certificado.NumeroOficio,
                certificado.Parametros,
                await firmantes.ObtenerAsync(ct));
        }
        finally
        {
            gate.Release();
        }
    }
}
