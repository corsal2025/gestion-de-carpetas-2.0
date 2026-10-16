using System.Globalization;
using System.Text;

namespace PeticionCambioDomicilio.Domain;

public static class TextNormalization
{
    /// <summary>Minúsculas, sin tildes, sin espacios de sobra. Para comparar encabezados y nombres de comuna.</summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        var collapsed = sb.ToString().Normalize(NormalizationForm.FormC);
        return string.Join(' ', collapsed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
