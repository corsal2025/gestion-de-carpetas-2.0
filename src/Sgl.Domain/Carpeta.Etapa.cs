using System.Globalization;

namespace Sgl.Domain;

/// <summary>
/// Everything a state change drags with it: the automatic stage date, the closing date, the print mark of the
/// physical-search listing and the undo point used by "Deshacer". All dates are system-recorded, never invented.
/// </summary>
public sealed partial class Carpeta
{
    /// <summary>When the carpeta entered its current stage (UTC instant; shown in Chile time). Restamped on every state change.</summary>
    public DateTime FechaEstadoActual { get; private set; }

    /// <summary>
    /// Chile date on which the process ended: a closing state (uploads, certificate/oficio) or a final decision.
    /// Null while the process is open and for imported carpetas whose Excel brought no date.
    /// </summary>
    public DateOnly? FechaCierre { get; private set; }

    /// <summary>When the search listing that includes this carpeta was marked as printed (UTC). Only meaningful in Archivos / Of. 43.</summary>
    public DateTime? FechaImpresion { get; private set; }

    /// <summary>States that end the process under R9 (default A): the carpeta stays in its "Subida…" tray.</summary>
    public static bool EsEstadoCierre(EstadoCarpeta estado) =>
        estado is EstadoCarpeta.SubidaConaset
            or EstadoCarpeta.SubidaConF8
            or EstadoCarpeta.SubidaConOficio
            or EstadoCarpeta.CambioDomSubidoConaset
            or EstadoCarpeta.CambioDomSubidoCorreo
            or EstadoCarpeta.CambioDomSinRegistro
            or EstadoCarpeta.CambioDomCarpetaDigital
            or EstadoCarpeta.CrearCertificado
            or EstadoCarpeta.Otorgado
            or EstadoCarpeta.Denegado;

    private void AplicarEstado(EstadoCarpeta nuevo, string usuario, DateTime now)
    {
        // Checked before anything is written: a final decision is never reopened or flipped as a side effect of a state change.
        if (EstadoTransitions.DecisionFor(nuevo) != Decision)
        {
            RequireDecisionNoFinal();
        }

        Audit(now, usuario, nameof(Estado), Estado.ToString(), nuevo.ToString());
        Estado = nuevo;
        FechaEstadoActual = now;
        LimpiarPeticionSiEntraACertificado(usuario, now);
        LimpiarPendienteBusquedaSiCorresponde(usuario, now);
        LimpiarImpresionSiCorresponde(usuario, now);
        if (nuevo != EstadoCarpeta.CambioDomicilio)
        {
            LimpiarPendienteReclasificacion(usuario, now);
        }

        var decision = EstadoTransitions.DecisionFor(nuevo);
        if (decision != Decision)
        {
            Audit(now, usuario, nameof(Decision), Decision.ToString(), decision.ToString());
            Decision = decision;
        }

        // FechaSubida is only stamped when empty (an upload date typed or imported earlier is never overwritten) and
        // never before the appointment: an upload "before" its own citation would be an impossible date, so it stays
        // empty rather than invented.
        if (EsEstadoSubida(nuevo) && FechaSubida is null && HoraChile.Fecha(now) >= FechaCitacion)
        {
            var hoy = HoraChile.Fecha(now);
            Audit(now, usuario, nameof(FechaSubida), null, Format(hoy));
            FechaSubida = hoy;
        }

        AjustarFechaCierre(usuario, now);
    }

    /// <summary>Stamps the closing date when the process closes (once) and clears it when the carpeta is reopened.</summary>
    private void AjustarFechaCierre(string usuario, DateTime now)
    {
        var cerrada = EsEstadoCierre(Estado) || Decision != Decision.Pendiente;
        if (cerrada && FechaCierre is null)
        {
            var hoy = HoraChile.Fecha(now);
            Audit(now, usuario, nameof(FechaCierre), null, Format(hoy));
            FechaCierre = hoy;
        }
        else if (!cerrada && FechaCierre is not null)
        {
            Audit(now, usuario, nameof(FechaCierre), Format(FechaCierre), null);
            FechaCierre = null;
        }
    }

    // ---- Listado para búsqueda (R10) ----

    /// <summary>Records that the search listing containing this carpeta was printed. Audited; a reprint restamps it.</summary>
    public void MarcarImpresa(string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (!PermiteBusquedaPendiente(Estado))
        {
            throw new DomainException("Solo se puede marcar como impresa una carpeta en Archivos u Of. 43.");
        }

        Audit(now, usuario, nameof(FechaImpresion), FormatImpresion(FechaImpresion), FormatImpresion(now));
        FechaImpresion = now;
    }

    private void LimpiarImpresionSiCorresponde(string usuario, DateTime now)
    {
        if (FechaImpresion is not null && !PermiteBusquedaPendiente(Estado))
        {
            Audit(now, usuario, nameof(FechaImpresion), FormatImpresion(FechaImpresion), null);
            FechaImpresion = null;
        }
    }

    private static string? FormatImpresion(DateTime? utc) =>
        utc is { } v ? HoraChile.ALocal(v).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : null;

    // ---- Deshacer (R11) ----

    /// <summary>The values a state change may alter, captured before it to be able to undo it.</summary>
    public PuntoRestauracionEstado PuntoRestauracion() =>
        new(Estado, Decision, FechaSubida, FechaCierre, FechaEstadoActual, PendienteBusqueda, FechaImpresion, FechaPeticion, CarpetaEncontrada, Resolucion, FechaOtorgamiento, SentidoCambioDomicilio, PendienteReclasificacion);

    /// <summary>
    /// Undoes a state change: returns the carpeta to <paramref name="punto"/> (state and every side effect of the
    /// change). Refused when the state is no longer <paramref name="estadoEsperado"/> — someone changed it again and
    /// that newer decision must not be overwritten. A late undo also never reverts a final decision taken after the point
    /// was captured; only the undo of the «Otorgamiento» action itself (<paramref name="permiteDeshacerDecision"/>) may.
    /// </summary>
    public void RestaurarEstado(PuntoRestauracionEstado punto, EstadoCarpeta estadoEsperado, string autor, DateTime now, bool permiteDeshacerDecision = false)
    {
        ArgumentNullException.ThrowIfNull(punto);
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (Estado != estadoEsperado)
        {
            throw new DomainException("La carpeta cambió de estado después de ese movimiento: ya no se puede deshacer.");
        }
        if (!permiteDeshacerDecision && Decision != Decision.Pendiente && punto.Decision == Decision.Pendiente)
        {
            throw new DomainException("La carpeta ya tiene una decisión final: no se puede deshacer ese movimiento.");
        }

        Audit(now, usuario, nameof(Estado), Estado.ToString(), punto.Estado.ToString());
        Estado = punto.Estado;
        FechaEstadoActual = punto.FechaEstadoActual;

        if (Decision != punto.Decision)
        {
            Audit(now, usuario, nameof(Decision), Decision.ToString(), punto.Decision.ToString());
            Decision = punto.Decision;
        }
        if (FechaSubida != punto.FechaSubida)
        {
            Audit(now, usuario, nameof(FechaSubida), Format(FechaSubida), Format(punto.FechaSubida));
            FechaSubida = punto.FechaSubida;
        }
        if (FechaCierre != punto.FechaCierre)
        {
            Audit(now, usuario, nameof(FechaCierre), Format(FechaCierre), Format(punto.FechaCierre));
            FechaCierre = punto.FechaCierre;
        }
        FijarMarcas(punto.PendienteBusqueda, punto.CarpetaEncontrada, usuario, now);
        if (FechaImpresion != punto.FechaImpresion)
        {
            Audit(now, usuario, nameof(FechaImpresion), FormatImpresion(FechaImpresion), FormatImpresion(punto.FechaImpresion));
            FechaImpresion = punto.FechaImpresion;
        }
        if (FechaPeticion != punto.FechaPeticion)
        {
            Audit(now, usuario, nameof(FechaPeticion), FormatImpresion(FechaPeticion), FormatImpresion(punto.FechaPeticion));
            FechaPeticion = punto.FechaPeticion;
        }

        if (FechaOtorgamiento != punto.FechaOtorgamiento)
        {
            Audit(now, usuario, nameof(FechaOtorgamiento), Format(FechaOtorgamiento), Format(punto.FechaOtorgamiento));
            FechaOtorgamiento = punto.FechaOtorgamiento;
        }
        if (Resolucion != punto.Resolucion)
        {
            Audit(now, usuario, nameof(Resolucion), Resolucion?.ToString(), punto.Resolucion?.ToString());
            Resolucion = punto.Resolucion;
        }

        if (SentidoCambioDomicilio != punto.Sentido)
        {
            Audit(now, usuario, CampoDireccion, SentidoCambioDomicilio?.ToString(), punto.Sentido?.ToString());
            SentidoCambioDomicilio = punto.Sentido;
        }

        if (PendienteReclasificacion != punto.PendienteReclasificacion)
        {
            Audit(now, usuario, CampoReclasificacion, PendienteReclasificacion ? "pendiente" : null, punto.PendienteReclasificacion ? "pendiente" : null);
            PendienteReclasificacion = punto.PendienteReclasificacion;
        }

        Audit(now, usuario, "Deshacer", null, $"Estado revertido a {punto.Estado}");
    }

    // ---- Clasificación por fecha (R6) ----

    /// <summary>"Buscar carpeta física": Archivos before 2023-07-01, Of. 43 until the digital cut-off; null without a readable date or for a digital carpeta (no paper).</summary>
    public static EstadoCarpeta? DestinoPorFechaUltimaCarpeta(string? fechaUltimaCarpeta) =>
        DestinoPorFechaUltimaCarpeta(fechaUltimaCarpeta, SectorFisico.Corte);

    public static EstadoCarpeta? DestinoPorFechaUltimaCarpeta(string? fechaUltimaCarpeta, DateOnly corte) =>
        DestinoPorFechaUltimaCarpeta(fechaUltimaCarpeta, corte, SectorFisico.CorteDigital);

    public static EstadoCarpeta? DestinoPorFechaUltimaCarpeta(string? fechaUltimaCarpeta, DateOnly corte, DateOnly corteDigital) =>
        SectorFisico.Calcular(fechaUltimaCarpeta, corte, corteDigital) switch
        {
            SectorFisico.Archivo => EstadoCarpeta.SeEncuentraEnArchivos,
            SectorFisico.Oficina43 => EstadoCarpeta.SeEncuentraEnOf43,
            _ => null,
        };

    /// <summary>
    /// Classification options for a row: without a readable "última carpeta" date the physical-location states
    /// (Archivos / Of. 43) are not offered, since they are decided only by that date.
    /// </summary>
    public static IReadOnlyList<EstadoCarpeta> OpcionesClasificacionSegunFecha(
        IEnumerable<EstadoCarpeta> opciones, string? fechaUltimaCarpeta)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        if (DestinoPorFechaUltimaCarpeta(fechaUltimaCarpeta) is not null) return opciones.ToList();
        return opciones.Where(e => e is not (EstadoCarpeta.SeEncuentraEnArchivos or EstadoCarpeta.SeEncuentraEnOf43)).ToList();
    }

    /// <summary>
    /// Destination of a classification: an explicit state is always respected; otherwise the default action is
    /// "Buscar carpeta física" computed from the entered date (or, failing that, the one already stored).
    /// </summary>
    public static EstadoCarpeta ResolverDestinoClasificacion(EstadoCarpeta? explicito, string? fechaIngresada, string? fechaGuardada)
    {
        if (explicito is { } elegido)
        {
            return elegido;
        }

        var fecha = string.IsNullOrWhiteSpace(fechaIngresada) ? fechaGuardada : fechaIngresada;
        if (string.IsNullOrWhiteSpace(fecha))
        {
            throw new DomainException("Indique la fecha de la última carpeta o elija un estado para clasificar.");
        }

        if (SectorFisico.Calcular(fecha) == SectorFisico.Digital)
            throw new DomainException("Carpeta digital en SGL: no requiere búsqueda física. Elija un estado para continuar.");

        return DestinoPorFechaUltimaCarpeta(fecha)
            ?? throw new DomainException("La fecha de la última carpeta no es válida: use el formato dd-mm-aaaa.");
    }
}

/// <summary>Values altered by a state change; <see cref="Carpeta.RestaurarEstado"/> puts them back.</summary>
public sealed record PuntoRestauracionEstado(
    EstadoCarpeta Estado,
    Decision Decision,
    DateOnly? FechaSubida,
    DateOnly? FechaCierre,
    DateTime FechaEstadoActual,
    bool PendienteBusqueda,
    DateTime? FechaImpresion,
    DateTime? FechaPeticion = null,
    bool CarpetaEncontrada = false,
    Resolucion? Resolucion = null,
    DateOnly? FechaOtorgamiento = null,
    SentidoCambioDomicilio? Sentido = null,
    bool PendienteReclasificacion = false);
