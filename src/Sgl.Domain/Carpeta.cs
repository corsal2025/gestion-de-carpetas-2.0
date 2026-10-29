using System.Globalization;

namespace Sgl.Domain;

/// <summary>A citizen's license folder. Every mutation is recorded in <see cref="Historial"/>.</summary>
public sealed partial class Carpeta
{
    public const int MaxUsuarioLength = 100;
    public const int MaxIdoneidadLength = 200;
    public const int MaxComunaLength = 100;

    public const int MaxCodigoF8Length = 15;
    public const int MaxCajaLength = 50;

    private readonly List<HistorialCambio> _historial = [];

    public Guid Id { get; private set; }

    public Guid CiudadanoId { get; private set; }

    public Ciudadano Ciudadano { get; private set; } = null!;

    public Sede Sede { get; private set; }

    public DateOnly FechaCitacion { get; private set; }

    public DateOnly? FechaSubida { get; private set; }

    public EstadoCarpeta Estado { get; private set; }

    public Decision Decision { get; private set; }

    public string? IdoneidadMoral { get; private set; }

    public string? CajaArchivo { get; private set; }

    public string? FechaUltimaCarpeta { get; private set; }

    public string? Comuna { get; private set; }

    /// <summary>F8 code typed by staff: ASCII digits only, up to <see cref="MaxCodigoF8Length"/> digits.</summary>
    public string? CodigoF8 { get; private set; }

    /// <summary>Date of the citizen's second-to-last carpeta (informational; may precede the citation date).</summary>
    public DateOnly? FechaPenultimaCarpeta { get; private set; }

    public string? TipoTramite { get; private set; }

    /// <summary>
    /// Marca "pendiente de búsqueda". Solo es válida mientras la carpeta se encuentra en
    /// Archivos u Of. 43; al pasar a cualquier otro estado se limpia automáticamente.
    /// </summary>
    public bool PendienteBusqueda { get; private set; }

    /// <summary>License classes requested in this carpeta (optional, may be several).</summary>
    public ClaseLicencia Clases { get; private set; }

    /// <summary>When the carpeta was registered or imported into the system.</summary>
    public DateTime FechaRegistro { get; private set; }

    /// <summary>An open carpeta has no final decision yet (neither Otorgado nor Denegado).</summary>
    public bool EstaAbierta => Decision == Decision.Pendiente;

    public IReadOnlyList<HistorialCambio> Historial => _historial;

    private Carpeta()
    {
    }

    public static Carpeta CrearImportada(
        Guid id,
        Ciudadano ciudadano,
        Sede sede,
        DateOnly fechaCitacion,
        DateOnly? fechaSubida,
        string? fechaUltimaCarpeta,
        EstadoCarpeta estado,
        Decision decision,
        string? idoneidadMoral,
        string? tipoTramite,
        string? cajaArchivo,
        string? comuna,
        DateTime now)
    {
        var carpeta = new Carpeta
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            Ciudadano = ciudadano,
            CiudadanoId = ciudadano.Id,
            Sede = sede,
            FechaCitacion = fechaCitacion,
            FechaSubida = fechaSubida,
            FechaUltimaCarpeta = fechaUltimaCarpeta,
            Comuna = comuna,
            Estado = estado,
            Decision = decision,
            IdoneidadMoral = idoneidadMoral,
            TipoTramite = tipoTramite,
            CajaArchivo = cajaArchivo,
            FechaRegistro = now,
            FechaEstadoActual = now,
            // Never invented: a closed carpeta only has a closing date when the Excel brought its upload date.
            FechaCierre = EsEstadoCierre(estado) || decision != Decision.Pendiente ? fechaSubida : null,
        };
        carpeta.Audit(now, "Sistema Importador", "Importacion", null, $"{sede} - {tipoTramite ?? estado.ToString()}");
        return carpeta;
    }

    public void AsignarCaja(string? nuevaCaja, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        var caja = string.IsNullOrWhiteSpace(nuevaCaja) ? null : nuevaCaja.Trim();
        if (caja?.Length > MaxCajaLength)
        {
            throw new DomainException($"El rótulo de caja excede {MaxCajaLength} caracteres.");
        }
        if (caja != CajaArchivo)
        {
            Audit(now, usuario, nameof(CajaArchivo), CajaArchivo, caja);
            CajaArchivo = caja;
        }
    }

    public void AsignarComuna(string? nuevaComuna, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        string? com = null;
        if (!string.IsNullOrWhiteSpace(nuevaComuna))
        {
            com = ComunasChile.Normalizar(nuevaComuna)
                ?? throw new DomainException("Comuna no reconocida: elija una de las 346 comunas de Chile de la lista.");
        }
        if (com != Comuna)
        {
            Audit(now, usuario, nameof(Comuna), Comuna, com);
            Comuna = com;
        }
    }

    public void AsignarCodigoF8(string? codigo, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        var nuevo = string.IsNullOrWhiteSpace(codigo) ? null : codigo.Trim();
        if (nuevo is not null && !nuevo.All(char.IsAsciiDigit))
        {
            throw new DomainException("El código F8 solo admite números.");
        }
        if (nuevo?.Length > MaxCodigoF8Length)
        {
            throw new DomainException($"El código F8 excede {MaxCodigoF8Length} caracteres.");
        }
        if (nuevo != CodigoF8)
        {
            Audit(now, usuario, nameof(CodigoF8), CodigoF8, nuevo);
            CodigoF8 = nuevo;
        }
    }

    public void AsignarFechaPenultimaCarpeta(DateOnly? fecha, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (fecha is { } f && f > HoraChile.Fecha(now))
        {
            throw new DomainException("La fecha de la penúltima carpeta no puede ser futura.");
        }
        if (fecha != FechaPenultimaCarpeta)
        {
            Audit(now, usuario, nameof(FechaPenultimaCarpeta), FechaPenultimaCarpeta?.ToString("yyyy-MM-dd"), fecha?.ToString("yyyy-MM-dd"));
            FechaPenultimaCarpeta = fecha;
        }
    }

    public void ModificarFechaUltimaCarpeta(string? nuevaFecha, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        var f = NormalizarFechaUltimaCarpeta(nuevaFecha);
        if (f != FechaUltimaCarpeta)
        {
            Audit(now, usuario, nameof(FechaUltimaCarpeta), FechaUltimaCarpeta, f);
            FechaUltimaCarpeta = f;
        }
    }

    /// <summary>Recognized dates are stored as ISO (what the date inputs and sorting expect); legacy free text is kept.</summary>
    private static string? NormalizarFechaUltimaCarpeta(string? fecha) =>
        string.IsNullOrWhiteSpace(fecha) ? null : SectorFisico.NormalizarIso(fecha) ?? fecha.Trim();

    public void ModificarObservacion(string? nuevaObservacion, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        var obs = string.IsNullOrWhiteSpace(nuevaObservacion) ? null : nuevaObservacion.Trim();
        if (obs?.Length > MaxIdoneidadLength)
        {
            throw new DomainException($"La observación excede {MaxIdoneidadLength} caracteres.");
        }
        if (obs != IdoneidadMoral)
        {
            Audit(now, usuario, "Observación", IdoneidadMoral, obs);
            IdoneidadMoral = obs;
        }
    }

    public static Carpeta Registrar(
        Ciudadano ciudadano,
        Sede sede,
        DateOnly fechaCitacion,
        string autor,
        DateTime now,
        ClaseLicencia clases = ClaseLicencia.Ninguna)
    {
        ArgumentNullException.ThrowIfNull(ciudadano);
        var usuario = RequireAutor(autor);
        RequireSede(sede);
        RequireFechaCitacion(fechaCitacion);
        RequireClases(clases);

        var carpeta = new Carpeta
        {
            Id = Guid.NewGuid(),
            Ciudadano = ciudadano,
            CiudadanoId = ciudadano.Id,
            Sede = sede,
            FechaCitacion = fechaCitacion,
            Estado = EstadoCarpeta.Citada,
            Decision = Decision.Pendiente,
            Clases = clases,
            FechaRegistro = now,
            FechaEstadoActual = now,
        };
        carpeta.Audit(now, usuario, "Registro", null, $"{EstadoCarpeta.Citada} ({sede})");
        if (clases != ClaseLicencia.Ninguna)
        {
            carpeta.Audit(now, usuario, nameof(ClaseLicencia), null, ClasesLicencia.Format(clases));
        }
        return carpeta;
    }

    public void AsignarClases(ClaseLicencia clases, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        RequireClases(clases);
        if (clases != Clases)
        {
            Audit(now, usuario, nameof(ClaseLicencia), NullIfEmpty(ClasesLicencia.Format(Clases)), NullIfEmpty(ClasesLicencia.Format(clases)));
            Clases = clases;
        }
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static void RequireClases(ClaseLicencia clases)
    {
        if (!ClasesLicencia.IsValid(clases))
        {
            throw new DomainException("Clase de licencia inválida.");
        }
    }

    public void CambiarEstado(EstadoCarpeta nuevo, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (!EstadoTransitions.CanTransition(Estado, nuevo))
        {
            throw new InvalidTransitionException(Estado, nuevo);
        }

        AplicarEstado(nuevo, usuario, now);
    }

    public static bool EsCambioDomicilio(EstadoCarpeta estado) =>
        estado is EstadoCarpeta.CambioDomicilio
            or EstadoCarpeta.CambioDomicilioSolicitado
            or EstadoCarpeta.CambioDomSubidoConaset
            or EstadoCarpeta.CambioDomSubidoCorreo;

    public static bool PermiteBusquedaPendiente(EstadoCarpeta estado) =>
        estado is EstadoCarpeta.SeEncuentraEnArchivos or EstadoCarpeta.SeEncuentraEnOf43;

    /// <summary>
    /// States where the "en proceso de búsqueda" mark may exist: the archive states plus "No existe carpeta"
    /// (the search continues there). Used only by the mark itself; the printed-listing rules, the tray queries and the
    /// automatic F8 notice keep using <see cref="PermiteBusquedaPendiente"/>. A mark set in an archive state is kept when
    /// the carpeta moves to "No existe carpeta" and cleared when it leaves the set.
    /// </summary>
    public static bool PermiteEnProcesoDeBusqueda(EstadoCarpeta estado) =>
        PermiteBusquedaPendiente(estado)
        || estado is EstadoCarpeta.NoExisteCarpeta or EstadoCarpeta.CambioDomicilio or EstadoCarpeta.CambioDomicilioSolicitado;

    public void MarcarPendienteBusqueda(bool valor, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (valor && !PermiteEnProcesoDeBusqueda(Estado))
        {
            throw new DomainException("Solo se puede marcar pendiente de búsqueda una carpeta en Archivos, Of. 43 o No existe carpeta.");
        }

        if (valor != PendienteBusqueda)
        {
            Audit(now, usuario, nameof(PendienteBusqueda), SiNo(PendienteBusqueda), SiNo(valor));
            PendienteBusqueda = valor;
        }
    }

    private void LimpiarPendienteBusquedaSiCorresponde(string usuario, DateTime now)
    {
        if (PendienteBusqueda && !PermiteEnProcesoDeBusqueda(Estado))
        {
            Audit(now, usuario, nameof(PendienteBusqueda), SiNo(true), SiNo(false));
            PendienteBusqueda = false;
        }
    }

    private static string SiNo(bool v) => v ? "Si" : "No";

    /// <summary>Manual correction: any state, no transition check (the audit trail keeps who and when).</summary>
    public void ForzarEstado(EstadoCarpeta nuevo, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (!Enum.IsDefined(nuevo))
        {
            throw new DomainException("Estado inválido.");
        }
        if (nuevo != Estado)
        {
            AplicarEstado(nuevo, usuario, now);
        }
    }

    private static bool EsEstadoSubida(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.SubidaConaset or
        EstadoCarpeta.SubidaConF8 or
        EstadoCarpeta.CambioDomSubidoConaset or
        EstadoCarpeta.CambioDomSubidoCorreo or
        EstadoCarpeta.SubidaConOficio or
        EstadoCarpeta.PrimeraLicencia or
        EstadoCarpeta.Otorgado => true,
        _ => false
    };

    /// <summary>A final decision (Otorgado / Denegado) can never be changed: the only way back is the undo of the action that set it.</summary>
    private void RequireDecisionNoFinal()
    {
        if (Decision is Decision.Otorgado or Decision.Denegado)
        {
            throw new DomainException($"La decisión ya es final ({Decision}) y no se puede modificar.");
        }
    }

    public void CambiarDecision(Decision nueva, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (nueva != Decision)
        {
            RequireDecisionNoFinal();
            Audit(now, usuario, nameof(Decision), Decision.ToString(), nueva.ToString());
            Decision = nueva;

            if (nueva is Decision.Otorgado or Decision.Denegado && FechaSubida is null)
            {
                FechaSubida = HoraChile.Fecha(now);
                Audit(now, usuario, nameof(FechaSubida), null, FechaSubida.Value.ToString("yyyy-MM-dd"));
            }

            AjustarFechaCierre(usuario, now);
        }
    }

    public void Editar(Sede sede, DateOnly fechaCitacion, DateOnly? fechaSubida, string? idoneidadMoral, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        RequireSede(sede);
        RequireFechaCitacion(fechaCitacion);
        if (fechaSubida is { } subida && subida < fechaCitacion)
        {
            throw new DomainException("La fecha de subida no puede ser anterior a la fecha de citación.");
        }
        if (fechaSubida is { } futura && futura > HoraChile.Fecha(now))
        {
            throw new DomainException("La fecha de subida no puede ser futura.");
        }

        var idoneidad = string.IsNullOrWhiteSpace(idoneidadMoral) ? null : idoneidadMoral.Trim();
        if (idoneidad?.Length > MaxIdoneidadLength)
        {
            throw new DomainException($"Idoneidad moral excede {MaxIdoneidadLength} caracteres.");
        }

        if (sede != Sede)
        {
            Audit(now, usuario, nameof(Sede), Sede.ToString(), sede.ToString());
            Sede = sede;
        }

        if (fechaCitacion != FechaCitacion)
        {
            Audit(now, usuario, nameof(FechaCitacion), Format(FechaCitacion), Format(fechaCitacion));
            FechaCitacion = fechaCitacion;
        }

        if (fechaSubida != FechaSubida)
        {
            Audit(now, usuario, nameof(FechaSubida), Format(FechaSubida), Format(fechaSubida));
            FechaSubida = fechaSubida;
        }

        if (idoneidad != IdoneidadMoral)
        {
            Audit(now, usuario, nameof(IdoneidadMoral), IdoneidadMoral, idoneidad);
            IdoneidadMoral = idoneidad;
        }
    }

    public void ModificarRut(Rut nuevoRut, Ciudadano? ciudadanoExistente, string autor, DateTime now)
    {
        RequireActiva();
        ArgumentNullException.ThrowIfNull(nuevoRut);
        var usuario = RequireAutor(autor);
        if (Ciudadano.Rut != nuevoRut)
        {
            var anterior = Ciudadano.Rut.Formatted;
            if (ciudadanoExistente is not null && ciudadanoExistente.Id != CiudadanoId)
            {
                Ciudadano = ciudadanoExistente;
                CiudadanoId = ciudadanoExistente.Id;
            }
            else
            {
                Ciudadano.CambiarRut(nuevoRut);
            }
            Audit(now, usuario, "RUT", anterior, nuevoRut.Formatted);
        }
    }

    /// <summary>Corrects the person's name (applies to all their carpetas); audited on this carpeta.</summary>
    public void ModificarNombre(string nombreCompleto, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        var anterior = Ciudadano.NombreCompleto;
        if (Ciudadano.CambiarNombreCompleto(nombreCompleto))
        {
            Audit(now, usuario, "Nombre", anterior, Ciudadano.NombreCompleto);
        }
    }

    private void Audit(DateTime now, string usuario, string campo, string? anterior, string? nuevo) =>
        _historial.Add(new HistorialCambio(Id, now, usuario, campo, anterior, nuevo));

    private static string? Format(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string RequireAutor(string autor) => Autores.Requerir(autor);

    /// <summary>Adds a free audit line (e.g. a certificate or F8 event tied to this carpeta) to the history.</summary>
    public void RegistrarEvento(string campo, string? anterior, string? nuevo, string autor, DateTime now)
    {
        RequireActiva();
        Audit(now, RequireAutor(autor), campo, anterior, nuevo);
    }

    private static void RequireFechaCitacion(DateOnly fecha)
    {
        if (fecha == default)
        {
            throw new DomainException("La fecha de citación es obligatoria.");
        }
    }

    private static void RequireSede(Sede sede)
    {
        if (!Enum.IsDefined(sede))
        {
            throw new DomainException("Sede inválida.");
        }
    }
}
