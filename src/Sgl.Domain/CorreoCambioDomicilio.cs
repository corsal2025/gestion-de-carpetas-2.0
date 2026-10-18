namespace Sgl.Domain;

/// <summary>
/// Idempotency record of the mail sync: one row per (Internet Message-Id, RUT) already turned into a carpeta (or
/// matched to an existing one). A re-sync of the same message never creates a second carpeta for that person.
/// </summary>
public sealed class CorreoCambioDomicilio
{
    public const int MaxMessageIdLength = 500;

    public long Id { get; private set; }

    public string MessageId { get; private set; } = string.Empty;

    /// <summary>Canonical RUT value (same form as <see cref="Rut.Value"/>).</summary>
    public string Rut { get; private set; } = string.Empty;

    public Guid CarpetaId { get; private set; }

    public DateTime ProcesadoEn { get; private set; }

    private CorreoCambioDomicilio()
    {
    }

    public CorreoCambioDomicilio(string messageId, Rut rut, Guid carpetaId, DateTime procesadoEn)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new DomainException("El identificador del correo es obligatorio.");
        }

        var id = messageId.Trim();
        MessageId = id.Length > MaxMessageIdLength ? id[..MaxMessageIdLength] : id;
        Rut = (rut ?? throw new DomainException("El RUT es obligatorio.")).Value;
        CarpetaId = carpetaId;
        ProcesadoEn = procesadoEn;
    }
}
