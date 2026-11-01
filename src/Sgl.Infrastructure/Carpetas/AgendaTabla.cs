using System.Text;
using ClosedXML.Excel;

namespace Sgl.Infrastructure.Carpetas;

/// <summary>
/// One cell of an agenda file. <see cref="Texto"/> is what a person sees; the typed values are only set for xlsx
/// cells Excel stored as date/time or number (a CSV carries text only), so dates and hours keep reading exactly as before.
/// </summary>
public readonly record struct CeldaAgenda(string Texto, DateTime? FechaHora = null, TimeSpan? Hora = null, double? Numero = null)
{
    public static CeldaAgenda Vacia { get; } = new(string.Empty);

    public bool EstaVacia => Texto.Length == 0 && FechaHora is null && Hora is null && Numero is null;
}

/// <summary>
/// Common reader for the daily agenda: an xlsx workbook (first sheet) or a CSV (RFC 4180, cp1252 or UTF-8 with BOM,
/// optional first line <c>sep=,</c> which is dropped). The format is recognised by content (xlsx = ZIP signature
/// <c>PK\x03\x04</c>), never by file name. Row numbers are 1-based and, for a CSV, count after the <c>sep=</c> line,
/// the same way Excel shows them.
/// </summary>
public sealed class AgendaTabla
{
    private static readonly byte[] FirmaZip = [0x50, 0x4B, 0x03, 0x04];

    private AgendaTabla(List<CeldaAgenda[]> filas) => Filas = filas;

    /// <summary>Every row of the file (header included); the row number is the index plus one.</summary>
    public IReadOnlyList<CeldaAgenda[]> Filas { get; }

    /// <summary>True when the stream starts with the ZIP signature. Leaves the stream at position 0.</summary>
    public static bool EsXlsx(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Position = 0;
        Span<byte> firma = stackalloc byte[4];
        var leidos = stream.Read(firma);
        stream.Position = 0;
        return leidos == 4 && firma.SequenceEqual(FirmaZip);
    }

    /// <summary>Reads the whole file (xlsx or CSV). Leaves the stream at position 0.</summary>
    public static AgendaTabla Leer(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            if (EsXlsx(stream))
            {
                PeticionCambioDomicilio.Excel.WorkbookSanitizer.ValidarStream(stream);
                using var wb = new XLWorkbook(stream);
                return wb.Worksheets.Count == 0 ? new AgendaTabla([]) : new AgendaTabla(LeerHoja(wb.Worksheet(1)));
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new AgendaTabla(LeerCsv(Decodificar(ms.ToArray())));
        }
        finally
        {
            stream.Position = 0;
        }
    }

    private static List<CeldaAgenda[]> LeerHoja(IXLWorksheet ws)
    {
        var filas = new List<CeldaAgenda[]>();
        var ultimaFila = ws.LastRowUsed()?.RowNumber() ?? 0;
        var ultimaCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var r = 1; r <= ultimaFila; r++)
        {
            var fila = new CeldaAgenda[ultimaCol];
            for (var c = 1; c <= ultimaCol; c++)
            {
                fila[c - 1] = Celda(ws.Cell(r, c));
            }
            filas.Add(fila);
        }
        return filas;
    }

    private static CeldaAgenda Celda(IXLCell cell)
    {
        if (cell.IsEmpty()) return CeldaAgenda.Vacia;
        var texto = cell.GetFormattedString().Trim();
        return cell.DataType switch
        {
            XLDataType.DateTime => new CeldaAgenda(texto, FechaHora: cell.GetDateTime()),
            XLDataType.TimeSpan => new CeldaAgenda(texto, Hora: cell.GetTimeSpan()),
            XLDataType.Number => new CeldaAgenda(texto, Numero: cell.GetDouble()),
            _ => new CeldaAgenda(cell.GetString().Trim()),
        };
    }

    /// <summary>UTF-8 when the file has a BOM or is valid UTF-8; otherwise Windows-1252 (what the booking portal exports).</summary>
    private static string Decodificar(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes);
        }
    }

    private static List<CeldaAgenda[]> LeerCsv(string texto)
    {
        var filas = new List<CeldaAgenda[]>();
        if (texto.Length == 0) return filas;

        var separador = ',';
        var inicio = 0;
        var finPrimera = texto.IndexOfAny(['\r', '\n']);
        var primera = finPrimera < 0 ? texto : texto[..finPrimera];
        if (primera.Trim().StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && primera.Trim().Length == 5)
        {
            separador = primera.Trim()[4];
            inicio = finPrimera < 0 ? texto.Length : finPrimera;
            while (inicio < texto.Length && texto[inicio] is '\r' or '\n') inicio++;
        }
        else
        {
            separador = new[] { ',', ';', '\t' }.MaxBy(s => primera.Count(ch => ch == s));
            if (primera.Count(ch => ch == separador) == 0) separador = ',';
        }

        var campos = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;
        var hayDatos = false;
        void CerrarCampo()
        {
            campos.Add(actual.ToString());
            actual.Clear();
        }
        void CerrarFila()
        {
            CerrarCampo();
            if (hayDatos || campos.Count > 1) filas.Add(campos.Select(c => new CeldaAgenda(c.Trim())).ToArray());
            else filas.Add([]);
            campos = [];
            hayDatos = false;
        }

        for (var i = inicio; i < texto.Length; i++)
        {
            var ch = texto[i];
            if (entreComillas)
            {
                if (ch == '"' && i + 1 < texto.Length && texto[i + 1] == '"')
                {
                    actual.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    entreComillas = false;
                }
                else
                {
                    actual.Append(ch);
                }
            }
            else if (ch == '"')
            {
                entreComillas = true;
                hayDatos = true;
            }
            else if (ch == separador)
            {
                CerrarCampo();
                hayDatos = true;
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;
                CerrarFila();
            }
            else
            {
                actual.Append(ch);
                hayDatos = true;
            }
        }

        if (actual.Length > 0 || campos.Count > 0 || hayDatos) CerrarFila();
        return filas;
    }
}
