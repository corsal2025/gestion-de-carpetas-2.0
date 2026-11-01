namespace Sgl.Domain;

/// <summary>Who started a change-of-address case: it decides which closing paths the carpeta may take.</summary>
public enum SentidoCambioDomicilio
{
    /// <summary>A person of our agenda asks us to request their carpeta from another comuna.</summary>
    Saliente = 1,

    /// <summary>Another comuna asks us (by email) to upload the carpeta of someone who lived here.</summary>
    Entrante = 2,
}

public sealed partial class Carpeta
{
    public const int MaxMotivoCorreccionLength = 200;

    /// <summary>
    /// Direction of the change-of-address case; null for carpetas outside that family. It is set when the case is born
    /// (classification fixes <see cref="SentidoCambioDomicilio.Saliente"/>, the mail sync <see cref="SentidoCambioDomicilio.Entrante"/>)
    /// and survives the later states, so an entrante uploaded with F8 is still known to be entrante.
    /// </summary>
    public SentidoCambioDomicilio? SentidoCambioDomicilio { get; private set; }

    /// <summary>
    /// Chile date on which someone reviewed the carpeta one by one in «Por pedir» (stored by the review mode of the next stage);
    /// null = not reviewed yet.
    /// </summary>
    public DateOnly? RevisadaEn { get; private set; }

    /// <summary>
    /// True while a carpeta that just entered «Cambio de domicilio» (classified as such, or born from an email) still waits for a
    /// person to confirm it in «Reclasificación». It cannot go on to «Por pedir» / «Solicitado» nor to the «Nos piden carpetas» actions
    /// until confirmed. It is not a state: the carpeta keeps its state, citation date and direction.
    /// </summary>
    public bool PendienteReclasificacion { get; private set; }

    /// <summary>History field of the reclassification stage.</summary>
    public const string CampoReclasificacion = "Reclasificación";

    public const string MensajeReclasificacionPendiente =
        "La carpeta está pendiente de reclasificación: confírmela primero en «Reclasificación».";

    /// <summary>History field that records the direction of the case.</summary>
    public const string CampoDireccion = "Dirección";

    /// <summary>History field that records why a classification was corrected.</summary>
    public const string CampoCorreccionClasificacion = "Corrección de clasificación";

    /// <summary>History field of the one-by-one review in «Por pedir».</summary>
    public const string CampoRevision = "Revisión en Conaset";

    /// <summary>History field of the request mail.</summary>
    public const string CampoPedido = "Pedido";

    /// <summary>Destinations a wrongly classified change-of-address carpeta can be sent to (the five classifications + staying).</summary>
    public static IReadOnlyList<EstadoCarpeta> DestinosCorreccionCambioDom { get; } =
    [
        EstadoCarpeta.PrimeraLicencia,
        EstadoCarpeta.SeEncuentraEnArchivos,
        EstadoCarpeta.SeEncuentraEnOf43,
        EstadoCarpeta.NoExisteCarpeta,
        EstadoCarpeta.CanjeLicExtranjera,
        EstadoCarpeta.CambioDomicilio,
    ];

    /// <summary>Change-of-address states plus the two entrante closings: the carpetas that carry a direction while they live here.</summary>
    public static bool EsFamiliaCambioDomicilio(EstadoCarpeta estado) =>
        EsCambioDomicilio(estado) || estado is EstadoCarpeta.CambioDomSinRegistro or EstadoCarpeta.CambioDomCarpetaDigital;

    private void FijarSentido(SentidoCambioDomicilio? nuevo, string usuario, DateTime now)
    {
        if (nuevo == SentidoCambioDomicilio)
        {
            return;
        }
        Audit(now, usuario, CampoDireccion, SentidoCambioDomicilio?.ToString(), nuevo?.ToString());
        SentidoCambioDomicilio = nuevo;
    }

    /// <summary>
    /// A person finds out the carpeta was not a change of address (or not what the email said) and sends it to the right
    /// place: 1° licencia, Archivos / Of. 43 (decided by the last-carpeta date), «No existe carpeta», canje, or back to
    /// «cambio de domicilio». Only before it closes. The reason is mandatory and stays in the history; leaving the family
    /// removes the direction (the undo point brings it back).
    /// </summary>
    public void CorregirClasificacionCambioDom(
        EstadoCarpeta destino, string motivo, string? fechaUltimaCarpeta, string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (Estado is not (EstadoCarpeta.CambioDomicilio or EstadoCarpeta.CambioDomicilioSolicitado))
        {
            throw new DomainException("Solo se corrige la clasificación de un cambio de domicilio que aún no se cierra.");
        }
        if (!DestinosCorreccionCambioDom.Contains(destino))
        {
            throw new DomainException("Elija una clasificación válida: 1° licencia, Archivos / Of. 43, No existe carpeta, canje o cambio de domicilio.");
        }

        var razon = motivo?.Trim();
        if (string.IsNullOrEmpty(razon))
        {
            throw new DomainException("Indique el motivo de la corrección.");
        }
        if (razon.Length > MaxMotivoCorreccionLength)
        {
            throw new DomainException($"El motivo excede {MaxMotivoCorreccionLength} caracteres.");
        }

        var final = destino;
        if (destino is EstadoCarpeta.SeEncuentraEnArchivos or EstadoCarpeta.SeEncuentraEnOf43)
        {
            final = DestinoPorFechaUltimaCarpeta(fechaUltimaCarpeta)
                ?? throw new DomainException("Indique una fecha de última carpeta válida (dd-mm-aaaa) para ubicar la carpeta física.");
            if (final != destino)
            {
                throw new DomainException(final == EstadoCarpeta.SeEncuentraEnArchivos
                    ? "Con esa fecha la carpeta está en Archivos, no en Of. 43."
                    : "Con esa fecha la carpeta está en Of. 43, no en Archivos.");
            }
            ModificarFechaUltimaCarpeta(fechaUltimaCarpeta, usuario, now);
        }

        Audit(now, usuario, CampoCorreccionClasificacion, null, razon);
        if (final != Estado)
        {
            ForzarEstado(final, usuario, now);
        }
        if (!EsFamiliaCambioDomicilio(final))
        {
            FijarSentido(null, usuario, now);
        }
        LimpiarPendienteReclasificacion(usuario, now);
    }

    /// <summary>
    /// A person checked the classification of a carpeta that just entered «Cambio de domicilio» and it is right: the carpeta
    /// leaves «Reclasificación». State, stage date, citation date and direction stay as they are; the history keeps the line.
    /// </summary>
    public void ConfirmarReclasificacion(string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (!PendienteReclasificacion)
        {
            throw new DomainException("La carpeta ya no está pendiente de reclasificación: actualice la lista.");
        }

        Audit(now, usuario, CampoReclasificacion, "pendiente", "confirmada");
        PendienteReclasificacion = false;
    }

    /// <summary>The carpeta must have gone through «Reclasificación» before the next stage may touch it.</summary>
    private void RequireReclasificada()
    {
        if (PendienteReclasificacion)
        {
            throw new DomainException(MensajeReclasificacionPendiente);
        }
    }

    /// <summary>States of the family that come after the «Reclasificación» gate: no generic route may reach them while the carpeta is pending.</summary>
    public static bool EsPosteriorALaPuertaDeReclasificacion(EstadoCarpeta estado) =>
        estado is EstadoCarpeta.CambioDomicilioSolicitado
            or EstadoCarpeta.CambioDomSubidoConaset
            or EstadoCarpeta.CambioDomSubidoCorreo
            or EstadoCarpeta.CambioDomSinRegistro
            or EstadoCarpeta.CambioDomCarpetaDigital;

    /// <summary>Where a wrongly classified entrante can go: 1° licencia or canje. Single source for «Reclasificación» and «Nos piden carpetas».</summary>
    public static IReadOnlyList<EstadoCarpeta> DestinosCorreccionEntrante { get; } =
        [EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.CanjeLicExtranjera];

    public const string MensajeEntrantePendienteSoloReclasificacion =
        "La carpeta entrante está pendiente de reclasificación: confírmela o corríjala en «Reclasificación» antes de cambiar su estado.";

    /// <summary>The only destinations a pending entrante accepts through the generic state routes: staying, or the entrante corrections.</summary>
    public static bool PermiteEstadoSiEntrantePendiente(EstadoCarpeta destino) =>
        destino == EstadoCarpeta.CambioDomicilio || DestinosCorreccionEntrante.Contains(destino);

    private void RequireReclasificadaParaEstado(EstadoCarpeta destino)
    {
        if (EsPosteriorALaPuertaDeReclasificacion(destino))
        {
            RequireReclasificada();
        }
        if (PendienteReclasificacion
            && SentidoCambioDomicilio == Domain.SentidoCambioDomicilio.Entrante
            && !PermiteEstadoSiEntrantePendiente(destino))
        {
            throw new DomainException(MensajeEntrantePendienteSoloReclasificacion);
        }
    }

    private void MarcarPendienteReclasificacion() => PendienteReclasificacion = true;

    private void LimpiarPendienteReclasificacion(string usuario, DateTime now)
    {
        if (PendienteReclasificacion)
        {
            Audit(now, usuario, CampoReclasificacion, "pendiente", null);
            PendienteReclasificacion = false;
        }
    }

    /// <summary>
    /// Someone checked this saliente in Conaset (by RUT) and it is still not uploaded: it stays in «Por pedir», ready for the
    /// mass send. Stores today's Chile date and leaves a line in the history.
    /// </summary>
    public void MarcarRevisada(string autor, DateTime now)
    {
        RequireActiva();
        RequireReclasificada();
        var usuario = RequireAutor(autor);
        if (Estado != EstadoCarpeta.CambioDomicilio || SentidoCambioDomicilio != Domain.SentidoCambioDomicilio.Saliente)
        {
            throw new DomainException("Solo se revisa una carpeta saliente que está por pedir.");
        }

        var hoy = HoraChile.Fecha(now);
        Audit(now, usuario, CampoRevision, Format(RevisadaEn), $"Pendiente: sigue sin subir, queda lista para pedir ({Format(hoy)})");
        RevisadaEn = hoy;
    }

    /// <summary>
    /// The request mail to <paramref name="comuna"/> left: the saliente moves from «Por pedir» to «Solicitado» and the history
    /// says «Pedido por correo · comuna».
    /// </summary>
    public void MarcarPedidaPorCorreo(string comuna, string autor, DateTime now)
    {
        RequireActiva();
        RequireReclasificada();
        var usuario = RequireAutor(autor);
        if (Estado != EstadoCarpeta.CambioDomicilio || SentidoCambioDomicilio != Domain.SentidoCambioDomicilio.Saliente)
        {
            throw new DomainException("Solo se pide por correo una carpeta saliente que está por pedir.");
        }

        CambiarEstado(EstadoCarpeta.CambioDomicilioSolicitado, usuario, now);
        Audit(now, usuario, CampoPedido, null, $"Pedido por correo · {comuna}");
    }

    /// <summary>
    /// The request mail was repeated by hand («volver a pedir») while the saliente waits in «Solicitado». Only a reminder: the
    /// state and the deadline (counted from the citation) do not change; the history keeps the date.
    /// </summary>
    public void RegistrarVueltaAPedir(string comuna, string autor, DateTime now)
    {
        RequireEnSeguimiento("Solo se vuelve a pedir una carpeta saliente que está solicitada.");
        Audit(now, RequireAutor(autor), CampoPedido, null, $"Vuelto a pedir por correo · {comuna} (recordatorio: el plazo sigue contando desde la cita)");
    }

    /// <summary>The other comuna uploaded the carpeta to Conaset (checked by hand): «Cambio dom. subido a Conaset».</summary>
    public void MarcarSubidaPorLaComuna(string autor, DateTime now)
    {
        RequireEnSeguimiento("Solo se marca como subida una carpeta saliente que está solicitada.");
        var usuario = RequireAutor(autor);
        CambiarEstado(EstadoCarpeta.CambioDomSubidoConaset, usuario, now);
        Audit(now, usuario, CampoRevision, null, "Ya subida: la otra comuna la subió a Conaset");
    }

    /// <summary>
    /// The deadline passed (or the user decided so) and the screenshot of the sent mail was uploaded to the platform as proof:
    /// «Cambio dom. subido con correo». The caller makes sure the screenshot exists.
    /// </summary>
    public void MarcarSubidaConCorreo(string autor, DateTime now)
    {
        RequireEnSeguimiento("Solo se sube la prueba de una carpeta saliente que está solicitada.");
        var usuario = RequireAutor(autor);
        CambiarEstado(EstadoCarpeta.CambioDomSubidoCorreo, usuario, now);
        Audit(now, usuario, CampoPedido, null, "Prueba subida: pantallazo del correo enviado a la comuna");
    }

    private void RequireEnSeguimiento(string mensaje)
    {
        RequireActiva();
        if (Estado != EstadoCarpeta.CambioDomicilioSolicitado || SentidoCambioDomicilio != Domain.SentidoCambioDomicilio.Saliente)
        {
            throw new DomainException(mensaje);
        }
    }
}
