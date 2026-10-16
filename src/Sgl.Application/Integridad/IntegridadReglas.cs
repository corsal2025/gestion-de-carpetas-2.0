using Sgl.Domain;

namespace Sgl.Application.Integridad;

/// <summary>The five rules that must never be broken (spec R12).</summary>
public enum ReglaIntegridad
{
    /// <summary>A carpeta appears in exactly one tray, the one its state dictates.</summary>
    UnaSolaBandeja = 1,

    /// <summary>The sum of the trays equals the total of "Todas" (cancelled excluded), also in the home panel.</summary>
    SumaBandejas = 2,

    /// <summary>No two open carpetas of the same RUT share a license class.</summary>
    DuplicadoMismaClase = 3,

    /// <summary>A cancelled carpeta never shows in modules, counters or statistics.</summary>
    AnuladaVisible = 4,

    /// <summary>No impossible dates: FechaSubida before FechaCitacion, FechaCierre in the future.</summary>
    FechasImposibles = 5,
}

public static class ReglasIntegridadInfo
{
    public static string Codigo(ReglaIntegridad regla) => $"R{(int)regla}";

    public static string Titulo(ReglaIntegridad regla) => regla switch
    {
        ReglaIntegridad.UnaSolaBandeja => "Carpeta en más de una bandeja (o en ninguna)",
        ReglaIntegridad.SumaBandejas => "La suma de las bandejas no coincide con el total",
        ReglaIntegridad.DuplicadoMismaClase => "Dos carpetas abiertas del mismo RUT con la misma clase",
        ReglaIntegridad.AnuladaVisible => "Carpeta anulada visible en módulos o contadores",
        ReglaIntegridad.FechasImposibles => "Fechas imposibles",
        _ => regla.ToString(),
    };
}

/// <summary>A carpeta reduced to what the rules need (cancelled ones included).</summary>
public sealed record CarpetaIntegridad(
    Guid Id,
    Guid CiudadanoId,
    string Rut,
    EstadoCarpeta Estado,
    Decision Decision,
    string? CajaArchivo,
    ClaseLicencia Clases,
    bool Anulada,
    DateOnly FechaCitacion,
    DateOnly? FechaSubida,
    DateOnly? FechaCierre,
    DateTime FechaRegistro);

/// <summary>
/// Everything the rules look at, gathered from the real pipelines (the tray predicates, the grouped counters and
/// the panel), so a divergence between implementations shows up as a violation.
/// </summary>
public sealed record EntradaIntegridad(
    IReadOnlyList<CarpetaIntegridad> Carpetas,
    IReadOnlyDictionary<Guid, IReadOnlyList<Bandeja>> BandejasVisibles,
    IReadOnlyDictionary<Bandeja, int> ConteoPorBandeja,
    IReadOnlyDictionary<Bandeja, int> ConteoPanel,
    int TotalVisible,
    IReadOnlyDictionary<Guid, DateTime> RevisionesDuplicado);

/// <summary>One broken rule. <see cref="CarpetaId"/> is null for aggregate rules; the detail never has personal data.</summary>
public sealed record ViolacionIntegridad(ReglaIntegridad Regla, Guid? CarpetaId, string Detalle);

/// <summary>Pure evaluation of the rules over a snapshot (no I/O: testable with synthetic data).</summary>
public static class ReglasIntegridad
{
    public static IReadOnlyList<ViolacionIntegridad> Evaluar(EntradaIntegridad entrada, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var v = new List<ViolacionIntegridad>();
        UnaSolaBandeja(entrada, v);
        SumaBandejas(entrada, v);
        DuplicadoMismaClase(entrada, v);
        AnuladaVisible(entrada, v);
        FechasImposibles(entrada, hoy, v);
        return v;
    }

    private static void UnaSolaBandeja(EntradaIntegridad e, List<ViolacionIntegridad> v)
    {
        foreach (var c in e.Carpetas.Where(c => !c.Anulada))
        {
            var visibles = e.BandejasVisibles.TryGetValue(c.Id, out var lista) ? lista : [];
            var esperada = Carpeta.ClasificarBandeja(c.Estado, c.Decision, c.CajaArchivo);
            if (visibles.Count != 1)
            {
                v.Add(new ViolacionIntegridad(ReglaIntegridad.UnaSolaBandeja, c.Id,
                    visibles.Count == 0 ? "No aparece en ninguna bandeja." : $"Aparece en {visibles.Count} bandejas a la vez."));
            }
            else if (visibles[0] != esperada)
            {
                v.Add(new ViolacionIntegridad(ReglaIntegridad.UnaSolaBandeja, c.Id,
                    "La bandeja mostrada no es la que corresponde a su estado."));
            }
        }
    }

    private static void SumaBandejas(EntradaIntegridad e, List<ViolacionIntegridad> v)
    {
        var activas = e.Carpetas.Count(c => !c.Anulada);
        var suma = e.ConteoPorBandeja.Values.Sum();
        if (suma != e.TotalVisible)
        {
            v.Add(new ViolacionIntegridad(ReglaIntegridad.SumaBandejas, null,
                $"Las bandejas suman {suma} y «Todas» muestra {e.TotalVisible}."));
        }
        if (e.TotalVisible != activas)
        {
            v.Add(new ViolacionIntegridad(ReglaIntegridad.SumaBandejas, null,
                $"«Todas» muestra {e.TotalVisible} y existen {activas} carpetas no anuladas."));
        }

        foreach (var bandeja in Enum.GetValues<Bandeja>())
        {
            var contador = e.ConteoPorBandeja.GetValueOrDefault(bandeja);
            var panel = e.ConteoPanel.GetValueOrDefault(bandeja);
            if (contador != panel)
            {
                v.Add(new ViolacionIntegridad(ReglaIntegridad.SumaBandejas, null,
                    $"Bandeja {bandeja}: el listado cuenta {contador} y el panel de inicio {panel}."));
            }
        }
    }

    private static void DuplicadoMismaClase(EntradaIntegridad e, List<ViolacionIntegridad> v)
    {
        var abiertas = e.Carpetas
            .Where(c => !c.Anulada && c.Decision == Decision.Pendiente && c.Clases != ClaseLicencia.Ninguna
                        && Carpeta.ClasificarBandeja(c.Estado, c.Decision, c.CajaArchivo) != Bandeja.Archivadas);
        foreach (var grupo in abiertas.GroupBy(c => c.CiudadanoId))
        {
            var lista = grupo.ToList();
            if (lista.Count < 2) continue;
            // A citizen marked "Mantener ambas" in Por clasificar stays accepted until a newer carpeta shows up.
            if (e.RevisionesDuplicado.TryGetValue(grupo.Key, out var revisadoEn) && lista.All(c => c.FechaRegistro <= revisadoEn))
            {
                continue;
            }

            foreach (var c in lista.Where(c => lista.Any(o => o.Id != c.Id && ClasesLicencia.Overlaps(o.Clases, c.Clases))))
            {
                v.Add(new ViolacionIntegridad(ReglaIntegridad.DuplicadoMismaClase, c.Id,
                    "Hay otra carpeta abierta del mismo RUT con una clase en común."));
            }
        }
    }

    private static void AnuladaVisible(EntradaIntegridad e, List<ViolacionIntegridad> v)
    {
        foreach (var c in e.Carpetas.Where(c => c.Anulada))
        {
            if (e.BandejasVisibles.TryGetValue(c.Id, out var lista) && lista.Count > 0)
            {
                v.Add(new ViolacionIntegridad(ReglaIntegridad.AnuladaVisible, c.Id,
                    "Está anulada pero aparece en una bandeja."));
            }
        }
    }

    private static void FechasImposibles(EntradaIntegridad e, DateOnly hoy, List<ViolacionIntegridad> v)
    {
        foreach (var c in e.Carpetas.Where(c => !c.Anulada))
        {
            if (c.FechaSubida is { } subida && subida < c.FechaCitacion)
            {
                v.Add(new ViolacionIntegridad(ReglaIntegridad.FechasImposibles, c.Id,
                    "La fecha de subida es anterior a la fecha de citación."));
            }
            if (c.FechaCierre is { } cierre && cierre > hoy)
            {
                v.Add(new ViolacionIntegridad(ReglaIntegridad.FechasImposibles, c.Id,
                    "La fecha de cierre está en el futuro."));
            }
        }
    }
}
