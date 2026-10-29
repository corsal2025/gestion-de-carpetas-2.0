using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace PeticionCambioDomicilio.Ews;

public interface IMailSender
{
    /// <summary>true si el transporte está configurado y listo para enviar.</summary>
    bool IsConfigured { get; }

    /// <param name="toAddress">Una o varias direcciones separadas por coma o punto y coma (un solo correo, todas en "Para").</param>
    Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken);
}

/// <summary>Un archivo adjunto de un correo.</summary>
public sealed record AdjuntoCorreo(string Nombre, string ContentType, byte[] Contenido);

/// <summary>Transporte que además sabe adjuntar archivos (lo usa el aviso de certificados al Director).</summary>
public interface IMailSenderConAdjuntos : IMailSender
{
    Task SendAsync(string toAddress, string subject, string body, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken cancellationToken);
}

/// <summary>
/// Transporte SOAP mínimo contra el EWS on-premises de Exchange. Solo envía correo
/// (CreateItem / SendAndSaveCopy). Basic auth sobre TLS.
/// Portado de LicenciasCarpetas/CambioDomicilio/Ews/* recortado a lo que esta app usa.
///
/// Reintenta SOLO fallos transitorios (servidor sobrecargado o caído un momento) y cortes de
/// red. Un 401/403 corta al primer intento y lanza <see cref="EwsAuthException"/>: reintentar
/// credenciales rechazadas solo acelera el bloqueo de la cuenta de dominio.
/// </summary>
public sealed class EwsMailSender : IMailSenderConAdjuntos, IDisposable
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace T = "http://schemas.microsoft.com/exchange/services/2006/types";
    private static readonly XNamespace M = "http://schemas.microsoft.com/exchange/services/2006/messages";
    private const int MaxAttempts = 4;

    private readonly EwsOptions? _ews;
    private readonly string? _sendAs;
    private readonly HttpClient? _http;
    private readonly string? _motivoNoConfigurado;

    public EwsMailSender(AppOptions options)
    {
        _ews = options.Ews;
        _sendAs = string.IsNullOrWhiteSpace(options.Ews?.SendAsAddress) ? null : options.Ews!.SendAsAddress!.Trim();
        if (_ews is { Url: not null, Username: not null, Password: not null } && !EsUrlHttps(_ews.Url))
        {
            // Basic auth sobre http plano expone usuario y clave de dominio: no se envía nada.
            _motivoNoConfigurado = "Peticion:Ews:Url debe empezar con https:// (con http las credenciales viajarían sin cifrar).";
        }
        else if (_ews is { Url: not null, Username: not null, Password: not null })
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_ews.Username}:{_ews.Password}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public bool IsConfigured => _http is not null;

    /// <summary>true si la URL es absoluta y usa https.</summary>
    public static bool EsUrlHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken) =>
        SendAsync(toAddress, subject, body, [], cancellationToken);

    public async Task SendAsync(
        string toAddress, string subject, string body, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken cancellationToken)
    {
        if (_http is null || _ews?.Url is null)
        {
            throw new EwsSendException(
                _motivoNoConfigurado ?? "Falta configurar Peticion:Ews (Url/Username/Password) en appsettings.Local.json.");
        }

        var soap = BuildSendMailRequest(toAddress, subject, body, _sendAs, adjuntos);
        var response = await PostAsync(_ews.Url, soap, cancellationToken);
        EnsureSuccess(response);
    }

    private async Task<XDocument> PostAsync(string url, string soap, CancellationToken cancellationToken)
    {
        var attempt = 0;
        var delay = TimeSpan.FromSeconds(2);

        while (true)
        {
            attempt++;
            try
            {
                using var content = new StringContent(soap, Encoding.UTF8, "text/xml");
                using var response = await _http!.PostAsync(url, content, cancellationToken);

                switch (EwsRetryPolicy.Classify(response.StatusCode))
                {
                    case EwsAction.Accept:
                        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                        return EwsXml.Parsear(xml);

                    case EwsAction.FailAuth:
                        throw new EwsAuthException(
                            $"Exchange rechazó las credenciales (HTTP {(int)response.StatusCode}). " +
                            "Revisá Peticion:Ews:Username y Password en appsettings.Local.json. " +
                            "El usuario que funciona tiene formato servervalpo\\cambiodedomicilio. " +
                            "No repitas el envío a ciegas: cada rechazo suma al bloqueo de la cuenta.");

                    case EwsAction.Retry when attempt < MaxAttempts:
                        await Task.Delay(delay, cancellationToken);
                        delay *= 2;
                        continue;

                    case EwsAction.Retry:
                        throw new EwsSendException(
                            $"Exchange no respondió tras {MaxAttempts} intentos (último: HTTP {(int)response.StatusCode}). " +
                            "Probá de nuevo en unos minutos.");

                    default: // FailPermanent
                        // El cuerpo de la respuesta no se incluye: puede traer datos internos del servidor.
                        throw new EwsSendException(
                            $"Exchange devolvió HTTP {(int)response.StatusCode}.");
                }
            }
            catch (Exception ex) when (IsNetwork(ex) && attempt < MaxAttempts)
            {
                await Task.Delay(delay, cancellationToken);
                delay *= 2;
            }
            catch (Exception ex) when (IsNetwork(ex))
            {
                throw new EwsSendException(
                    $"No se pudo alcanzar Exchange tras {MaxAttempts} intentos: {ex.Message}. " +
                    "¿Hay red municipal / VPN?", ex);
            }
        }
    }

    /// <summary>Fallos de transporte (DNS, conexión, timeout) que sí conviene reintentar.</summary>
    private static bool IsNetwork(Exception ex) =>
        ex is HttpRequestException or System.Net.Sockets.SocketException
            || (ex is TaskCanceledException tce && tce.InnerException is TimeoutException);

    private static void EnsureSuccess(XDocument document)
    {
        var responseMessage = document.Descendants()
            .FirstOrDefault(e => e.Name.Namespace == M && e.Name.LocalName.EndsWith("ResponseMessage"));

        if (responseMessage?.Attribute("ResponseClass")?.Value == "Error")
        {
            var code = responseMessage.Element(M + "ResponseCode")?.Value ?? "desconocido";
            var text = responseMessage.Element(M + "MessageText")?.Value;

            if (code is "ErrorAccessDenied" or "ErrorImpersonateUserDenied" or "ErrorSendAsDenied")
            {
                throw new EwsAuthException(
                    $"EWS rechazó el envío por permisos ({code}{(text is null ? "" : $" — {text}")}). " +
                    "Si usás SendAsAddress, la cuenta autenticada necesita permiso \"Send As\" sobre ese buzón.");
            }

            throw new EwsSendException($"EWS CreateItem falló: {code}{(text is null ? "" : $" — {text}")}");
        }
    }

    public static string BuildSendMailRequest(
        string toAddress, string subject, string body, string? sendAsAddress, IReadOnlyList<AdjuntoCorreo>? adjuntos = null)
    {
        var envelope = new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", Soap),
            new XAttribute(XNamespace.Xmlns + "t", T),
            new XAttribute(XNamespace.Xmlns + "m", M),
            new XElement(Soap + "Header",
                new XElement(T + "RequestServerVersion", new XAttribute("Version", "Exchange2013_SP1"))),
            new XElement(Soap + "Body",
                new XElement(M + "CreateItem",
                    new XAttribute("MessageDisposition", "SendAndSaveCopy"),
                    new XElement(M + "SavedItemFolderId",
                        new XElement(T + "DistinguishedFolderId", new XAttribute("Id", "sentitems"))),
                    new XElement(M + "Items",
                        new XElement(T + "Message",
                            new XElement(T + "Subject", TextoSeguro.SinControl(subject)),
                            new XElement(T + "Body", new XAttribute("BodyType", "Text"), TextoSeguro.SinControl(body)),
                            // El orden importa: Attachments va después de Body y antes de ToRecipients.
                            adjuntos is { Count: > 0 }
                                ? new XElement(T + "Attachments",
                                    adjuntos.Select(a => new XElement(T + "FileAttachment",
                                        new XElement(T + "Name", TextoSeguro.SinControl(a.Nombre)),
                                        new XElement(T + "ContentType", a.ContentType),
                                        new XElement(T + "Content", Convert.ToBase64String(a.Contenido)))))
                                : null,
                            new XElement(T + "ToRecipients",
                                toAddress.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                    .Select(a => new XElement(T + "Mailbox",
                                        new XElement(T + "EmailAddress", TextoSeguro.SinControl(a))))),
                            // From solo cuando se envia como otro buzon (requiere permiso Send As).
                            // El orden importa: EWS exige ToRecipients antes que From.
                            sendAsAddress is null
                                ? null
                                : new XElement(T + "From",
                                    new XElement(T + "Mailbox",
                                        new XElement(T + "EmailAddress", sendAsAddress))))))));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), envelope)
            .ToString(SaveOptions.DisableFormatting);
    }

    public void Dispose() => _http?.Dispose();
}
