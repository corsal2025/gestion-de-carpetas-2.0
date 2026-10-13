namespace Sgl.Domain;

/// <summary>
/// One run of the integrity watchdog (counts only, no personal data). Kept as audit trail of when the rules
/// were checked, what triggered the run and how many problems it found.
/// </summary>
public sealed class IntegridadCorrida
{
    public const int MaxOrigenLength = 20;
    public const int MaxDetalleLength = 200;

    public long Id { get; private set; }

    /// <summary>UTC instant of the run; shown in Chile time.</summary>
    public DateTime Fecha { get; private set; }

    /// <summary>What triggered it: Inicio, Importacion, Diaria or Manual.</summary>
    public string Origen { get; private set; } = string.Empty;

    /// <summary>Total number of violations found (0 = everything holds).</summary>
    public int Problemas { get; private set; }

    /// <summary>True when the check itself could not complete (the result is then not "all clear").</summary>
    public bool Fallo { get; private set; }

    public int DuracionMs { get; private set; }

    /// <summary>Problems per rule, e.g. "R1=0;R2=0;R3=2;R4=0;R5=0" (or the failure reason without personal data).</summary>
    public string Detalle { get; private set; } = string.Empty;

    public bool TieneProblemas => Fallo || Problemas > 0;

    private IntegridadCorrida()
    {
    }

    public IntegridadCorrida(DateTime fecha, string origen, int problemas, bool fallo, int duracionMs, string detalle)
    {
        Fecha = fecha;
        Origen = string.IsNullOrWhiteSpace(origen) ? "Manual" : origen.Trim()[..Math.Min(origen.Trim().Length, MaxOrigenLength)];
        Problemas = Math.Max(0, problemas);
        Fallo = fallo;
        DuracionMs = Math.Max(0, duracionMs);
        var texto = detalle?.Trim() ?? string.Empty;
        Detalle = texto[..Math.Min(texto.Length, MaxDetalleLength)];
    }
}
