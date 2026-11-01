using System.Linq.Expressions;

namespace Sgl.Domain;

/// <summary>
/// Intermediate outcome of the «Otorgamiento» module for a carpeta that is not decided yet. It lives apart from
/// <see cref="Carpeta.Estado"/> so the channel the carpeta was uploaded through (Conaset, F8, oficio, canje...) is kept.
/// </summary>
public enum Resolucion
{
    EsperaExamen = 1,
    ClasePendiente = 2,
    ParaDenegar = 3,
}

public sealed partial class Carpeta
{
    /// <summary>Intermediate outcome chosen in Otorgamiento; null until someone picks one (legacy states derive theirs).</summary>
    public Resolucion? Resolucion { get; private set; }

    /// <summary>Chile date on which «Otorgada» was pressed (automatic). Null for denied or undecided carpetas.</summary>
    public DateOnly? FechaOtorgamiento { get; private set; }

    /// <summary>The stored resolution, or the one a legacy state (Espera examen / Clase pendiente / Para denegar) already means.</summary>
    public Resolucion? ResolucionEfectiva => Resolucion ?? ResolucionDeEstado(Estado);

    public static Resolucion? ResolucionDeEstado(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.EsperaExamen => Domain.Resolucion.EsperaExamen,
        EstadoCarpeta.ClasePendiente => Domain.Resolucion.ClasePendiente,
        EstadoCarpeta.ParaDenegar => Domain.Resolucion.ParaDenegar,
        _ => null,
    };

    /// <summary>
    /// States whose carpeta waits for the final decision in «Otorgamiento»: the five uploads, canje, 1° licencia and the
    /// three legacy decision states. They stay in their own tray as well (this is a derived view, never an assigned tray).
    /// </summary>
    public static bool EsParaOtorgar(EstadoCarpeta estado) =>
        estado is EstadoCarpeta.SubidaConaset
            or EstadoCarpeta.SubidaConF8
            or EstadoCarpeta.SubidaConOficio
            or EstadoCarpeta.CambioDomSubidoConaset
            or EstadoCarpeta.CambioDomSubidoCorreo
            or EstadoCarpeta.CanjeLicExtranjera
            or EstadoCarpeta.PrimeraLicencia
            or EstadoCarpeta.EsperaExamen
            or EstadoCarpeta.ClasePendiente
            or EstadoCarpeta.ParaDenegar;

    /// <summary>
    /// SQL-translatable «Para otorgar»: open, undecided, not archived in a real box, in one of the <see cref="EsParaOtorgar"/>
    /// states and not an entrante (a carpeta uploaded for another comuna is not decided here, whatever its state).
    /// </summary>
    public static readonly Expression<Func<Carpeta, bool>> ParaOtorgarExpresion = c =>
        !c.Anulada
        && c.Decision == Decision.Pendiente
        && c.SentidoCambioDomicilio != Domain.SentidoCambioDomicilio.Entrante
        && (c.CajaArchivo == null || c.CajaArchivo.Trim() == "" || c.CajaArchivo == ColaDeCaja)
        && (c.Estado == EstadoCarpeta.SubidaConaset
            || c.Estado == EstadoCarpeta.SubidaConF8
            || c.Estado == EstadoCarpeta.SubidaConOficio
            || c.Estado == EstadoCarpeta.CambioDomSubidoConaset
            || c.Estado == EstadoCarpeta.CambioDomSubidoCorreo
            || c.Estado == EstadoCarpeta.CanjeLicExtranjera
            || c.Estado == EstadoCarpeta.PrimeraLicencia
            || c.Estado == EstadoCarpeta.EsperaExamen
            || c.Estado == EstadoCarpeta.ClasePendiente
            || c.Estado == EstadoCarpeta.ParaDenegar);

    /// <summary>«Otorgada»: final decision, today's Chile date as the grant date; the carpeta moves to Cerradas.</summary>
    public void Otorgar(string autor, DateTime now)
    {
        var usuario = RequireDecidible(autor);
        CambiarDecision(Decision.Otorgado, usuario, now);
        var hoy = HoraChile.Fecha(now);
        Audit(now, usuario, nameof(FechaOtorgamiento), Format(FechaOtorgamiento), Format(hoy));
        FechaOtorgamiento = hoy;
        LimpiarResolucion(usuario, now);
    }

    public const string MensajeDecisionSoloEnOtorgamiento = "Otorgar o denegar se hace desde el módulo Otorgamiento.";

    /// <summary>History label for what <see cref="AplicarCargaHistorica"/> writes, so the origin of a decision outside Otorgamiento is never anonymous.</summary>
    public const string OrigenCargaHistorica = "Importación Excel (carga histórica)";

    /// <summary>
    /// Historical load of the master spreadsheet: the ONLY path besides «Otorgamiento» that may leave a final decision.
    /// It is exempt from <c>IPermisos</c> (no user decides here; the planilla is the record) and stamps
    /// <see cref="FechaOtorgamiento"/> only when the caller has a reliable date (never invented). Every use leaves an
    /// «Origen» line in the history. A decision already final is never overwritten.
    /// </summary>
    /// <param name="estadoFinal">Legacy final state the planilla implies (Otorgado / Denegado), or null to keep the current state.</param>
    public void AplicarCargaHistorica(EstadoCarpeta? estadoFinal, Decision decision, DateOnly? fechaOtorgamiento, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (estadoFinal is { } e && EstadoTransitions.DecisionFor(e) == Decision.Pendiente)
        {
            throw new DomainException("La carga histórica solo aplica estados finales; use el cambio de estado para el resto.");
        }

        var cambiaEstado = estadoFinal is { } destino && destino != Estado;
        var cambiaDecision = decision != Decision.Pendiente && decision != Decision;
        if (!cambiaEstado && !cambiaDecision)
        {
            return;
        }

        // Validates before writing anything: a final decision is never flipped by the planilla.
        if (cambiaEstado && EstadoTransitions.DecisionFor(estadoFinal!.Value) != Decision)
        {
            RequireDecisionNoFinal();
        }
        if (cambiaDecision)
        {
            RequireDecisionNoFinal();
        }

        Audit(now, usuario, "Origen", null, OrigenCargaHistorica);
        if (cambiaEstado)
        {
            AplicarEstado(estadoFinal!.Value, usuario, now);
        }
        if (decision != Decision.Pendiente)
        {
            CambiarDecision(decision, usuario, now);
        }
        if (Decision == Decision.Otorgado && fechaOtorgamiento is { } fecha && FechaOtorgamiento is null)
        {
            Audit(now, usuario, nameof(FechaOtorgamiento), null, Format(fecha));
            FechaOtorgamiento = fecha;
        }
    }

    /// <summary>«Denegada»: final decision without grant date.</summary>
    public void Denegar(string autor, DateTime now)
    {
        var usuario = RequireDecidible(autor);
        CambiarDecision(Decision.Denegado, usuario, now);
        LimpiarResolucion(usuario, now);
    }

    /// <summary>Sets the intermediate outcome (Espera examen / Clase pendiente / Para denegar); audited, editable until the final decision.</summary>
    public void AsignarResolucion(Resolucion nueva, string autor, DateTime now)
    {
        var usuario = RequireDecidible(autor);
        if (!Enum.IsDefined(nueva))
        {
            throw new DomainException("Resolución inválida.");
        }
        if (nueva != ResolucionEfectiva)
        {
            Audit(now, usuario, nameof(Resolucion), ResolucionEfectiva?.ToString(), nueva.ToString());
        }
        Resolucion = nueva;
    }

    private void LimpiarResolucion(string usuario, DateTime now)
    {
        if (Resolucion is { } anterior)
        {
            Audit(now, usuario, nameof(Resolucion), anterior.ToString(), null);
            Resolucion = null;
        }
    }

    /// <summary>Only an open carpeta in a «para otorgar» state may be decided or given a resolution.</summary>
    private string RequireDecidible(string autor)
    {
        RequireActiva();
        if (!EsParaOtorgar(Estado))
        {
            throw new DomainException("Solo se decide en Otorgamiento una carpeta ya subida (o en 1° Licencia o Canje).");
        }
        if (SentidoCambioDomicilio == Domain.SentidoCambioDomicilio.Entrante)
        {
            throw new DomainException("Una carpeta pedida por otra comuna no se otorga ni se deniega aquí.");
        }
        RequireDecisionNoFinal();
        return RequireAutor(autor);
    }
}
