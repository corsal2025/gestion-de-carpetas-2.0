using Sgl.Domain;

namespace Sgl.Domain.Tests;

internal static class CarpetaFactory
{
    public static readonly DateTime Now = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    public static Carpeta New() =>
        Carpeta.Registrar(
            new Ciudadano(Rut.Parse("12345678-5"), "Juan", "Pérez"),
            Sede.AvArgentina,
            new DateOnly(2026, 3, 10),
            "registrador",
            Now);

    // Walks a valid path from Citada to the requested state.
    public static Carpeta InEstado(EstadoCarpeta target)
    {
        var carpeta = New();
        if (target is EstadoCarpeta.Otorgado or EstadoCarpeta.Denegado)
        {
            // A final state is only reachable through the Otorgamiento module; tests that need a decided carpeta use the historical load.
            carpeta.CambiarEstado(EstadoCarpeta.PrimeraLicencia, "setup", Now);
            carpeta.CambiarEstado(EstadoCarpeta.EsperaExamen, "setup", Now);
            carpeta.AplicarCargaHistorica(target, EstadoTransitions.DecisionFor(target), null, "setup", Now);
            return carpeta;
        }

        var path = PathTo(target);
        if (path is not null)
        {
            foreach (var step in path)
            {
                carpeta.CambiarEstado(step, "setup", Now);
            }
        }
        else
        {
            // Forcing into the change-of-address family needs a direction: saliente by default; «Solicitado» uses entrante so the unnarrowed table is still exercised.
            carpeta.ForzarEstado(target, "setup", Now, target == EstadoCarpeta.CambioDomicilioSolicitado ? SentidoCambioDomicilio.Entrante : SentidoCambioDomicilio.Saliente);
        }
        return carpeta;
    }

    private static EstadoCarpeta[]? PathTo(EstadoCarpeta target) => target switch
    {
        EstadoCarpeta.Citada => [],
        EstadoCarpeta.PorClasificar => null,
        EstadoCarpeta.Revision => [EstadoCarpeta.Revision],
        EstadoCarpeta.PrimeraLicencia => [EstadoCarpeta.PrimeraLicencia],
        EstadoCarpeta.CambioDomicilio => [EstadoCarpeta.CambioDomicilio],
        EstadoCarpeta.Alertada => [EstadoCarpeta.Revision, EstadoCarpeta.Alertada],
        EstadoCarpeta.SubidaConaset => [EstadoCarpeta.Revision, EstadoCarpeta.SubidaConaset],
        EstadoCarpeta.EsperaExamen => [EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen],
        EstadoCarpeta.ClasePendiente => [EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen, EstadoCarpeta.ClasePendiente],
        EstadoCarpeta.ParaDenegar => [EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen, EstadoCarpeta.ParaDenegar],
        _ => null,
    };
}
