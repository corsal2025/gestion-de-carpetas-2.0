namespace Sgl.Domain;

public sealed class Ciudadano
{
    public const int MaxNameLength = 100;

    public Guid Id { get; private set; }

    public Rut Rut { get; private set; } = null!;

    public string Nombre { get; private set; } = string.Empty;

    public string Apellido { get; private set; } = string.Empty;

    public string NombreCompleto => $"{Nombre} {Apellido}";

    /// <summary>Lowercase, accent-free "nombre apellido" used for provider-independent searching.</summary>
    public string NombreBusqueda { get; private set; } = string.Empty;

    public static string NormalizeForSearch(string text)
    {
        var decomposed = text.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var chars = decomposed
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(chars).Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
    }

    private Ciudadano()
    {
    }

    public Ciudadano(Rut rut, string nombre, string apellido)
    {
        Id = Guid.NewGuid();
        Rut = rut ?? throw new DomainException("El RUT es obligatorio.");
        Nombre = RequireName(nombre, "nombre");
        Apellido = RequireName(apellido, "apellido");
        NombreBusqueda = NormalizeForSearch(NombreCompleto);
    }

    private static string RequireName(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"El {field} es obligatorio.");
        }

        var trimmed = value.Trim();
        return trimmed.Length > MaxNameLength
            ? throw new DomainException($"El {field} excede {MaxNameLength} caracteres.")
            : trimmed;
    }
}
