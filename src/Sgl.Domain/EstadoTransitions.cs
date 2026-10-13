namespace Sgl.Domain;

/// <summary>
/// State machine for a carpeta. Mirrors exactly the "Flujo de carpeta" stateDiagram
/// in docs/arquitectura.html; any edge not listed here is rejected.
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
            [EstadoCarpeta.CambioDomicilio] = [EstadoCarpeta.SubidaConaset],
            [EstadoCarpeta.EsperaExamen] = [EstadoCarpeta.ClasePendiente, EstadoCarpeta.Otorgado, EstadoCarpeta.ParaDenegar],
            [EstadoCarpeta.ClasePendiente] = [],
            [EstadoCarpeta.ParaDenegar] = [EstadoCarpeta.Denegado],
            [EstadoCarpeta.Otorgado] = [],
            [EstadoCarpeta.Denegado] = [],
            // Physical search (phase 3, R8): found -> uploaded to Conaset; not found -> F8 tray.
            [EstadoCarpeta.SeEncuentraEnArchivos] = [EstadoCarpeta.SubidaConaset, EstadoCarpeta.NoExisteCarpeta],
            [EstadoCarpeta.SeEncuentraEnOf43] = [EstadoCarpeta.SubidaConaset, EstadoCarpeta.NoExisteCarpeta],
            // A carpeta declared missing can still show up later, or be resolved by F8 / certificate / oficio.
            [EstadoCarpeta.NoExisteCarpeta] =
            [
                EstadoCarpeta.SubidaConF8, EstadoCarpeta.CrearCertificado, EstadoCarpeta.SubidaConOficio,
                EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.SeEncuentraEnOf43,
            ],
            [EstadoCarpeta.PorClasificar] =
            [
                EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.SubidaConaset, EstadoCarpeta.SubidaConF8,
                EstadoCarpeta.CambioDomSubidoConaset, EstadoCarpeta.CambioDomSubidoCorreo, EstadoCarpeta.SubidaConOficio,
                EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.SeEncuentraEnOf43, EstadoCarpeta.CambioDomicilioSolicitado,
                EstadoCarpeta.CambioDomicilio, EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.CrearCertificado,
                EstadoCarpeta.CanjeLicExtranjera,
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
