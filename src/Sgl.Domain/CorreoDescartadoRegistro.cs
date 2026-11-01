namespace Sgl.Domain;

/// <summary>
/// An email of the «PARA PEDIR» folder that could not become a carpeta (no valid RUT, rejected person, ...). It is kept so the user
/// can see what was left out and why. Subject and sender are personal data in practice: they are stored truncated, without the
/// body, and never written to logs. One row per Message-Id: syncing again does not duplicate it.
/// </summary>
public sealed class CorreoDescartadoRegistro
{
    public const int MaxMessageIdLength = CorreoCambioDomicilio.MaxMessageIdLength;
    public const int MaxAsuntoLength = 120;
    public const int MaxRemitenteLength = 120;
    public const int MaxMotivoLength = 200;

    public long Id { get; private set; }

    public string MessageId { get; private set; } = string.Empty;

    public string Asunto { get; private set; } = string.Empty;

    public string Remitente { get; private set; } = string.Empty;

    /// <summary>UTC instant the email arrived.</summary>
    public DateTime RecibidoEn { get; private set; }

    public string Motivo { get; private set; } = string.Empty;

    /// <summary>UTC instant of the sync that left it out.</summary>
    public DateTime DescartadoEn { get; private set; }

    private CorreoDescartadoRegistro()
    {
    }

    public CorreoDescartadoRegistro(
        string messageId, string? asunto, string? remitente, DateTime recibidoEnUtc, string motivo, DateTime descartadoEnUtc)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new DomainException("El identificador del correo es obligatorio.");
        }
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new DomainException("El motivo del descarte es obligatorio.");
        }

        MessageId = Recortar(messageId.Trim(), MaxMessageIdLength);
        Asunto = Recortar((asunto ?? string.Empty).Trim(), MaxAsuntoLength);
        Remitente = Recortar((remitente ?? string.Empty).Trim(), MaxRemitenteLength);
        RecibidoEn = recibidoEnUtc;
        Motivo = Recortar(motivo.Trim(), MaxMotivoLength);
        DescartadoEn = descartadoEnUtc;
    }

    /// <summary>The key used to look a message up (same trimming and cut as the stored value).</summary>
    public static string Clave(string messageId) => Recortar((messageId ?? string.Empty).Trim(), MaxMessageIdLength);

    private static string Recortar(string texto, int max) => texto.Length > max ? texto[..max] : texto;
}
