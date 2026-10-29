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
            // CambioDomSubidoCorreo: the requesting comuna's email was filed in "CARP. YA SUBIDAS" (mail sync).
            [EstadoCarpeta.CambioDomicilio] = [EstadoCarpeta.SubidaConaset, EstadoCarpeta.SubidaConF8, EstadoCarpeta.SubidaConOficio, EstadoCarpeta.CrearCertificado, EstadoCarpeta.CambioDomSubidoCorreo, EstadoCarpeta.CambioDomSubidoConaset, EstadoCarpeta.NoExisteCarpeta],
            // An F8 can be requested from here too: uploaded -> SubidaConF8; not generated -> Crear certificado.
            [EstadoCarpeta.CambioDomicilioSolicitado] = [EstadoCarpeta.SubidaConF8, EstadoCarpeta.SubidaConOficio, EstadoCarpeta.CrearCertificado, EstadoCarpeta.CambioDomSubidoConaset, EstadoCarpeta.NoExisteCarpeta],
            [EstadoCarpeta.EsperaExamen] = [EstadoCarpeta.ClasePendiente, EstadoCarpeta.Otorgado, EstadoCarpeta.ParaDenegar],
            [EstadoCarpeta.ClasePendiente] = [EstadoCarpeta.EsperaExamen, EstadoCarpeta.Otorgado, EstadoCarpeta.ParaDenegar],
            [EstadoCarpeta.ParaDenegar] = [EstadoCarpeta.Denegado],
            [EstadoCarpeta.Otorgado] = [],
            [EstadoCarpeta.Denegado] = [],
            // Physical search (phase 3, R8): found -> uploaded to Conaset; not found -> F8 tray; or certificate requested.
            [EstadoCarpeta.SeEncuentraEnArchivos] = [EstadoCarpeta.SubidaConaset, EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.CrearCertificado],
            [EstadoCarpeta.SeEncuentraEnOf43] = [EstadoCarpeta.SubidaConaset, EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.CrearCertificado],
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

    public static bool IsFinal(EstadoCarpeta estado) => estado is EstadoCarpeta.Otorgado or EstadoCarpeta.Denegado;

    public static Decision DecisionFor(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.Otorgado => Decision.Otorgado,
        EstadoCarpeta.Denegado => Decision.Denegado,
        _ => Decision.Pendiente,
    };
}
