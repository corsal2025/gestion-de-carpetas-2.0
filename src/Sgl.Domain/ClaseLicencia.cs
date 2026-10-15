namespace Sgl.Domain;

/// <summary>Chilean driver's license classes. A carpeta may request several (flags).</summary>
[Flags]
public enum ClaseLicencia
{
    Ninguna = 0,
    A1 = 1 << 0,
    A2 = 1 << 1,
    A3 = 1 << 2,
    A4 = 1 << 3,
    A5 = 1 << 4,
    B = 1 << 5,
    C = 1 << 6,
    D = 1 << 7,
    E = 1 << 8,
    F = 1 << 9,
}

public static class ClasesLicencia
{
    public static readonly IReadOnlyList<ClaseLicencia> Todas =
    [
        ClaseLicencia.A1, ClaseLicencia.A2, ClaseLicencia.A3, ClaseLicencia.A4, ClaseLicencia.A5,
        ClaseLicencia.B, ClaseLicencia.C, ClaseLicencia.D, ClaseLicencia.E, ClaseLicencia.F,
    ];

    private static readonly ClaseLicencia Validas = Todas.Aggregate(ClaseLicencia.Ninguna, (acc, c) => acc | c);

    public static bool IsValid(ClaseLicencia clases) => (clases & ~Validas) == 0;

    public static bool Overlaps(ClaseLicencia a, ClaseLicencia b) => (a & b) != ClaseLicencia.Ninguna;

    public static string Format(ClaseLicencia clases) =>
        string.Join(", ", Todas.Where(c => clases.HasFlag(c)).Select(c => c.ToString()));
}
