using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace PeticionCambioDomicilio.Ews;

public interface IMailSender
{
    /// <summary>true si el transporte está configurado y listo para enviar.</summary>
    bool IsConfigured { get; }

    Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken);
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
public sealed class EwsMailSender : IMailSender, IDisposable
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace T = "http://schemas.microsoft.com/exchange/services/2006/types";
    private static readonly XNamespace M = "http://schemas.microsoft.com/exchange/services/2006/messages";
    private const int MaxAttempts = 4;

    private readonly EwsOptions? _ews;
    private readonly string? _sendAs;
    private readonly HttpClient? _http;

    public EwsMailSender(AppOptions options)
    {
        _ews = options.Ews;
        _sendAs = string.IsNullOrWhiteSpace(options.Ews?.SendAsAddress) ? null : options.Ews!.SendAsAddress!.Trim();
        if (_ews is { Url: not null, Username: not null, Password: not null })
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_ews.Username}:{_ews.Password}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public bool IsConfigured => _http is not null;

    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
    {
        if (_http is null || _ews?.Url is null)
        {
            throw new EwsSendException(
                "Falta configurar Peticion:Ews (Url/Username/Password) en appsettings.Local.json.");
        }

        var soap = BuildSendMailRequest(toAddress, subject, body, _sendAs);
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
                        return XDocument.Parse(xml);

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
                        var cuerpo = await SafeReadBody(response, cancellationToken);
                        throw new EwsSendException(
                            $"Exchange devolvió HTTP {(int)response.StatusCode}{cuerpo}.");
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

    private static async Task<string> SafeReadBody(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = (await response.Content.ReadAsStringAsync(ct)).Trim();
            if (text.Length == 0)
            {
                return string.Empty;
            }

            return " — " + (text.Length > 300 ? text[..300] + "…" : text);
        }
        catch
        {
            return string.Empty;
        }
    }

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

    private static string BuildSendMailRequest(string toAddress, string subject, string body, string? sendAsAddress)
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
                            new XElement(T + "Subject", subject),
                            new XElement(T + "Body", new XAttribute("BodyType", "Text"), body),
                            new XElement(T + "ToRecipients",
                                new XElement(T + "Mailbox",
                                    new XElement(T + "EmailAddress", toAddress))),
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
