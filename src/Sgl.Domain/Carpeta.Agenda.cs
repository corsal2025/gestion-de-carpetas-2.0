namespace Sgl.Domain;

public sealed partial class Carpeta
{
    /// <summary>Trámite declared in the daily agenda (only for agenda-imported carpetas).</summary>
    public TramiteAgenda? TramiteAgenda { get; private set; }

    /// <summary>Appointment time from the daily agenda.</summary>
    public TimeOnly? HoraCita { get; private set; }

    public const int MaxTipoTramiteLength = 100;
    public const int MaxEmailContactoLength = 254;
    public const int MaxTelefonoContactoLength = 30;

    /// <summary>Moment (Chile local time) the citizen booked the appointment: with the RUT it is the agenda import key.</summary>
    public DateTime? FechaTomaCita { get; private set; }

    /// <summary>Contact e-mail as given when booking (snapshot of that moment, not of the citizen).</summary>
    public string? EmailContacto { get; private set; }

    /// <summary>Contact phone as given when booking (snapshot of that moment, not of the citizen).</summary>
    public string? TelefonoContacto { get; private set; }

    /// <summary>True for a carpeta created at the counter by "Asistencia del día" (came without an appointment).</summary>
    public bool EsExtra { get; private set; }

    /// <summary>States a classification can pick. "Citada" is excluded: leaving a carpeta for review is its own action.</summary>
    public static IReadOnlyList<EstadoCarpeta> DestinosClasificacion =>
        EstadoTransitions.NextStates(EstadoCarpeta.PorClasificar).Where(e => e != EstadoCarpeta.Citada).ToList();

    /// <summary>Creates a carpeta from a daily agenda row. Nothing is auto-classified.</summary>
    public static Carpeta CrearDesdeAgenda(
        Ciudadano ciudadano,
        Sede sede,
        DateOnly fechaCita,
        TimeOnly? horaCita,
        TramiteAgenda? tramite,
        string autor,
        DateTime now,
        DateTime? fechaTomaCita = null,
        string? email = null,
        string? telefono = null)
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
            FechaTomaCita = fechaTomaCita,
            RutImportacion = ciudadano.Rut.Value,
            EmailContacto = Recortado(email, MaxEmailContactoLength),
            TelefonoContacto = Recortado(telefono, MaxTelefonoContactoLength),
            Estado = EstadoCarpeta.PorClasificar,
            Decision = Decision.Pendiente,
            FechaRegistro = now,
            FechaEstadoActual = now,
        };
        carpeta.Audit(now, usuario, "Agenda", null, $"{EstadoCarpeta.PorClasificar} ({sede})");
        return carpeta;
    }

    /// <summary>
    /// Carpeta for someone who came without an appointment (signed the paper). Enters "Por clasificar" marked as extra,
    /// dated today, with the trámite chosen at the counter (free text; <paramref name="tramiteAgenda"/> only when it maps).
    /// </summary>
    public static Carpeta CrearExtra(
        Ciudadano ciudadano, Sede sede, DateOnly hoy, string tramite, TramiteAgenda? tramiteAgenda, string autor, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(ciudadano);
        var usuario = RequireAutor(autor);
        RequireSede(sede);
        var texto = Recortado(tramite, MaxTipoTramiteLength)
            ?? throw new DomainException("Elija el trámite de la persona.");

        var carpeta = new Carpeta
        {
            Id = Guid.NewGuid(),
            Ciudadano = ciudadano,
            CiudadanoId = ciudadano.Id,
            Sede = sede,
            FechaCitacion = hoy,
            TipoTramite = texto,
            TramiteAgenda = tramiteAgenda,
            EsExtra = true,
            Estado = EstadoCarpeta.PorClasificar,
            Decision = Decision.Pendiente,
            FechaRegistro = now,
            FechaEstadoActual = now,
        };
        carpeta.Audit(now, usuario, "Extra", null, $"{EstadoCarpeta.PorClasificar} ({sede}) · {texto}");
        return carpeta;
    }

    private static string? Recortado(string? valor, int max)
    {
        var v = valor?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }

    /// <summary>
    /// Manual classification of a "Por clasificar" carpeta. At least one license class is mandatory.
    /// Optionally records the last-folder date; the comuna is mandatory for change-of-address states and refused elsewhere.
    /// Everything is audited.
    /// </summary>
    public void Clasificar(EstadoCarpeta destino, ClaseLicencia clases, string? fechaUltimaCarpeta, string? comuna, string autor, DateTime now)
    {
        ValidarClasificacion(destino, clases, comuna, autor);
        CambiarEstado(destino, autor, now);
        if (EsCambioDomicilio(destino))
        {
            // A person of our agenda asking for a change of address: we request their carpeta from the other comuna.
            FijarSentido(Domain.SentidoCambioDomicilio.Saliente, RequireAutor(autor), now);
        }
        if (destino == EstadoCarpeta.CambioDomicilio)
        {
            // Only the plain «cambio de domicilio» destination goes through «Reclasificación» before «Por pedir».
            MarcarPendienteReclasificacion();
        }
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

    /// <summary>
    /// "Dejar para revisión": a carpeta from Por clasificar that has no resolution yet goes back to Citada, so it
    /// shows in "Para revisión" until it gets a state. No class is required.
    /// </summary>
    public void DejarParaRevision(string autor, DateTime now)
    {
        RequireActiva();
        RequireAutor(autor);
        if (Estado != EstadoCarpeta.PorClasificar)
        {
            throw new DomainException("Solo se pueden dejar para revisión las carpetas de 'Por clasificar'.");
        }
        CambiarEstado(EstadoCarpeta.Citada, autor, now);
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
        if (EsCambioDomicilio(destino) && string.IsNullOrWhiteSpace(comuna))
        {
            throw new DomainException("Indique la comuna a la que se pide el cambio de domicilio.");
        }
        if (!string.IsNullOrWhiteSpace(comuna) && ComunasChile.Normalizar(comuna) is null)
        {
            throw new DomainException("Comuna no reconocida: elija una de las 346 comunas de Chile de la lista.");
        }
    }
}
