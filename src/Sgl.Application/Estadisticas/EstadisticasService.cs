using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Estadisticas;

public sealed class EstadisticasService(IEstadisticasQuery query, TimeProvider clock, BandejasOptions? bandejas = null)
{
    private const int DiasMaximos = 3650;

    private static readonly Bandeja[] BandejasAbiertas =
        [Bandeja.PorClasificar, Bandeja.ParaRevision, Bandeja.PorPreparar, Bandeja.CambioDomicilio, Bandeja.CambioDomicilioSolicitado, Bandeja.SinCarpetaF8,
         Bandeja.SubidaConaset, Bandeja.SubidaConF8, Bandeja.SubidaConOficio, Bandeja.CambioDomSubidoConaset, Bandeja.CambioDomSubidoCorreo,
         Bandeja.SeEncuentraEnArchivos, Bandeja.SeEncuentraEnOf43, Bandeja.CrearCertificado, Bandeja.CanjeLicExtranjera];

    public Task<EstadisticasDto> ObtenerAsync(FiltroEstadisticas filtro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var opciones = bandejas ?? new BandejasOptions();
        var normalizado = filtro with
        {
            DiasSinSubir = Math.Clamp(filtro.DiasSinSubir, 1, DiasMaximos),
            DiasBusquedaAbierta = Math.Clamp(filtro.DiasBusquedaAbierta, 0, DiasMaximos),
            UmbralRojo = BandejasAbiertas.ToDictionary(
                b => b,
                b => opciones.Umbrales.TryGetValue(b, out var u) ? u.Rojo : new UmbralDias().Rojo),
        };
        var hoy = clock.HoyChile();
        return query.ObtenerAsync(normalizado, hoy, ct);
    }
}
