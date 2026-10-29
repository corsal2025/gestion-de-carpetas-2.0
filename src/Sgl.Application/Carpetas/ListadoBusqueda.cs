using System.Globalization;
using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>One line of the printable search listing.</summary>
public sealed record ListadoBusquedaItem(
    int Numero,
    Guid Id,
    string Rut,
    string NombreCompleto,
    Sede Sede,
    string? Comuna,
    string FechaUltimaCarpeta,
    DateTime? FechaImpresion);

/// <summary>
/// Printable "listado para búsqueda" of a physical-search tray: what the official takes to the shelves, with room
/// to tick "encontrada / no existe". Contains personal data by design (it is a printed work document).
/// </summary>
public sealed record ListadoBusquedaDto(
    string Folio,
    DateTime EmitidoUtc,
    Bandeja Bandeja,
    string Sector,
    string Titulo,
    Sede? Sede,
    int Total,
    IReadOnlyList<ListadoBusquedaItem> Items)
{
    /// <summary>Chile local emission time, as printed.</summary>
    public DateTime EmitidoChile => HoraChile.ALocal(EmitidoUtc);

    public string EmitidoTexto =>
        $"{FechaImpresa.De(DateOnly.FromDateTime(EmitidoChile))} {EmitidoChile.ToString("HH:mm", CultureInfo.InvariantCulture)}";

    public IReadOnlyList<Guid> Ids => Items.Select(i => i.Id).ToList();
}

/// <summary>Printed dates as number/letters/number ("02/FEB/2019"): day and month can never be read swapped.</summary>
public static class FechaImpresa
{
    private static readonly string[] Meses = ["ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC"];

    public static string De(DateOnly fecha) =>
        $"{fecha.Day:00}/{Meses[fecha.Month - 1]}/{fecha.Year.ToString(CultureInfo.InvariantCulture)}";
}

public static class ListadoBusquedaBuilder
{
    /// <summary>
    /// Orders by last-folder date (oldest first, the natural shelf walk); carpetas without a readable date go last,
    /// then by name, so the printed order is deterministic.
    /// </summary>
    public static ListadoBusquedaDto Construir(Bandeja bandeja, Sede? sede, IEnumerable<CarpetaResumenDto> carpetas, DateTime ahoraUtc)
    {
        ArgumentNullException.ThrowIfNull(carpetas);
        var esArchivo = bandeja == Bandeja.SeEncuentraEnArchivos;
        var sector = esArchivo ? "Archivo" : "Oficina 43";

        var ordenadas = carpetas
            .Select(c => (Carpeta: c, Fecha: SectorFisico.TryParseFecha(c.FechaUltimaCarpeta, out var d) ? (DateOnly?)d : null))
            .OrderBy(x => x.Fecha is null)
            .ThenBy(x => x.Fecha)
            .ThenBy(x => x.Carpeta.NombreCompleto, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Carpeta.Rut, StringComparer.Ordinal)
            .Select((x, i) => new ListadoBusquedaItem(
                i + 1,
                x.Carpeta.Id,
                x.Carpeta.Rut,
                x.Carpeta.NombreCompleto,
                x.Carpeta.Sede,
                x.Carpeta.Comuna,
                x.Fecha is { } f ? FechaImpresa.De(f) : "—",
                x.Carpeta.FechaImpresion))
            .ToList();

        var folio = $"{(esArchivo ? "Archivo" : "Oficina43")}{(sede is { } s ? $"-{s}" : string.Empty)}-{HoraChile.ALocal(ahoraUtc):yyyyMMdd-HHmmss}";
        return new ListadoBusquedaDto(
            folio,
            ahoraUtc,
            bandeja,
            sector,
            $"Listado para búsqueda — Se encuentra en {(esArchivo ? "Archivos" : "Of. 43")}",
            sede,
            ordenadas.Count,
            ordenadas);
    }
}
