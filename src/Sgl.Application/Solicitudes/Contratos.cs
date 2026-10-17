using System.Net.Mail;
using Sgl.Domain;

namespace Sgl.Application.Solicitudes;

// ---- Listings (F8 module and Certificado / Oficio module) ----

/// <summary>A row of the F8 module: the request plus the carpeta data the tray shows.</summary>
public sealed record SolicitudF8Dto(
    Guid Id,
    Guid CarpetaId,
    string Rut,
    string NombreCompleto,
    Sede Sede,
    string? Comuna,
    EstadoCarpeta EstadoCarpeta,
    EstadoCarpeta EstadoOrigen,
    string SolicitadoPor,
    DateTime SolicitadoEn,
    EstadoSolicitudF8 Estado,
    string? ResueltoPor,
    DateTime? ResueltoEn,
    string? Observacion);

/// <summary>A row of the Certificado / Oficio module.</summary>
public sealed record SolicitudCertificadoDto(
    Guid Id,
    Guid CarpetaId,
    string Rut,
    string NombreCompleto,
    Sede Sede,
    string? Comuna,
    EstadoCarpeta EstadoCarpeta,
    TipoCertificado Tipo,
    OrigenCertificado Origen,
    Guid? SolicitudF8Id,
    string SolicitadoPor,
    DateTime SolicitadoEn,
    EstadoCertificado Estado,
    string? FirmadoPor,
    DateTime? FirmadoEn,
    string? NumeroOficio,
    IReadOnlyDictionary<string, string> Parametros,
    DateTime? EnviadoADirectorEn,
    string? Observacion);

/// <summary>Filters of the F8 module. <see cref="Texto"/> is a RUT fragment or a name fragment.</summary>
public sealed record ListarSolicitudesF8Query(
    EstadoSolicitudF8? Estado = null,
    string? Texto = null,
    int Skip = 0,
    int Take = ListadoSolicitudes.TakePorDefecto);

public sealed record ListarCertificadosQuery(
    EstadoCertificado? Estado = null,
    TipoCertificado? Tipo = null,
    string? Texto = null,
    int Skip = 0,
    int Take = ListadoSolicitudes.TakePorDefecto);

/// <summary>Repository-level filter: the free text is already split into a RUT or a normalized name fragment.</summary>
public sealed record FiltroSolicitudesF8(EstadoSolicitudF8? Estado, string? RutFragment, string? NameFragment, int Skip, int Take);

public sealed record FiltroCertificados(
    EstadoCertificado? Estado, TipoCertificado? Tipo, string? RutFragment, string? NameFragment, int Skip, int Take, Guid? Id = null);

public static class ListadoSolicitudes
{
    public const int TakePorDefecto = 50;
    public const int TakeMaximo = 500;

    internal static (int Skip, int Take) Acotar(int skip, int take) =>
        (Math.Max(0, skip), Math.Clamp(take, 1, TakeMaximo));
}

// ---- Results ----

/// <summary>
/// Outcome of asking for an F8. When one is already open nothing is created and <see cref="YaAbierta"/> is true with
/// the id of the existing request.
/// </summary>
public sealed record SolicitarF8Resultado(Guid SolicitudId, bool Creada, bool YaAbierta, string? Mensaje);

/// <summary>Outcome of "no se generó": the certificate that was opened (or the open one that already existed).</summary>
public sealed record NoSeGeneroResultado(Guid CertificadoId, bool CertificadoYaExistia, bool EnviadoADirector, string? ErrorEnvio);

public sealed record CertificadoCreado(Guid Id, bool EnviadoADirector, string? ErrorEnvio);

public sealed record CrearCertificadoManualCommand(
    Guid CarpetaId,
    TipoCertificado Tipo,
    IReadOnlyDictionary<string, string>? Parametros,
    string? NumeroOficio,
    string Autor);

// ---- Signers (editable settings, never hard-coded) ----

/// <summary>Who signs and who is notified. All empty until an administrator fills them in.</summary>
public sealed record Firmantes(
    string DirectorNombre,
    string DirectorCargo,
    string DirectorCorreo,
    string SecretariaNombre,
    string SecretariaCargo)
{
    public const int MaxLength = 200;

    public static Firmantes Vacios { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>Trims every field and checks the lengths and the director's e-mail; throws <see cref="DomainException"/>.</summary>
    public Firmantes Normalizar()
    {
        var normalizado = new Firmantes(
            Limpiar(DirectorNombre, "El nombre del director"),
            Limpiar(DirectorCargo, "El cargo del director"),
            Limpiar(DirectorCorreo, "El correo del director"),
            Limpiar(SecretariaNombre, "El nombre de la secretaria"),
            Limpiar(SecretariaCargo, "El cargo de la secretaria"));
        if (normalizado.DirectorCorreo.Length > 0 && !EsCorreo(normalizado.DirectorCorreo))
        {
            throw new DomainException("El correo del director no es válido.");
        }

        return normalizado;
    }

    private static string Limpiar(string? valor, string campo)
    {
        var limpio = valor?.Trim() ?? string.Empty;
        return limpio.Length > MaxLength
            ? throw new DomainException($"{campo} excede {MaxLength} caracteres.")
            : limpio;
    }

    private static bool EsCorreo(string correo) =>
        !correo.Contains(' ')
        && MailAddress.TryCreate(correo, out var direccion)
        && direccion.Address == correo
        && direccion.Host.Contains('.');
}

// ---- Notification ----

/// <summary>What the mail to the director carries when a certificate / oficio request is created.</summary>
public sealed record NotificacionCertificado(
    Guid CertificadoId,
    Guid CarpetaId,
    TipoCertificado Tipo,
    OrigenCertificado Origen,
    string Rut,
    string NombreCompleto,
    Sede Sede,
    string SolicitadoPor,
    DateTime SolicitadoEnUtc,
    string? NumeroOficio,
    IReadOnlyDictionary<string, string> Parametros,
    Firmantes Firmantes);

/// <summary>
/// Port for the notification to the director. The default adapter is a safe no-op (sends nothing, returns false);
/// a real mail adapter comes later. Only a real send may return true.
/// </summary>
public interface INotificadorCertificado
{
    /// <returns>True only when the mail was actually sent.</returns>
    Task<bool> EnviarAsync(NotificacionCertificado notificacion, CancellationToken ct = default);
}

// ---- Persistence ports (implemented in Infrastructure; they never save by themselves) ----

public interface ISolicitudF8Repository
{
    /// <summary>Tracked request by id (resolution mutates it inside the unit of work).</summary>
    Task<SolicitudF8?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>The open (Solicitado) request of a carpeta, if any (tracked).</summary>
    Task<SolicitudF8?> GetAbiertaPorCarpetaAsync(Guid carpetaId, CancellationToken ct = default);

    Task AddAsync(SolicitudF8 solicitud, CancellationToken ct = default);

    Task<(IReadOnlyList<SolicitudF8Dto> Items, int Total)> ListarAsync(FiltroSolicitudesF8 filtro, CancellationToken ct = default);

    /// <summary>Requests per state (cancelled carpetas excluded); states without rows are absent.</summary>
    Task<IReadOnlyDictionary<EstadoSolicitudF8, int>> ContarPorEstadoAsync(CancellationToken ct = default);
}

public interface ICertificadoRepository
{
    Task<SolicitudCertificado?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>The open (PorFirmar) certificate of a carpeta and tipo, if any (tracked).</summary>
    Task<SolicitudCertificado?> GetAbiertoAsync(Guid carpetaId, TipoCertificado tipo, CancellationToken ct = default);

    Task AddAsync(SolicitudCertificado certificado, CancellationToken ct = default);

    Task<(IReadOnlyList<SolicitudCertificadoDto> Items, int Total)> ListarAsync(FiltroCertificados filtro, CancellationToken ct = default);

    Task<IReadOnlyDictionary<EstadoCertificado, int>> ContarPorEstadoAsync(TipoCertificado? tipo, CancellationToken ct = default);
}

public interface IFirmantesRepository
{
    Task<Firmantes> ObtenerAsync(CancellationToken ct = default);

    /// <summary>Persists the signers (this one commits by itself: it is a settings screen, not part of a workflow).</summary>
    Task GuardarAsync(Firmantes firmantes, CancellationToken ct = default);
}
