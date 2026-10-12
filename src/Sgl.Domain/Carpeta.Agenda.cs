namespace Sgl.Domain;

public sealed partial class Carpeta
{
    /// <summary>Trámite declared in the daily agenda (only for agenda-imported carpetas).</summary>
    public TramiteAgenda? TramiteAgenda { get; private set; }

    /// <summary>Appointment time from the daily agenda.</summary>
    public TimeOnly? HoraCita { get; private set; }

    /// <summary>States a "Por clasificar" carpeta may be manually classified into.</summary>
    public static IReadOnlyList<EstadoCarpeta> DestinosClasificacion => EstadoTransitions.NextStates(EstadoCarpeta.PorClasificar);

    /// <summary>Creates a carpeta from a daily agenda row. Nothing is auto-classified.</summary>
    public static Carpeta CrearDesdeAgenda(
        Ciudadano ciudadano,
        Sede sede,
        DateOnly fechaCita,
        TimeOnly? horaCita,
        TramiteAgenda? tramite,
        string autor,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(ciudadano);
        var usuario = RequireAutor(autor);
        RequireSede(sede);

        var carpeta = new Carpeta
        {
            Id = Guid.NewGuid(),
            Ciudadano = ciudadano,
            CiudadanoId = ciudadano.Id,
            Sede = sede,
            FechaCitacion = fechaCita,
            HoraCita = horaCita,
            TramiteAgenda = tramite,
            Estado = EstadoCarpeta.PorClasificar,
            Decision = Decision.Pendiente,
            FechaRegistro = now,
        };
        carpeta.Audit(now, usuario, "Agenda", null, $"{EstadoCarpeta.PorClasificar} ({sede})");
        return carpeta;
    }

    /// <summary>
    /// Manual classification of a "Por clasificar" carpeta. At least one license class is mandatory.
    /// Optionally records the last-folder date and, only for change-of-address states, the comuna.
    /// Everything is audited.
    /// </summary>
    public void Clasificar(EstadoCarpeta destino, ClaseLicencia clases, string? fechaUltimaCarpeta, string? comuna, string autor, DateTime now)
    {
        ValidarClasificacion(destino, clases, comuna, autor);
        CambiarEstado(destino, autor, now);
        AsignarClases(clases, autor, now);
        if (!string.IsNullOrWhiteSpace(fechaUltimaCarpeta))
        {
            ModificarFechaUltimaCarpeta(fechaUltimaCarpeta, autor, now);
        }
        if (!string.IsNullOrWhiteSpace(comuna))
        {
            AsignarComuna(comuna, autor, now);
        }
    }

    /// <summary>Checks every classification rule without changing anything (lets a batch validate all rows first).</summary>
    public void ValidarClasificacion(EstadoCarpeta destino, ClaseLicencia clases, string? comuna, string autor)
    {
        RequireActiva();
        RequireAutor(autor);
        if (Estado != EstadoCarpeta.PorClasificar)
        {
            throw new DomainException("Solo se pueden clasificar carpetas en 'Por clasificar'.");
        }
        if (clases == ClaseLicencia.Ninguna)
        {
            throw new DomainException("Debe marcar al menos una clase de licencia para clasificar.");
        }
        RequireClases(clases);
        if (!string.IsNullOrWhiteSpace(comuna) && !EsCambioDomicilio(destino))
        {
            throw new DomainException("La comuna solo aplica a estados de cambio de domicilio.");
        }

        if (!EstadoTransitions.CanTransition(Estado, destino))
        {
            throw new InvalidTransitionException(Estado, destino);
        }
        if (comuna?.Trim().Length > MaxComunaLength)
        {
            throw new DomainException($"La comuna excede {MaxComunaLength} caracteres.");
        }
    }
}
