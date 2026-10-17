using Microsoft.Extensions.Logging;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Ews;
using Sgl.Application.Solicitudes;
using Sgl.Domain;

namespace Sgl.Infrastructure.Carpetas;

/// <summary>Settings section "Certificados". Everything is OFF by default: nothing is mailed until it is switched on.</summary>
public sealed class CertificadosOptions
{
    public const string SectionName = "Certificados";

    /// <summary>Master switch of the mail to the Director. False (default) = the request is only registered.</summary>
    public bool EnvioCorreoActivo { get; set; }
}

/// <summary>The mail could not be sent. The message is generic on purpose: the cause is in the log.</summary>
public sealed class EnvioCorreoFallidoException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Mails the Director when a certificate / oficio request is created, with the generated .docx attached.
/// Safe by default: gated by <c>Certificados:EnvioCorreoActivo</c> and by the Peticiones TEST MODE
/// (<c>Peticion:TestModeEmail</c>: the mail is redirected to the test mailbox, marked, and reported as NOT sent to
/// the Director). It returns true only when the Director's own address was mailed. Information logs carry ids only:
/// no addresses, names or bodies.
/// </summary>
public sealed class NotificadorCertificadoCorreo(
    IMailSender mail,
    AppOptions peticion,
    CertificadosOptions opciones,
    IGeneradorDocumentoCertificado generador,
    ILogger<NotificadorCertificadoCorreo> logger) : INotificadorCertificado
{
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string ErrorGenerico = "No se pudo enviar el correo al director.";

    public async Task<bool> EnviarAsync(NotificacionCertificado notificacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notificacion);

        if (!opciones.EnvioCorreoActivo)
        {
            return Omitido(notificacion, "envío desactivado");
        }

        var destino = notificacion.Firmantes.DirectorCorreo?.Trim();
        if (string.IsNullOrEmpty(destino))
        {
            return Omitido(notificacion, "sin correo del Director");
        }

        if (!mail.IsConfigured || mail is not IMailSenderConAdjuntos transporte)
        {
            return Omitido(notificacion, "correo no configurado");
        }

        var adjuntos = Adjuntar(notificacion, out var sinAdjunto);
        var asunto = $"Solicitud de certificado – {Etiqueta(notificacion.Tipo)}";
        var cuerpo = Cuerpo(notificacion, sinAdjunto);

        var modoPrueba = !string.IsNullOrWhiteSpace(peticion.TestModeEmail);
        var envioA = destino;
        if (modoPrueba)
        {
            asunto = $"[PRUEBA] {asunto}";
            cuerpo = string.Join("\n", "*** CORREO DE PRUEBA - NO SE ENVIO AL DIRECTOR ***", $"Habria ido a: {destino}", new string('-', 60), string.Empty, string.Empty) + cuerpo;
            envioA = peticion.TestModeEmail!.Trim();
        }

        try
        {
            await transporte.SendAsync(envioA, asunto, cuerpo, adjuntos, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falló el correo de la solicitud de certificado {CertificadoId}.", notificacion.CertificadoId);
            throw new EnvioCorreoFallidoException(ErrorGenerico, ex);
        }

        if (modoPrueba)
        {
            logger.LogInformation(
                "Solicitud de certificado {CertificadoId}: correo de prueba enviado al buzón de prueba (no al Director).",
                notificacion.CertificadoId);
            return false;
        }

        logger.LogInformation("Solicitud de certificado {CertificadoId}: correo enviado al Director.", notificacion.CertificadoId);
        return true;
    }

    private bool Omitido(NotificacionCertificado n, string motivo)
    {
        logger.LogInformation(
            "Solicitud de certificado {CertificadoId} ({Tipo}) registrada; correo al Director omitido: {Motivo}.",
            n.CertificadoId, n.Tipo, motivo);
        return false;
    }

    /// <summary>The generated document, or none when its data is not complete yet (e.g. the renewal year).</summary>
    private IReadOnlyList<AdjuntoCorreo> Adjuntar(NotificacionCertificado n, out bool sinAdjunto)
    {
        sinAdjunto = false;
        try
        {
            var doc = generador.Generar(DatosCertificado.Desde(n), n.Firmantes);
            return [new AdjuntoCorreo(doc.NombreArchivo, DocxContentType, doc.Contenido)];
        }
        catch (DomainException ex)
        {
            sinAdjunto = true;
            logger.LogInformation(
                "Solicitud de certificado {CertificadoId}: el documento aún no se puede generar ({Motivo}); el correo va sin adjunto.",
                n.CertificadoId, ex.Message);
            return [];
        }
    }

    private static string Etiqueta(TipoCertificado tipo) => tipo switch
    {
        TipoCertificado.SinCarpeta => "sin carpeta",
        TipoCertificado.RegistroCivil => "Registro Civil",
        _ => "Oficio",
    };

    private static string Cuerpo(NotificacionCertificado n, bool sinAdjunto)
    {
        var persona = $"{n.NombreCompleto}, RUT {n.Rut}";
        var solicitud = n.Tipo switch
        {
            TipoCertificado.SinCarpeta =>
                $"Se solicita la elaboración del certificado de acreditación de licencia de conducir de {persona}, " +
                "cuya carpeta física no fue posible localizar.",
            TipoCertificado.RegistroCivil =>
                $"Se solicita la elaboración del certificado para el Registro Civil de {persona}, " +
                "para rectificar la información de su hoja de vida del conductor.",
            _ =>
                $"Se solicita la elaboración del oficio al Registro Civil para eliminar una anotación " +
                $"en la hoja de vida del conductor de {persona}.",
        };
        var adjunto = sinAdjunto
            ? "El documento no se pudo adjuntar todavía (faltan datos); se generará desde el sistema."
            : "Se adjunta el documento para su revisión y firma.";
        return string.Join("\n\n", "Estimado/a:", solicitud, adjunto, "Saluda atentamente,\nSistema de Gestión de Licencias") + "\n";
    }
}
