namespace Sgl.Domain;

/// <summary>
/// State machine for a carpeta: any edge not listed here is rejected by <c>CambiarEstado</c>. docs/arquitectura.html
/// summarizes the main flow only; this table is the source of truth (manual corrections use <c>ForzarEstado</c>).
/// </summary>
public static class EstadoTransitions
{
    private static readonly IReadOnlyDictionary<EstadoCarpeta, EstadoCarpeta[]> Edges =
        new Dictionary<EstadoCarpeta, EstadoCarpeta[]>
        {
            [EstadoCarpeta.Citada] = [EstadoCarpeta.Revision, EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.CambioDomicilio],
            [EstadoCarpeta.Revision] = [EstadoCarpeta.Alertada, EstadoCarpeta.SubidaConaset],
            [EstadoCarpeta.Alertada] = [EstadoCarpeta.ParaDenegar],
            [EstadoCarpeta.SubidaConaset] = [EstadoCarpeta.EsperaExamen],
            [EstadoCarpeta.PrimeraLicencia] = [EstadoCarpeta.EsperaExamen],
            // Union of both directions; the overloads taking a <see cref="SentidoCambioDomicilio"/> narrow it (see NextStates).
            // CambioDomSubidoCorreo is only for a saliente already requested («Solicitado») whose proof was uploaded.
            [EstadoCarpeta.CambioDomicilio] = [EstadoCarpeta.SubidaConaset, EstadoCarpeta.SubidaConF8, EstadoCarpeta.SubidaConOficio, EstadoCarpeta.CrearCertificado, EstadoCarpeta.CambioDomSubidoCorreo, EstadoCarpeta.CambioDomSubidoConaset, EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.CambioDomicilioSolicitado, EstadoCarpeta.CambioDomSinRegistro, EstadoCarpeta.CambioDomCarpetaDigital],
            // An F8 can be requested from here too (entrante): uploaded -> SubidaConF8; not generated -> Crear certificado.
            [EstadoCarpeta.CambioDomicilioSolicitado] = [EstadoCarpeta.SubidaConF8, EstadoCarpeta.SubidaConOficio, EstadoCarpeta.CrearCertificado, EstadoCarpeta.CambioDomSubidoConaset, EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.CambioDomSubidoCorreo],
            [EstadoCarpeta.EsperaExamen] = [EstadoCarpeta.ClasePendiente, EstadoCarpeta.Otorgado, EstadoCarpeta.ParaDenegar],
            [EstadoCarpeta.ClasePendiente] = [EstadoCarpeta.EsperaExamen, EstadoCarpeta.Otorgado, EstadoCarpeta.ParaDenegar],
            [EstadoCarpeta.ParaDenegar] = [EstadoCarpeta.Denegado],
            [EstadoCarpeta.Otorgado] = [],
            [EstadoCarpeta.Denegado] = [],
            // Physical search (phase 3, R8): found -> uploaded to Conaset; not found -> "No existe carpeta" (F8 first).
            // No direct edge to Crear certificado: only when the F8 cannot be generated (see NoExisteCarpeta below).
            [EstadoCarpeta.SeEncuentraEnArchivos] = [EstadoCarpeta.SubidaConaset, EstadoCarpeta.NoExisteCarpeta],
            [EstadoCarpeta.SeEncuentraEnOf43] = [EstadoCarpeta.SubidaConaset, EstadoCarpeta.NoExisteCarpeta],
            // A carpeta declared missing is uploaded with F8 or with an oficio; it never goes back to the shelves.
            // F8 not generated -> Crear certificado; the signed certificate / oficio then uploads it (SubidaConOficio).
            [EstadoCarpeta.NoExisteCarpeta] = [EstadoCarpeta.SubidaConF8, EstadoCarpeta.SubidaConOficio, EstadoCarpeta.CrearCertificado],
            [EstadoCarpeta.CrearCertificado] = [EstadoCarpeta.SubidaConOficio],
            [EstadoCarpeta.PorClasificar] =
            [
                EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.SubidaConaset, EstadoCarpeta.SubidaConF8,
                EstadoCarpeta.CambioDomSubidoConaset, EstadoCarpeta.CambioDomSubidoCorreo, EstadoCarpeta.SubidaConOficio,
                EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.SeEncuentraEnOf43, EstadoCarpeta.CambioDomicilioSolicitado,
                EstadoCarpeta.CambioDomicilio, EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.CrearCertificado,
                EstadoCarpeta.CanjeLicExtranjera, EstadoCarpeta.Citada,
            ],
        };

    public static IReadOnlyList<EstadoCarpeta> NextStates(EstadoCarpeta from) =>
        Edges.TryGetValue(from, out var next) ? next : [];

    public static bool CanTransition(EstadoCarpeta from, EstadoCarpeta to) => NextStates(from).Contains(to);

    /// <summary>
    /// Edges of a change-of-address carpeta by direction. A saliente asks another comuna for the carpeta of one of our
    /// people: it has no F8, certificate or oficio, goes «Por pedir» → «Solicitado» and ends uploaded by the comuna or
    /// with the proof of the email. An entrante is a request from another comuna: it never goes through «Solicitado» and
    /// never ends «con correo». Any other estado (or a carpeta without direction) keeps the plain table.
    /// </summary>
    public static IReadOnlyList<EstadoCarpeta> NextStates(EstadoCarpeta from, SentidoCambioDomicilio? sentido) =>
        (sentido, from) switch
        {
            (SentidoCambioDomicilio.Saliente, EstadoCarpeta.CambioDomicilio) =>
                [EstadoCarpeta.CambioDomicilioSolicitado, EstadoCarpeta.CambioDomSubidoConaset],
            (SentidoCambioDomicilio.Saliente, EstadoCarpeta.CambioDomicilioSolicitado) =>
                [EstadoCarpeta.CambioDomSubidoConaset, EstadoCarpeta.CambioDomSubidoCorreo],
            (SentidoCambioDomicilio.Entrante, EstadoCarpeta.CambioDomicilio) =>
                NextStates(from).Where(e => e is not (EstadoCarpeta.CambioDomicilioSolicitado or EstadoCarpeta.CambioDomSubidoCorreo)).ToList(),
            _ => NextStates(from),
        };

    public static bool CanTransition(EstadoCarpeta from, EstadoCarpeta to, SentidoCambioDomicilio? sentido) =>
        NextStates(from, sentido).Contains(to);

    /// <summary>
    /// Manual corrections skip the table, but a saliente still never reaches the entrante-only closings nor the F8 /
    /// certificate / oficio / «No existe carpeta» / «Subida a Conaset» states from «Por pedir» or «Solicitado», and never
    /// «subido con correo» (that one needs the screenshot proof: <c>MarcarSubidaConCorreo</c>). An entrante never reaches
    /// «Solicitado» nor «subido con correo» from «Por pedir».
    /// </summary>
    public static bool VetaForzadoSaliente(EstadoCarpeta actual, EstadoCarpeta nuevo, SentidoCambioDomicilio? sentido) =>
        (sentido == SentidoCambioDomicilio.Saliente
            && actual is EstadoCarpeta.CambioDomicilio or EstadoCarpeta.CambioDomicilioSolicitado
            && nuevo is EstadoCarpeta.NoExisteCarpeta or EstadoCarpeta.SubidaConF8 or EstadoCarpeta.SubidaConOficio
                or EstadoCarpeta.CrearCertificado or EstadoCarpeta.SubidaConaset
                or EstadoCarpeta.CambioDomSinRegistro or EstadoCarpeta.CambioDomCarpetaDigital
                or EstadoCarpeta.CambioDomSubidoCorreo)
        || (sentido == SentidoCambioDomicilio.Entrante
            && actual is EstadoCarpeta.CambioDomicilio
            && nuevo is EstadoCarpeta.CambioDomicilioSolicitado or EstadoCarpeta.CambioDomSubidoCorreo);

    public static bool IsFinal(EstadoCarpeta estado) => estado is EstadoCarpeta.Otorgado or EstadoCarpeta.Denegado;

    public static Decision DecisionFor(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.Otorgado => Decision.Otorgado,
        EstadoCarpeta.Denegado => Decision.Denegado,
        _ => Decision.Pendiente,
    };
}
