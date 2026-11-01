namespace Sgl.Domain;

/// <summary>Chilean RUT value object. Normalized as "12345678-K" (no dots, uppercase K).</summary>
public sealed record Rut
{
    private const int MaxBodyLength = 9;

    public string Value { get; }

    private Rut(string value) => Value = value;

    public string Body => Value[..Value.IndexOf('-')];

    public char CheckDigit => Value[^1];

    public string Formatted => long.Parse(Body, System.Globalization.CultureInfo.InvariantCulture)
        .ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
        .Replace(',', '.') + "-" + CheckDigit;

    public static Rut Parse(string? input) =>
        TryParse(input, out var rut) ? rut! : throw new DomainException("RUT inválido.");

    public static bool TryParse(string? input, out Rut? rut)
    {
        rut = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var clean = input.Trim().Replace(".", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
        if (clean.Length < 2)
        {
            return false;
        }

        var body = clean[..^1];
        var dv = clean[^1];
        if (body.Length > MaxBodyLength || !body.All(char.IsAsciiDigit))
        {
            return false;
        }

        body = body.TrimStart('0');
        if (body.Length == 0 || ComputeCheckDigit(body) != dv)
        {
            return false;
        }

        rut = new Rut($"{body}-{dv}");
        return true;
    }

    /// <summary>
    /// Formats what is being typed as "12.345.678-5": keeps digits and K, puts the dash before the last character and
    /// the dots in thousands. Whatever is not a digit or K is dropped; at most 10 characters (9-digit body + check digit).
    /// </summary>
    public static string FormatearEntrada(string? input)
    {
        var limpio = new string((input ?? string.Empty).Where(ch => char.IsAsciiDigit(ch) || ch is 'k' or 'K').ToArray())
            .ToUpperInvariant();
        if (limpio.Length > MaxBodyLength + 1)
        {
            limpio = limpio[..(MaxBodyLength + 1)];
        }

        if (limpio.Length < 2)
        {
            return limpio;
        }

        var cuerpo = limpio[..^1];
        var puntos = new System.Text.StringBuilder();
        for (var i = 0; i < cuerpo.Length; i++)
        {
            if (i > 0 && (cuerpo.Length - i) % 3 == 0)
            {
                puntos.Append('.');
            }

            puntos.Append(cuerpo[i]);
        }

        return $"{puntos}-{limpio[^1]}";
    }

    /// <summary>
    /// Checks a typed RUT. Returns null when valid (and the RUT), or the message to show the user: empty, impossible
    /// format, or a check digit that does not match (saying which one it should be).
    /// </summary>
    public static string? Validar(string? input, out Rut? rut)
    {
        if (TryParse(input, out rut))
        {
            return null;
        }

        var clean = (input ?? string.Empty).Trim().Replace(".", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
        if (clean.Length == 0)
        {
            return "Escriba un RUT.";
        }

        var body = clean.Length > 1 ? clean[..^1].TrimStart('0') : string.Empty;
        if (body.Length == 0 || body.Length > MaxBodyLength || !body.All(char.IsAsciiDigit))
        {
            return "RUT inválido: revise el número.";
        }

        return $"El dígito verificador no calza. Debería ser {ComputeCheckDigit(body)}. Revise el papel.";
    }

    /// <summary>Modulo-11 check digit with weights 2..7 applied right to left.</summary>
    public static char ComputeCheckDigit(string body)
    {
        var sum = 0;
        var weight = 2;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            sum += (body[i] - '0') * weight;
            weight = weight == 7 ? 2 : weight + 1;
        }

        var result = 11 - (sum % 11);
        return result switch
        {
            11 => '0',
            10 => 'K',
            _ => (char)('0' + result),
        };
    }

    public override string ToString() => Value;
}
