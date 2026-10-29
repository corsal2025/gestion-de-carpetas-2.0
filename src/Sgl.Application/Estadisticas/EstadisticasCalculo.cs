using System.Globalization;
using System.Text;
using Sgl.Domain;

namespace Sgl.Application.Estadisticas;

/// <summary>Audit entry of the "pendiente de búsqueda" flag (true = marked, false = cleared).</summary>
public sealed record MarcaBusqueda(Guid CarpetaId, DateTime Fecha, bool Activa);

/// <summary>Audit entry of a carpeta moving to <see cref="EstadoCarpeta.NoExisteCarpeta"/>.</summary>
public sealed record MarcaNoExiste(Guid CarpetaId, DateTime Fecha);

/// <summary>Audit entry of a caja assignment change.</summary>
public sealed record MovimientoCaja(Guid CarpetaId, DateTime Fecha, string? Anterior, string? Nuevo);

/// <summary>A closed carpeta reduced to what the delivery-time metric needs.</summary>
public sealed record CarpetaCerrada(Bandeja Modulo, DateOnly FechaCitacion, DateOnly FechaCierre);

/// <summary>Pure aggregation rules used by the statistics query; kept here so they are unit-testable.</summary>
public static class EstadisticasCalculo
{
    public const string ViaConaset = "Conaset";
    public const string ViaF8 = "F8";
    public const string ViaOficio = "Oficio";
    public const string ViaCorreo = "Correo";
    public const string ViaOtra = "Otra";

    private const int LargoMinimoTermino = 3;

    /// <summary>Terms seen in fewer carpetas are dropped so one-off words (e.g. proper names) never surface.</summary>
    public const int FrecuenciaMinimaTermino = 2;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "DE", "LA", "EL", "EN", "Y", "A", "LOS", "LAS", "DEL", "AL", "CON", "POR", "PARA", "SE", "SU", "SUS",
        "UN", "UNA", "UNOS", "UNAS", "QUE", "NO", "SI", "ES", "LO", "LE", "O", "U", "E", "YA", "MAS", "PERO",
        "SIN", "SOBRE", "ESTA", "ESTE", "ESTO", "SON", "FUE", "HAY", "TIENE", "COMO", "SR", "SRA", "CARPETA",
    };

    public static TiemposDto Tiempos(IReadOnlyCollection<double> valores)
    {
        if (valores.Count == 0) return new TiemposDto(0, null, null, null);
        var orden = valores.Order().ToArray();
        var mitad = orden.Length / 2;
        var mediana = orden.Length % 2 == 1 ? orden[mitad] : (orden[mitad - 1] + orden[mitad]) / 2;
        return new TiemposDto(orden.Length, Math.Round(orden.Average(), 1), mediana, orden[^1]);
    }

    /// <summary>
    /// Delivery time = closing date minus citación, in days, over closed carpetas; overall and per module (only
    /// modules with samples, in tray order). Rows whose closing date precedes the citación are inconsistent and skipped.
    /// </summary>
    public static TiempoEntregaDto TiempoEntrega(IEnumerable<CarpetaCerrada> cerradas)
    {
        ArgumentNullException.ThrowIfNull(cerradas);
        var validas = cerradas
            .Where(c => c.FechaCierre >= c.FechaCitacion)
            .Select(c => (c.Modulo, Dias: (double)(c.FechaCierre.DayNumber - c.FechaCitacion.DayNumber)))
            .ToList();

        return new TiempoEntregaDto(
            Tiempos(validas.Select(v => v.Dias).ToList()),
            validas
                .GroupBy(v => v.Modulo)
                .OrderBy(g => g.Key)
                .Select(g => new TiempoEntregaModuloDto(g.Key, Tiempos(g.Select(v => v.Dias).ToList())))
                .ToList());
    }

    public static string ViaDeSubida(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.SubidaConaset or EstadoCarpeta.CambioDomSubidoConaset => ViaConaset,
        EstadoCarpeta.SubidaConF8 => ViaF8,
        EstadoCarpeta.SubidaConOficio => ViaOficio,
        EstadoCarpeta.CambioDomSubidoCorreo => ViaCorreo,
        _ => ViaOtra,
    };

    /// <summary>
    /// Pairs each "marked" entry with the next "cleared" entry of the same carpeta. A cleared search is
    /// "no encontrada" when the carpeta moved to NoExisteCarpeta between both entries (inclusive), otherwise
    /// "encontrada". Unpaired marks are still open.
    /// </summary>
    public static BusquedasDto AnalizarBusquedas(
        IEnumerable<MarcaBusqueda> marcas,
        IEnumerable<MarcaNoExiste> noExiste,
        IReadOnlyDictionary<Guid, string> sectorPorCarpeta,
        DateTime ahora,
        int diasAbierta)
    {
        var noExistePorCarpeta = noExiste.ToLookup(n => n.CarpetaId, n => n.Fecha);
        int iniciadas = 0, noEncontradas = 0, abiertas = 0, vencidas = 0, archivo = 0, of43 = 0, desconocido = 0;
        var horas = new List<double>();

        foreach (var grupo in marcas.GroupBy(m => m.CarpetaId))
        {
            DateTime? inicio = null;
            foreach (var m in grupo.OrderBy(m => m.Fecha))
            {
                if (m.Activa)
                {
                    if (inicio is null) iniciadas++;
                    inicio ??= m.Fecha;
                    continue;
                }

                if (inicio is not { } desde) continue;
                var hasta = m.Fecha;
                if (noExistePorCarpeta[grupo.Key].Any(f => f >= desde && f <= hasta))
                {
                    noEncontradas++;
                }
                else
                {
                    horas.Add(Math.Round((hasta - desde).TotalHours, 1));
                    switch (sectorPorCarpeta.GetValueOrDefault(grupo.Key))
                    {
                        case SectorFisico.Archivo: archivo++; break;
                        case SectorFisico.Oficina43: of43++; break;
                        default: desconocido++; break;
                    }
                }

                inicio = null;
            }

            if (inicio is { } abierta)
            {
                abiertas++;
                if ((ahora - abierta).TotalDays > diasAbierta) vencidas++;
            }
        }

        return new BusquedasDto(
            HayDatos: iniciadas > 0,
            Iniciadas: iniciadas,
            Encontradas: horas.Count,
            NoEncontradas: noEncontradas,
            Abiertas: abiertas,
            AbiertasVencidas: vencidas,
            HorasParaEncontrar: Tiempos(horas),
            EncontradasArchivo: archivo,
            EncontradasOficina43: of43,
            EncontradasSectorDesconocido: desconocido);
    }

    /// <summary>Most frequent words in free-text observations: uppercase, no accents, no stopwords or digits.</summary>
    public static IReadOnlyList<ConteoDto> TerminosFrecuentes(IEnumerable<string?> textos, int top)
    {
        var conteo = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var texto in textos)
        {
            if (string.IsNullOrWhiteSpace(texto)) continue;
            foreach (var termino in Tokenizar(texto).Where(EsTerminoValido).Distinct())
            {
                conteo[termino] = conteo.GetValueOrDefault(termino) + 1;
            }
        }

        return conteo
            .Where(kv => kv.Value >= FrecuenciaMinimaTermino)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(top)
            .Select(kv => new ConteoDto(kv.Key, kv.Value))
            .ToList();
    }

    private static IEnumerable<string> Tokenizar(string texto)
    {
        var sb = new StringBuilder();
        foreach (var ch in SinAcentos(texto).ToUpperInvariant())
        {
            if (char.IsLetter(ch))
            {
                sb.Append(ch);
                continue;
            }

            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0) yield return sb.ToString();
    }

    public static bool EsTerminoValido(string t) => t.Length >= LargoMinimoTermino && !Stopwords.Contains(t);

    private static string SinAcentos(string s)
    {
        var normalizado = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);
        foreach (var ch in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Carpetas archived and distinct cajas closed per month (a real caja assignment, never the queue);
    /// plus how many distinct cajas were reopened (carpetas sent back from a real caja to the queue).
    /// </summary>
    public static (IReadOnlyList<ArchivoMesDto> PorMes, int CajasReabiertas) ArchivoPorMes(IEnumerable<MovimientoCaja> movimientos)
    {
        var lista = movimientos.ToList();
        var porMes = lista
            .Where(m => EsCajaReal(m.Nuevo))
            .GroupBy(m => (HoraChile.ALocal(m.Fecha).Year, HoraChile.ALocal(m.Fecha).Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new ArchivoMesDto(
                g.Key.Year,
                g.Key.Month,
                g.Select(m => m.CarpetaId).Distinct().Count(),
                g.Select(m => m.Nuevo!).Distinct(StringComparer.OrdinalIgnoreCase).Count()))
            .ToList();

        var reabiertas = lista
            .Where(m => m.Nuevo == Carpeta.ColaDeCaja && EsCajaReal(m.Anterior))
            .Select(m => m.Anterior!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return (porMes, reabiertas);
    }

    /// <summary>Free-text keys (comunas) that contain digits may hold identifiers such as RUTs; never show them.</summary>
    public static bool EsEtiquetaSegura(string? clave) => !string.IsNullOrWhiteSpace(clave) && !clave.Any(char.IsDigit);

    public static bool EsCajaReal(string? caja) => !string.IsNullOrWhiteSpace(caja) && caja != Carpeta.ColaDeCaja;
}
