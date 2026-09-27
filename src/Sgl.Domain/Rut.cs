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
        TryParse(input, out var rut) ? rut! : throw new DomainException($"RUT inválido: '{input}'.");

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
