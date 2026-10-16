using Microsoft.AspNetCore.Mvc.RazorPages;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Pages;

/// <summary>
/// Cómo trabajan las comunas: cuántas carpetas piden, cuánto tardan en subirlas a CONASET,
/// cuántas terminamos subiendo nosotros. Todo sale de las peticiones enviadas y de la fecha
/// <see cref="Peticion.SubidaEn"/> (que llega desde el Excel o del cierre manual).
/// </summary>
public sealed class EstadisticasModel : PageModel
{
    private readonly PeticionRepository _repository;

    public EstadisticasModel(PeticionRepository repository) => _repository = repository;

    public int TotalEnviadas { get; private set; }
    public int ComunasConEnvios { get; private set; }
    public int Cerradas { get; private set; }
    public int Abiertas { get; private set; }
    public int SubioComuna { get; private set; }
    public int SubimosNosotros { get; private set; }
    public int DentroDePlazo { get; private set; }
    public double? PorcentajeEnPlazo { get; private set; }
    public double? DemoraPromedio { get; private set; }

    public IReadOnlyList<(string Comuna, int Cantidad)> RankingVolumen { get; private set; } =
        Array.Empty<(string, int)>();
    public int VolumenMaximo { get; private set; } = 1;

    public IReadOnlyList<ComunaStat> PorComuna { get; private set; } = Array.Empty<ComunaStat>();

    public sealed record ComunaStat(
        string Comuna,
        int Solicitadas,
        int SubioComuna,
        int SubimosNosotros,
        int Cerradas,
        double? DemoraPromedio,
        double? PorcentajeEnPlazo);

    private static int? DemoraHabiles(Peticion p)
    {
        if (p.EnviadaEn is not { } enviada || p.SubidaEn is not { } subida)
        {
            return null;
        }

        var inicio = DateOnly.FromDateTime(enviada.LocalDateTime);
        return subida <= inicio ? 0 : DeadlineCalculator.BusinessDaysBetween(inicio, subida);
    }

    public void OnGet()
    {
        var enviadas = _repository.All()
            .Where(p => p.Estado == EstadoPeticion.Enviada && p.EnviadaEn is not null)
            .ToList();

        TotalEnviadas = enviadas.Count;
        ComunasConEnvios = enviadas
            .Select(p => p.Comuna)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var cerradas = enviadas.Where(p => p.SubidaEn is not null).ToList();
        Cerradas = cerradas.Count;
        Abiertas = TotalEnviadas - Cerradas;
        SubioComuna = cerradas.Count(p => EstadoCarpetaCatalog.SubidaPorComuna(p.EstadoCarpeta));
        SubimosNosotros = cerradas.Count(p => EstadoCarpetaCatalog.SubidaPorNosotros(p.EstadoCarpeta));

        var demoras = cerradas
            .Select(DemoraHabiles)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();

        if (demoras.Count > 0)
        {
            DemoraPromedio = demoras.Average();
            DentroDePlazo = demoras.Count(d => d <= DeadlineCalculator.PlazoDiasHabiles);
            PorcentajeEnPlazo = (double)DentroDePlazo / demoras.Count * 100;
        }

        RankingVolumen = enviadas
            .GroupBy(p => p.Comuna, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Comuna: g.Key, Cantidad: g.Count()))
            .OrderByDescending(x => x.Cantidad)
            .ThenBy(x => x.Comuna, StringComparer.CurrentCulture)
            .Take(12)
            .ToList();
        VolumenMaximo = RankingVolumen.Count > 0 ? RankingVolumen.Max(x => x.Cantidad) : 1;

        PorComuna = enviadas
            .GroupBy(p => p.Comuna, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var cerr = g.Where(p => p.SubidaEn is not null).ToList();
                var ds = cerr
                    .Select(DemoraHabiles)
                    .Where(d => d is not null)
                    .Select(d => d!.Value)
                    .ToList();

                return new ComunaStat(
                    g.Key,
                    g.Count(),
                    cerr.Count(p => EstadoCarpetaCatalog.SubidaPorComuna(p.EstadoCarpeta)),
                    cerr.Count(p => EstadoCarpetaCatalog.SubidaPorNosotros(p.EstadoCarpeta)),
                    cerr.Count,
                    ds.Count > 0 ? ds.Average() : null,
                    ds.Count > 0
                        ? (double)ds.Count(d => d <= DeadlineCalculator.PlazoDiasHabiles) / ds.Count * 100
                        : null);
            })
            .OrderByDescending(s => s.DemoraPromedio ?? -1)
            .ThenByDescending(s => s.Solicitadas)
            .ThenBy(s => s.Comuna, StringComparer.CurrentCulture)
            .ToList();
    }
}
