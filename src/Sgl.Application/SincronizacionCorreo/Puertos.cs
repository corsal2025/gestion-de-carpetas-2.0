using Sgl.Domain;

namespace Sgl.Application.SincronizacionCorreo;

/// <summary>One email listed from a mailbox folder (plain-text body; the body is never shown in the UI).</summary>
public sealed record CorreoEntrante(
    string MessageId,
    string Asunto,
    string Remitente,
    string Cuerpo,
    DateTimeOffset RecibidoEn);

/// <summary>Read-only access to the department mailbox (port; EWS adapter in Infrastructure).</summary>
public interface IEmailReader
{
    /// <summary>True when the connection settings (Url, user, password) are present.</summary>
    bool EstaConfigurado { get; }

    /// <summary>
    /// Every item currently in the folder with that display name (no time filter: the trigger is the operator filing
    /// the email in the folder). Empty when the folder does not exist.
    /// </summary>
    Task<IReadOnlyList<CorreoEntrante>> LeerCarpetaAsync(string carpeta, CancellationToken ct = default);
}

/// <summary>Maps a sender address to the comuna that sent it (comuna directory; port).</summary>
public interface IComunaRemitente
{
    /// <summary>Comuna name, or null when the address / domain is unknown or ambiguous.</summary>
    string? ComunaDe(string remitente);
}

/// <summary>Persistence of the sync idempotency records (shares the unit of work of <c>ICarpetaRepository</c>).</summary>
public interface ICorreoCambioDomicilioRepository
{
    Task<bool> ExisteAsync(string messageId, Rut rut, CancellationToken ct = default);

    /// <summary>Carpetas created (or matched) from that message, any person.</summary>
    Task<IReadOnlyList<Guid>> CarpetasDeMensajeAsync(string messageId, CancellationToken ct = default);

    /// <summary>Open, non-cancelled carpetas of that citizen currently in <see cref="EstadoCarpeta.CambioDomicilio"/>.</summary>
    Task<IReadOnlyList<Guid>> CarpetasEnCambioDomicilioAsync(Guid ciudadanoId, CancellationToken ct = default);

    Task AddAsync(CorreoCambioDomicilio registro, CancellationToken ct = default);
}

/// <summary>Folder names and defaults of the sync (configurable, see <c>Peticion:Ews</c>).</summary>
public sealed class CorreoCambioDomicilioOptions
{
    public const string CarpetaParaPedirPorDefecto = "CARP. PARA PEDIR";
    public const string CarpetaYaSubidasPorDefecto = "CARP. YA SUBIDAS";

    public string CarpetaParaPedir { get; init; } = CarpetaParaPedirPorDefecto;

    public string CarpetaYaSubidas { get; init; } = CarpetaYaSubidasPorDefecto;

    /// <summary>Sede assigned to carpetas created from email (the email does not say it).</summary>
    public Sede Sede { get; init; } = Sede.AvArgentina;
}
