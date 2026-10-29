using System.Globalization;

namespace Sgl.Domain;

/// <summary>Standard reasons for cancelling (soft-deleting) a carpeta.</summary>
public static class MotivosAnulacion
{
    public const string Duplicado = "Duplicado";
    public const string Reagendamiento = "Reagendamiento";
    public const int MaxLength = 100;
}

public sealed partial class Carpeta
{
    private const string CampoAnulacion = "Anulacion";
    private const string CampoReagendamiento = "Reagendamiento";

    /// <summary>Soft delete flag: a cancelled carpeta disappears from every module, count and statistic.</summary>
    public bool Anulada { get; private set; }

    public string? MotivoAnulacion { get; private set; }

    public DateTime? AnuladaEn { get; private set; }

    public string? AnuladaPor { get; private set; }

    /// <summary>Open for duplicate alerts: not cancelled, no final decision and not physically archived in a box.</summary>
    public bool EstaAbiertaSinArchivar => !Anulada && EstaAbierta && Bandeja != Bandeja.Archivadas;

    private void RequireActiva()
    {
        if (Anulada)
        {
            throw new DomainException("La carpeta está anulada: restáurela antes de modificarla.");
        }
    }

    /// <summary>Cancels the carpeta without deleting it. The current state is kept so it can be restored.</summary>
    public void Anular(string motivo, string autor, DateTime now)
    {
        var usuario = RequireAutor(autor);
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new DomainException("El motivo de anulación es obligatorio.");
        }

        var m = motivo.Trim();
        if (m.Length > MotivosAnulacion.MaxLength)
        {
            throw new DomainException($"El motivo de anulación excede {MotivosAnulacion.MaxLength} caracteres.");
        }
        if (Anulada)
        {
            throw new DomainException("La carpeta ya está anulada.");
        }

        Audit(now, usuario, CampoAnulacion, null, $"Anulada: {m}");
        Anulada = true;
        MotivoAnulacion = m;
        AnuladaEn = now;
        AnuladaPor = usuario;
    }

    /// <summary>Undoes a cancellation; the carpeta returns to the state it had (never changed by the cancellation).</summary>
    public void Restaurar(string autor, DateTime now)
    {
        var usuario = RequireAutor(autor);
        if (!Anulada)
        {
            throw new DomainException("La carpeta no está anulada.");
        }

        Audit(now, usuario, CampoAnulacion, $"Anulada: {MotivoAnulacion}", "Restaurada");
        Anulada = false;
        MotivoAnulacion = null;
        AnuladaEn = null;
        AnuladaPor = null;
    }

    /// <summary>Moves the appointment (and sede when it differs) to a new agenda date, recording a readable history line.</summary>
    public void Reagendar(DateOnly nuevaFecha, Sede nuevaSede, string autor, DateTime now)
    {
        var usuario = RequireAutor(autor);
        RequireSede(nuevaSede);
        if (Anulada)
        {
            throw new DomainException("No se puede reagendar una carpeta anulada.");
        }

        if (FechaSubida is { } subida && subida < nuevaFecha)
        {
            throw new DomainException("La fecha de subida no puede ser anterior a la nueva fecha de citación.");
        }

        var linea = string.Create(CultureInfo.InvariantCulture, $"Reagendada desde {FechaCitacion:dd-MM} a {nuevaFecha:dd-MM}");
        Audit(now, usuario, CampoReagendamiento, null, linea);
        if (nuevaFecha != FechaCitacion)
        {
            Audit(now, usuario, nameof(FechaCitacion), Format(FechaCitacion), Format(nuevaFecha));
            FechaCitacion = nuevaFecha;
        }
        if (nuevaSede != Sede)
        {
            Audit(now, usuario, nameof(Sede), Sede.ToString(), nuevaSede.ToString());
            Sede = nuevaSede;
        }
    }
}
