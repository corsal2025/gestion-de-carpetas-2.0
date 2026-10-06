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
            carpeta.ForzarEstado(target, "setup", Now);
        }
        return carpeta;
    }

    private static EstadoCarpeta[]? PathTo(EstadoCarpeta target) => target switch
    {
        EstadoCarpeta.Citada => [],
        EstadoCarpeta.Revision => [EstadoCarpeta.Revision],
        EstadoCarpeta.PrimeraLicencia => [EstadoCarpeta.PrimeraLicencia],
        EstadoCarpeta.CambioDomicilio => [EstadoCarpeta.CambioDomicilio],
        EstadoCarpeta.Alertada => [EstadoCarpeta.Revision, EstadoCarpeta.Alertada],
        EstadoCarpeta.SubidaConaset => [EstadoCarpeta.Revision, EstadoCarpeta.SubidaConaset],
        EstadoCarpeta.EsperaExamen => [EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen],
        EstadoCarpeta.ClasePendiente => [EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen, EstadoCarpeta.ClasePendiente],
        EstadoCarpeta.Otorgado => [EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen, EstadoCarpeta.Otorgado],
        EstadoCarpeta.ParaDenegar => [EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen, EstadoCarpeta.ParaDenegar],
        EstadoCarpeta.Denegado => [EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen, EstadoCarpeta.ParaDenegar, EstadoCarpeta.Denegado],
        _ => null,
    };
}
