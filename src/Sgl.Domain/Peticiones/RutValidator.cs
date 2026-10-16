using System.Text;

namespace PeticionCambioDomicilio.Domain;

/// <summary>
/// Normaliza un RUT chileno (con o sin puntos, K en mayúscula o minúscula) a la forma
/// canónica con puntos (ej. "18.785.387-7") y valida el dígito verificador.
/// Devuelve null si la entrada no tiene forma de RUT o el dígito no calza.
/// Copiado tal cual de LicenciasCarpetas/CambioDomicilio/Extraction/RutValidator.cs.
/// </summary>
public static class RutValidator
{
    public static string? NormalizeAndValidate(string? rawRut)
    {
        if (rawRut is null)
        {
            return null;
        }

        var digitsAndK = new StringBuilder();
        foreach (var c in rawRut)
        {
            if (char.IsDigit(c) || c is 'k' or 'K')
            {
                digitsAndK.Append(char.ToUpperInvariant(c));
            }
        }

        if (digitsAndK.Length < 2)
        {
            return null;
        }

        var body = digitsAndK.ToString(0, digitsAndK.Length - 1);
        var checkDigit = digitsAndK[^1];

        if (body.Length is < 7 or > 8 || !body.All(char.IsDigit))
        {
            return null;
        }

        if (ComputeCheckDigit(body) != checkDigit)
        {
            return null;
        }

        if (body.Length == 7)
        {
            body = "0" + body;
        }

        return Format(body, checkDigit);
    }

    private static char ComputeCheckDigit(string body)
    {
        var sum = 0;
        var multiplier = 2;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            sum += (body[i] - '0') * multiplier;
            multiplier = multiplier == 7 ? 2 : multiplier + 1;
        }

        var remainder = 11 - (sum % 11);
        return remainder switch
        {
            11 => '0',
            10 => 'K',
            _ => (char)('0' + remainder)
        };
    }

    private static string Format(string body, char checkDigit)
    {
        var reversed = new string(body.Reverse().ToArray());
        var grouped = new StringBuilder();
        for (var i = 0; i < reversed.Length; i++)
        {
            if (i > 0 && i % 3 == 0)
            {
                grouped.Append('.');
            }
            grouped.Append(reversed[i]);
        }
        var formattedBody = new string(grouped.ToString().Reverse().ToArray());
        return $"{formattedBody}-{checkDigit}";
    }
}
