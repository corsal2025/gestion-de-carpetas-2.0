namespace Sgl.Domain;

/// <summary>
/// Appointment cancelled in the agenda, kept only for monthly statistics. It never holds RUT, name, e-mail or
/// phone. A pseudonymous fingerprint separates reservations that happen to share a time slot.
/// </summary>
public sealed class CitaCancelada
{
    public const int MaxTramiteLength = 100;
    public const int MaxMotivoLength = 250;

    public long Id { get; private set; }

    /// <summary>Pseudonymous reservation fingerprint. Never a raw RUT; legacy statistics have no fingerprint.</summary>
    public string? IdentidadAgenda { get; private set; }

    public void VincularIdentidad(string identidad)
    {
        if (IdentidadAgenda is not null || identidad.Length != 64 || !identidad.All(Uri.IsHexDigit))
            throw new DomainException("La identidad de la cita cancelada no es válida.");
        IdentidadAgenda = identidad;
    }

    public DateOnly FechaCita { get; private set; }

    public DateTime? FechaTomaCita { get; private set; }

    public TimeOnly? HoraCita { get; private set; }

    public Sede Sede { get; private set; }

    public string Tramite { get; private set; } = string.Empty;

    public string? Motivo { get; private set; }

    public DateTime RegistradaEn { get; private set; }

    private CitaCancelada()
    {
    }

    public CitaCancelada(
        DateOnly fechaCita, DateTime? fechaTomaCita, TimeOnly? horaCita, Sede sede, string? tramite, string? motivo, DateTime registradaEn)
    {
        FechaCita = fechaCita;
        FechaTomaCita = fechaTomaCita;
        HoraCita = horaCita;
        Sede = sede;
        Tramite = Recortar(tramite, MaxTramiteLength) ?? string.Empty;
        Motivo = Recortar(motivo, MaxMotivoLength);
        RegistradaEn = registradaEn;
    }

    private static string? Recortar(string? valor, int max)
    {
        var v = valor?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
