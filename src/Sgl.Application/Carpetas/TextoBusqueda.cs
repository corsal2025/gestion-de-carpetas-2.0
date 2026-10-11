using System.Text.RegularExpressions;
using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>Splits free search text into a RUT fragment or a normalized name fragment.</summary>
internal static partial class TextoBusqueda
{
    [GeneratedRegex(@"^[0-9.\-kK]+$")]
    private static partial Regex RutLikeText();

    public static (string? RutFragment, string? NameFragment) Parse(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return (null, null);
        }

        var t = texto.Trim();
        return RutLikeText().IsMatch(t)
            ? (t.Replace(".", string.Empty).ToUpperInvariant(), null)
            : (null, Ciudadano.NormalizeForSearch(t));
    }
}
