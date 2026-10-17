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

    public void CambiarRut(Rut nuevoRut)
    {
        Rut = nuevoRut ?? throw new DomainException("El RUT es obligatorio.");
    }

    /// <summary>
    /// Manual correction of the full name (shared by every carpeta of this person). The last two words are the
    /// surnames, as in <see cref="Separar"/>. Returns true when the name changed.
    /// </summary>
    public bool CambiarNombreCompleto(string completo)
    {
        if (string.IsNullOrWhiteSpace(completo))
        {
            throw new DomainException("El nombre es obligatorio.");
        }

        var (nombre, apellido) = Separar(completo);
        nombre = RequireName(nombre, "nombre");
        apellido = RequireName(apellido, "apellido");
        if (nombre == Nombre && apellido == Apellido)
        {
            return false;
        }

        Nombre = nombre;
        Apellido = apellido;
        NombreBusqueda = NormalizeForSearch(NombreCompleto);
        return true;
    }

    public const string SinNombre = "SIN NOMBRE";
    public const string SinApellido = "SIN APELLIDO";

    /// <summary>Surname placeholder written by the first Excel importer when only a full-name column existed.</summary>
    private const string ApellidoVecino = "VECINO";

    /// <summary>Splits a full name in uppercase: the last two words are the surnames.</summary>
    public static (string Nombre, string Apellido) Separar(string? completo)
    {
        var partes = (completo ?? "").Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length switch
        {
            0 => (SinNombre, SinApellido),
            1 => (partes[0], SinApellido),
            2 => (partes[0], partes[1]),
            _ => (string.Join(' ', partes[..^2]), string.Join(' ', partes[^2..])),
        };
    }

    /// <summary>
    /// Brings a stored name to the current rule: uppercase, and the legacy "Vecino" surname placeholder replaced by
    /// the surnames found at the end of the name.
    /// </summary>
    /// <returns>True when something changed.</returns>
    public bool NormalizarNombre()
    {
        var (nombre, apellido) = string.Equals(Apellido.Trim(), ApellidoVecino, StringComparison.OrdinalIgnoreCase)
            ? Separar(Nombre)
            : (Nombre.Trim().ToUpperInvariant(), Apellido.Trim().ToUpperInvariant());
        if (nombre.Length > MaxNameLength) nombre = nombre[..MaxNameLength];
        if (apellido.Length > MaxNameLength) apellido = apellido[..MaxNameLength];

        var busqueda = NormalizeForSearch($"{nombre} {apellido}");
        if (nombre == Nombre && apellido == Apellido && busqueda == NombreBusqueda)
        {
            return false;
        }

        Nombre = nombre;
        Apellido = apellido;
        NombreBusqueda = busqueda;
        return true;
    }

    /// <summary>True when <see cref="NormalizarNombre"/> would change this name.</summary>
    public static bool RequiereNormalizar(string nombre, string apellido) =>
        nombre != nombre.Trim().ToUpperInvariant()
        || apellido != apellido.Trim().ToUpperInvariant()
        || string.Equals(apellido.Trim(), ApellidoVecino, StringComparison.OrdinalIgnoreCase);

    private static string RequireName(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"El {field} es obligatorio.");
        }

        // Names are always stored in uppercase (accents kept: "MARÍA PEÑA").
        var trimmed = value.Trim().ToUpperInvariant();
        return trimmed.Length > MaxNameLength
            ? throw new DomainException($"El {field} excede {MaxNameLength} caracteres.")
            : trimmed;
    }
}
