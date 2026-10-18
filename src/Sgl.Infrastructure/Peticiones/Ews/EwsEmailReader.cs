using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Sgl.Application.SincronizacionCorreo;

namespace PeticionCambioDomicilio.Ews;

/// <summary>
/// Read-only EWS adapter of <see cref="IEmailReader"/>: lists the items of a mailbox folder by display name
/// (FindFolder → paged FindItem → batched GetItem, plain-text body). Never sends, moves or flags anything.
/// Ported from CambioDeDomicilio/Ews (EwsEmailReader, EwsClient, EwsMessages, EwsResponseParser), reusing the
/// same connection settings as <see cref="EwsMailSender"/> (section Peticion:Ews) and its retry policy: a 401/403
/// stops at the first attempt (retrying rejected credentials only speeds up the domain-account lockout).
/// </summary>
public sealed class EwsEmailReader : IEmailReader, IDisposable
{
    private const int MaxAttempts = 4;
    private const int PageSize = 200;
    private const int MaxPages = 50;

    /// <summary>Exchange throttling rejects large GetItem batches; 50 stays well under the default policy.</summary>
    private const int GetItemBatchSize = 50;

    private readonly string? _url;
    private readonly string? _buzon;
    private readonly HttpClient? _http;
    private readonly Dictionary<string, EwsCarpetaRef> _carpetas = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EwsEmailReader(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var ews = options.Ews;
        _url = ews?.Url;
        _buzon = FirstNonBlank(ews?.MailboxToRead, ews?.SendAsAddress);
        if (!string.IsNullOrWhiteSpace(ews?.Url) && !string.IsNullOrWhiteSpace(ews.Username) && !string.IsNullOrWhiteSpace(ews.Password))
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ews.Username}:{ews.Password}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public bool EstaConfigurado => _http is not null;

    public async Task<IReadOnlyList<CorreoEntrante>> LeerCarpetaAsync(string carpeta, CancellationToken ct = default)
    {
        if (_http is null || _url is null)
        {
            return [];
        }

        await _gate.WaitAsync(ct);
        try
        {
            try
            {
                return await ListarAsync(carpeta, ct);
            }
            catch (EwsSendException ex) when (ex is not EwsAuthException)
            {
                // A cached folder id goes stale when the folder is renamed or recreated in Outlook: re-resolve once.
                _carpetas.Remove(carpeta);
                return await ListarAsync(carpeta, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<CorreoEntrante>> ListarAsync(string carpeta, CancellationToken ct)
    {
        var folder = await ResolverCarpetaAsync(carpeta, ct);
        if (folder is null)
        {
            return [];
        }

        var refs = new List<EwsItemRef>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var respuesta = await PostAsync(EwsLectura.FindItem(folder, offset, PageSize), ct);
            var (items, incluyeUltimo) = EwsLectura.ParsearFindItem(respuesta);
            refs.AddRange(items.Where(i => vistos.Add(i.Id)));
            if (incluyeUltimo || items.Count == 0)
            {
                break;
            }
            offset += items.Count;
        }

        var correos = new List<CorreoEntrante>(refs.Count);
        foreach (var lote in refs.Chunk(GetItemBatchSize))
        {
            correos.AddRange(EwsLectura.ParsearGetItem(await PostAsync(EwsLectura.GetItem(lote), ct)));
        }
        return correos;
    }

    private async Task<EwsCarpetaRef?> ResolverCarpetaAsync(string carpeta, CancellationToken ct)
    {
        if (_carpetas.TryGetValue(carpeta, out var cached))
        {
            return cached;
        }

        var resuelta = EwsLectura.ParsearFindFolder(await PostAsync(EwsLectura.FindFolder(carpeta, _buzon), ct));
        if (resuelta is not null)
        {
            _carpetas[carpeta] = resuelta;
        }
        return resuelta;
    }

    private async Task<XDocument> PostAsync(string soap, CancellationToken ct)
    {
        var attempt = 0;
        var delay = TimeSpan.FromSeconds(2);
        while (true)
        {
            attempt++;
            try
            {
                using var content = new StringContent(soap, Encoding.UTF8, "text/xml");
                using var response = await _http!.PostAsync(_url, content, ct);
                switch (EwsRetryPolicy.Classify(response.StatusCode))
                {
                    case EwsAction.Accept:
                        return XDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    case EwsAction.FailAuth:
                        throw new EwsAuthException(
                            $"Exchange rechazó las credenciales (HTTP {(int)response.StatusCode}). " +
                            "Revise Peticion:Ews:Username y la clave (user-secrets). No reintente a ciegas: cada rechazo suma al bloqueo de la cuenta.");
                    case EwsAction.Retry when attempt < MaxAttempts:
                        await Task.Delay(delay, ct);
                        delay *= 2;
                        continue;
                    case EwsAction.Retry:
                        throw new EwsSendException(
                            $"Exchange no respondió tras {MaxAttempts} intentos (último: HTTP {(int)response.StatusCode}).");
                    default:
                        throw new EwsSendException($"Exchange devolvió HTTP {(int)response.StatusCode}.");
                }
            }
            catch (Exception ex) when (EsDeRed(ex) && attempt < MaxAttempts)
            {
                await Task.Delay(delay, ct);
                delay *= 2;
            }
            catch (Exception ex) when (EsDeRed(ex))
            {
                throw new EwsSendException($"No se pudo alcanzar Exchange tras {MaxAttempts} intentos: {ex.Message}.", ex);
            }
        }
    }

    private static bool EsDeRed(Exception ex) =>
        ex is HttpRequestException or System.Net.Sockets.SocketException
            || (ex is TaskCanceledException tce && tce.InnerException is TimeoutException);

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    public void Dispose()
    {
        _http?.Dispose();
        _gate.Dispose();
    }
}

/// <summary>A folder resolved by FindFolder.</summary>
public sealed record EwsCarpetaRef(string Id, string ChangeKey);

/// <summary>An item reference returned by FindItem.</summary>
public sealed record EwsItemRef(string Id, string ChangeKey);

/// <summary>
/// SOAP envelopes and response parsing of the read path (pure functions, testable without network). XML is built
/// with XElement so every value is escaped structurally.
/// </summary>
public static class EwsLectura
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace T = "http://schemas.microsoft.com/exchange/services/2006/types";
    private static readonly XNamespace M = "http://schemas.microsoft.com/exchange/services/2006/messages";

    /// <summary>Resolves a folder by display name anywhere under the mailbox root (optionally another mailbox).</summary>
    public static string FindFolder(string displayName, string? buzon) =>
        Envelope(
            new XElement(M + "FindFolder",
                new XAttribute("Traversal", "Deep"),
                new XElement(M + "FolderShape", new XElement(T + "BaseShape", "IdOnly")),
                new XElement(M + "Restriction",
                    new XElement(T + "IsEqualTo",
                        Field("folder:DisplayName"),
                        new XElement(T + "FieldURIOrConstant",
                            new XElement(T + "Constant", new XAttribute("Value", displayName))))),
                new XElement(M + "ParentFolderIds",
                    new XElement(T + "DistinguishedFolderId",
                        new XAttribute("Id", "msgfolderroot"),
                        string.IsNullOrWhiteSpace(buzon)
                            ? null
                            : new XElement(T + "Mailbox", new XElement(T + "EmailAddress", buzon))))));

    /// <summary>Lists the items of a folder, newest first, with no time filter (filing in the folder is the trigger).</summary>
    public static string FindItem(EwsCarpetaRef folder, int offset, int maxEntries) =>
        Envelope(
            new XElement(M + "FindItem",
                new XAttribute("Traversal", "Shallow"),
                new XElement(M + "ItemShape", new XElement(T + "BaseShape", "IdOnly")),
                new XElement(M + "IndexedPageItemView",
                    new XAttribute("MaxEntriesReturned", maxEntries),
                    new XAttribute("Offset", offset),
                    new XAttribute("BasePoint", "Beginning")),
                new XElement(M + "SortOrder",
                    new XElement(T + "FieldOrder",
                        new XAttribute("Order", "Descending"),
                        Field("item:DateTimeReceived"))),
                new XElement(M + "ParentFolderIds",
                    new XElement(T + "FolderId", new XAttribute("Id", folder.Id), new XAttribute("ChangeKey", folder.ChangeKey)))));

    public static string GetItem(IEnumerable<EwsItemRef> items) =>
        Envelope(
            new XElement(M + "GetItem",
                new XElement(M + "ItemShape",
                    new XElement(T + "BaseShape", "IdOnly"),
                    new XElement(T + "BodyType", "Text"),
                    new XElement(T + "AdditionalProperties",
                        Field("item:Subject"),
                        Field("item:DateTimeReceived"),
                        Field("item:Body"),
                        Field("message:From"),
                        Field("message:InternetMessageId"))),
                new XElement(M + "ItemIds",
                    items.Select(i => new XElement(T + "ItemId", new XAttribute("Id", i.Id), new XAttribute("ChangeKey", i.ChangeKey))))));

    /// <summary>Throws when the SOAP response reports an error response class.</summary>
    public static void EnsureSuccess(XDocument document, string operation)
    {
        ArgumentNullException.ThrowIfNull(document);
        var message = document.Descendants()
            .FirstOrDefault(e => e.Name.Namespace == M && e.Name.LocalName.EndsWith("ResponseMessage", StringComparison.Ordinal));
        if (message?.Attribute("ResponseClass")?.Value == "Error")
        {
            var code = message.Element(M + "ResponseCode")?.Value ?? "desconocido";
            throw new EwsSendException($"EWS {operation} falló con el código {code}.");
        }
    }

    public static EwsCarpetaRef? ParsearFindFolder(XDocument document)
    {
        EnsureSuccess(document, "FindFolder");
        var folderId = document.Descendants(T + "FolderId").FirstOrDefault();
        var id = folderId?.Attribute("Id")?.Value;
        var changeKey = folderId?.Attribute("ChangeKey")?.Value;
        return string.IsNullOrEmpty(id) || string.IsNullOrEmpty(changeKey) ? null : new EwsCarpetaRef(id, changeKey);
    }

    public static (IReadOnlyList<EwsItemRef> Items, bool IncluyeUltimo) ParsearFindItem(XDocument document)
    {
        EnsureSuccess(document, "FindItem");
        var root = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "RootFolder");
        var attr = root?.Attribute("IncludesLastItemInRange")?.Value;
        var incluyeUltimo = attr is null || string.Equals(attr, "true", StringComparison.OrdinalIgnoreCase);
        var items = document.Descendants(T + "ItemId")
            .Select(e => new EwsItemRef(e.Attribute("Id")?.Value ?? string.Empty, e.Attribute("ChangeKey")?.Value ?? string.Empty))
            .Where(r => r.Id.Length > 0)
            .ToList();
        return (items, incluyeUltimo);
    }

    /// <summary>Messages without Internet Message-Id or sender are skipped (they cannot be made idempotent).</summary>
    public static IReadOnlyList<CorreoEntrante> ParsearGetItem(XDocument document)
    {
        EnsureSuccess(document, "GetItem");
        var correos = new List<CorreoEntrante>();
        foreach (var message in document.Descendants(T + "Message"))
        {
            var messageId = message.Element(T + "InternetMessageId")?.Value;
            var remitente = message.Element(T + "From")?.Element(T + "Mailbox")?.Element(T + "EmailAddress")?.Value;
            if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(remitente))
            {
                continue;
            }

            var recibido = DateTimeOffset.TryParse(
                message.Element(T + "DateTimeReceived")?.Value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed) ? parsed : DateTimeOffset.UtcNow;

            correos.Add(new CorreoEntrante(
                messageId.Trim(),
                message.Element(T + "Subject")?.Value ?? string.Empty,
                remitente.Trim(),
                message.Element(T + "Body")?.Value ?? string.Empty,
                recibido));
        }
        return correos;
    }

    private static XElement Field(string uri) => new(T + "FieldURI", new XAttribute("FieldURI", uri));

    private static string Envelope(XElement body)
    {
        var envelope = new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soap", Soap),
            new XAttribute(XNamespace.Xmlns + "t", T),
            new XAttribute(XNamespace.Xmlns + "m", M),
            new XElement(Soap + "Header",
                new XElement(T + "RequestServerVersion", new XAttribute("Version", "Exchange2013_SP1"))),
            new XElement(Soap + "Body", body));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), envelope).ToString(SaveOptions.DisableFormatting);
    }
}
