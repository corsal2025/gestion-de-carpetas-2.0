using System.Text.RegularExpressions;
using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>One hit of the top-bar search: who, and the tray where the carpeta is now (cancelled ones flagged).</summary>
public sealed record ResultadoBusquedaGlobalDto(Guid Id, string NombreCompleto, string Rut, Bandeja Bandeja, bool Anulada);

/// <summary>Rules of the top-bar search over every carpeta of the system.</summary>
public static partial class BusquedaGlobal
{
    /// <summary>Maximum number of hits returned.</summary>
    public const int Limite = 20;

    /// <summary>Shorter texts are ignored (too many hits to be useful).</summary>
    public const int MinimoCaracteres = 3;

    [GeneratedRegex(@"^[0-9.\-kK\s]+$")]
    private static partial Regex RutLikeText();

    /// <summary>
    /// RUT-like text becomes a compact fragment (digits and K, no dots, dash or spaces) matched against the compact
    /// stored RUT; anything else becomes an accent/case-insensitive name fragment. Both null when the text is too short.
    /// </summary>
    public static (string? RutCompacto, string? Nombre) Parse(string? texto)
    {
        var t = texto?.Trim() ?? string.Empty;
        if (t.Length < MinimoCaracteres)
        {
            return (null, null);
        }

        if (RutLikeText().IsMatch(t))
        {
            var compacto = new string(t.Where(ch => char.IsDigit(ch) || ch is 'k' or 'K').ToArray()).ToUpperInvariant();
            return compacto.Length == 0 ? (null, null) : (compacto, null);
        }

        return (null, Ciudadano.NormalizeForSearch(t));
    }
}
