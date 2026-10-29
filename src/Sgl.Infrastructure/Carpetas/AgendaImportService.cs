using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sgl.Application.Integridad;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Infrastructure.Carpetas;

/// <summary>Outcome of a daily agenda import. Only counts and row numbers, never personal data.</summary>
public sealed record AgendaImportResult(
    int FilasLeidas,
    int Creadas,
    int YaExistian,
    int CanceladasOmitidas,
    int CiudadanosCreados,
    IReadOnlyList<int> FilasRutInvalido,
    IReadOnlyList<string> Errores,
    int ConAlertaDuplicado = 0,
    int YaExistianAnuladas = 0);

/// <summary>
/// Imports the daily appointment agenda (one sheet, "Fecha Cita | Hora Inicio | RUT | Nombre | ...") as
/// "Por clasificar" carpetas. Email and phone are intentionally ignored (data minimization).
/// Idempotent by citizen + sede + appointment date (cancelled rows included, so a resolved duplicate is never
/// re-created); all-or-nothing transaction. Rows whose RUT already has an open carpeta still enter "Por clasificar"
/// and are counted as duplicate alerts (the alert itself is computed live when listing).
/// </summary>
public sealed class AgendaImportService(
    SglDbContext db, TimeProvider clock, ILogger<AgendaImportService> logger, IntegridadSolicitudes? integridad = null)
{
    private const int MaxFilasEncabezado = 5;

    private sealed record Columnas(int Fecha, int Hora, int Rut, int Nombre, int Tramite, int Ubicacion, int? Estado, int? Motivo);

    /// <summary>True when the first sheet carries the agenda headers. Leaves the stream at position 0.</summary>
    public static bool EsFormatoAgenda(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            PeticionCambioDomicilio.Excel.WorkbookSanitizer.ValidarStream(stream);
            using var wb = new XLWorkbook(stream);
            return wb.Worksheets.Count > 0 && DetectarColumnas(wb.Worksheet(1), out _, out _);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            stream.Position = 0;
        }
    }

    public async Task<AgendaImportResult> ImportarAsync(Stream stream, string autor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var sanitized = await ExcelImportService.QuitarValidacionesDeDatosAsync(stream, ct);
            using var wb = new XLWorkbook(sanitized);
            var ws = wb.Worksheet(1);
            if (!DetectarColumnas(ws, out var cols, out var filaEncabezado))
            {
                throw new InvalidOperationException("El archivo no tiene el formato de agenda esperado.");
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await ImportarFilasAsync(ws, cols, filaEncabezado, autor, ct);
            db.ImportacionLogs.Add(new ImportacionLog(
                clock.GetUtcNow().UtcDateTime, autor, result.FilasLeidas, result.Creadas, 0,
                result.FilasRutInvalido.Count + result.Errores.Count));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            integridad?.Solicitar(OrigenIntegridad.Importacion);
            return result;
        }
        catch (Exception ex)
        {
            // Nothing of a failed import may stay tracked in the circuit's context for a later save.
            db.ChangeTracker.Clear();
            if (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Agenda import failed for author {Autor}", autor);
            }
            throw;
        }
    }

    private async Task<AgendaImportResult> ImportarFilasAsync(
        IXLWorksheet ws, Columnas cols, int filaEncabezado, string autor, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var usuario = string.IsNullOrWhiteSpace(autor) ? "IMPORTACION_AGENDA" : autor.Trim();
        var ciudadanos = await db.Ciudadanos.ToDictionaryAsync(c => c.Rut.Value, StringComparer.OrdinalIgnoreCase, ct);
        var claves = new HashSet<(Guid, Sede, DateOnly)>();

        int leidas = 0, creadas = 0, yaExistian = 0, canceladas = 0, ciudadanosCreados = 0, conAlerta = 0, yaExistianAnuladas = 0;
        var conCarpetaEnEstaCarga = new HashSet<Guid>();
        var rutInvalido = new List<int>();
        var errores = new List<string>();
        var ultima = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (var n = filaEncabezado + 1; n <= ultima; n++)
        {
            ct.ThrowIfCancellationRequested();
            var row = ws.Row(n);
            if (row.IsEmpty()) continue;
            leidas++;

            var estado = Normalizar(Texto(row, cols.Estado));
            if (estado.Contains("CANCEL") || estado.Contains("ANULAD") || !string.IsNullOrWhiteSpace(Texto(row, cols.Motivo)))
            {
                canceladas++;
                continue;
            }

            if (!Rut.TryParse(Texto(row, cols.Rut), out var rut))
            {
                rutInvalido.Add(n);
                continue;
            }

            var fecha = LeerFecha(row.Cell(cols.Fecha));
            if (fecha is null)
            {
                errores.Add($"Fila {n}: fecha de cita vacía o inválida.");
                continue;
            }

            var sede = MapearSede(Texto(row, cols.Ubicacion));
            if (sede is null)
            {
                errores.Add($"Fila {n}: ubicación (sede) no reconocida.");
                continue;
            }

            if (!ciudadanos.TryGetValue(rut!.Value, out var ciudadano))
            {
                var (nombre, apellido) = SepararNombre(Texto(row, cols.Nombre));
                ciudadano = new Ciudadano(rut, nombre, apellido);
                db.Ciudadanos.Add(ciudadano);
                ciudadanos[rut.Value] = ciudadano;
                ciudadanosCreados++;
            }

            var ciudadanoId = ciudadano.Id;
            var sedeValor = sede.Value;
            var fechaValor = fecha.Value;
            bool? anuladaPrevia = !claves.Add((ciudadanoId, sedeValor, fechaValor))
                ? false
                : await db.Carpetas.IgnoreQueryFilters()
                    .Where(c => c.CiudadanoId == ciudadanoId && c.Sede == sedeValor && c.FechaCitacion == fechaValor)
                    .Select(c => (bool?)c.Anulada)
                    .OrderBy(a => a)
                    .FirstOrDefaultAsync(ct);
            if (anuladaPrevia is { } esAnulada)
            {
                yaExistian++;
                if (esAnulada) yaExistianAnuladas++;
                continue;
            }

            db.Carpetas.Add(Carpeta.CrearDesdeAgenda(
                ciudadano, sedeValor, fechaValor, LeerHora(row.Cell(cols.Hora)),
                MapearTramite(Texto(row, cols.Tramite)), usuario, now));
            creadas++;
            if (!conCarpetaEnEstaCarga.Add(ciudadanoId)
                || await db.Carpetas.Where(c => c.CiudadanoId == ciudadanoId).AnyAsync(Carpeta.AbiertaSinArchivarExpresion, ct))
            {
                conAlerta++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new AgendaImportResult(leidas, creadas, yaExistian, canceladas, ciudadanosCreados, rutInvalido, errores, conAlerta, yaExistianAnuladas);
    }

    private static bool DetectarColumnas(IXLWorksheet ws, out Columnas cols, out int filaEncabezado)
    {
        cols = null!;
        filaEncabezado = 0;
        var ultima = Math.Min(MaxFilasEncabezado, ws.LastRowUsed()?.RowNumber() ?? 0);
        for (var r = 1; r <= ultima; r++)
        {
            var mapa = new Dictionary<string, int>();
            var row = ws.Row(r);
            var lastCol = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
            for (var c = 1; c <= lastCol; c++)
            {
                var h = Normalizar(row.Cell(c).GetString());
                if (h.Length > 0) mapa.TryAdd(h, c);
            }

            int? Col(params string[] nombres) =>
                nombres.Select(x => mapa.TryGetValue(x, out var i) ? i : (int?)null).FirstOrDefault(i => i is not null);

            var fecha = Col("FECHA CITA");
            var hora = Col("HORA INICIO", "HORA");
            var rut = Col("RUT");
            var nombre = Col("NOMBRE");
            var tramite = Col("TRAMITE");
            var ubic = Col("UBICACION", "SEDE");
            if (fecha is null || hora is null || rut is null || nombre is null || tramite is null || ubic is null) continue;

            cols = new Columnas(fecha.Value, hora.Value, rut.Value, nombre.Value, tramite.Value, ubic.Value,
                Col("ESTADO"), Col("MOTIVO CANCELACION"));
            filaEncabezado = r;
            return true;
        }
        return false;
    }

    internal static Sede? MapearSede(string? raw)
    {
        var s = Normalizar(raw);
        if (s.Contains("PLACILLA")) return Sede.Placilla;
        if (s.Contains("PUERTO") || s.StartsWith("MERC", StringComparison.Ordinal)) return Sede.MercadoPuerto;
        if (s.Contains("ARGENTINA")) return Sede.AvArgentina;
        return null;
    }

    internal static TramiteAgenda? MapearTramite(string? raw)
    {
        var s = Normalizar(raw);
        if (s.Contains("RENOV")) return TramiteAgenda.Renovacion;
        if (s.Contains("PRIMERA") || s.Contains("EXTENS")) return TramiteAgenda.PrimeraVezExtension;
        return null;
    }

    /// <summary>Best effort: last two words are surnames. The full name stays searchable via NombreBusqueda.</summary>
    internal static (string Nombre, string Apellido) SepararNombre(string? completo)
    {
        var (nombre, apellido) = Ciudadano.Separar(completo);
        return (Recortar(nombre), Recortar(apellido));
    }

    private static string Recortar(string s) => s.Length > Ciudadano.MaxNameLength ? s[..Ciudadano.MaxNameLength] : s;

    private static DateOnly? LeerFecha(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());
        if (cell.DataType == XLDataType.Number) return DateOnly.FromDateTime(DateTime.FromOADate(cell.GetDouble()));
        string[] formatos = ["dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d-M-yyyy", "d/M/yyyy", "yyyy/MM/dd"];
        var soloFecha = cell.GetString().Trim().Split(' ')[0];
        return DateOnly.TryParseExact(soloFecha, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static TimeOnly? LeerHora(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        switch (cell.DataType)
        {
            case XLDataType.DateTime:
                return TimeOnly.FromDateTime(cell.GetDateTime());
            case XLDataType.TimeSpan:
                return TimeOnly.FromTimeSpan(cell.GetTimeSpan());
            case XLDataType.Number:
                var frac = cell.GetDouble() % 1;
                return frac < 0 ? null : TimeOnly.FromTimeSpan(TimeSpan.FromDays(frac));
        }
        string[] formatos = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"];
        return TimeOnly.TryParseExact(cell.GetString().Trim(), formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
    }

    private static string? Texto(IXLRow row, int? col) =>
        col is { } c && !row.Cell(c).IsEmpty() ? row.Cell(c).GetFormattedString().Trim() : null;

    private static string Normalizar(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var decomposed = text.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
