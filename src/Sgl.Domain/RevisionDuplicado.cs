namespace Sgl.Domain;

/// <summary>
/// Marks a citizen's set of open carpetas as reviewed for duplicates. The case is pending again
/// only when an open carpeta is registered after <see cref="RevisadoEn"/>.
/// </summary>
public sealed class RevisionDuplicado
{
    public Guid CiudadanoId { get; private set; }

    public DateTime RevisadoEn { get; private set; }

    public string RevisadoPor { get; private set; } = string.Empty;

    private RevisionDuplicado()
    {
    }

    public RevisionDuplicado(Guid ciudadanoId, string autor, DateTime now)
    {
        CiudadanoId = ciudadanoId;
        Renovar(autor, now);
    }

    public void Renovar(string autor, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(autor))
        {
            throw new DomainException("El autor del cambio es obligatorio.");
        }

        var trimmed = autor.Trim();
        if (trimmed.Length > Carpeta.MaxUsuarioLength)
        {
            throw new DomainException($"El autor excede {Carpeta.MaxUsuarioLength} caracteres.");
        }

        RevisadoPor = trimmed;
        RevisadoEn = now;
    }
}
