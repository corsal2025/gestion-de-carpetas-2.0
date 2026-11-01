using System.Globalization;
using System.Text;
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
    int YaExistianAnuladas = 0,
    int CanceladasGuardadas = 0,
    int CanceladasYaRegistradas = 0,
    int CanceladasDeCarpetaExistente = 0);

/// <summary>
/// Imports the daily appointment agenda (xlsx or CSV, recognised by content) as "Por clasificar" carpetas.
/// Idempotent by citizen + "Fecha Toma Cita" and, for carpetas without that date, by citizen + sede + appointment date
/// (anulled rows included, so a resolved duplicate is never re-created); all-or-nothing transaction. Contact e-mail and
/// phone are kept on the carpeta. Cancelled rows never create a carpeta: they are stored apart as <see cref="CitaCancelada"/>
/// (no RUT, no name) for the monthly statistics. Rows whose RUT already has an open carpeta still enter "Por clasificar"
/// and are counted as duplicate alerts (the alert itself is computed live when listing).
/// </summary>
public sealed class AgendaImportService(
    SglDbContext db, TimeProvider clock, ILogger<AgendaImportService> logger, IntegridadSolicitudes? integridad = null)
{
    private const int MaxFilasEncabezado = 5;

    private sealed record Columnas(
        int Fecha, int Hora, int Rut, int Nombre, int Tramite, int Ubicacion, int? Estado, int? Motivo,
        int? Toma, int? Email, int? Telefono);

    /// <summary>True when the file (xlsx or CSV) carries the agenda headers. Leaves the stream at position 0.</summary>
    public static bool EsFormatoAgenda(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return DetectarColumnas(AgendaTabla.Leer(stream), out _, out _);
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
            var tabla = await LeerTablaAsync(stream, ct);
            if (!DetectarColumnas(tabla, out var cols, out var filaEncabezado))
            {
                throw new InvalidOperationException("El archivo no tiene el formato de agenda esperado.");
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await ImportarFilasAsync(tabla, cols, filaEncabezado, autor, ct);
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

    private static async Task<AgendaTabla> LeerTablaAsync(Stream stream, CancellationToken ct)
    {
        var copia = new MemoryStream();
        await stream.CopyToAsync(copia, ct);
        if (!AgendaTabla.EsXlsx(copia)) return AgendaTabla.Leer(copia);

        // The real workbook carries long data-validation formulas ClosedXML refuses to open: strip them first.
        using var sanitized = await ExcelImportService.QuitarValidacionesDeDatosAsync(copia, ct);
        return AgendaTabla.Leer(sanitized);
    }

    private async Task<AgendaImportResult> ImportarFilasAsync(
        AgendaTabla tabla, Columnas cols, int filaEncabezado, string autor, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var usuario = string.IsNullOrWhiteSpace(autor) ? "IMPORTACION_AGENDA" : autor.Trim();
        var ciudadanos = await db.Ciudadanos.ToDictionaryAsync(c => c.Rut.Value, StringComparer.OrdinalIgnoreCase, ct);
        var clavesCita = new HashSet<(Guid, Sede, DateOnly)>();
        var clavesToma = new HashSet<(Guid, DateTime)>();
        var canceladasConocidas = (await db.CitasCanceladas
                .Select(x => new { x.FechaTomaCita, x.FechaCita, x.HoraCita, x.Sede, x.Tramite })
                .ToListAsync(ct))
            .Select(x => (x.FechaTomaCita, x.FechaCita, x.HoraCita, x.Sede, x.Tramite))
            .ToHashSet();

        int leidas = 0, creadas = 0, yaExistian = 0, canceladas = 0, ciudadanosCreados = 0, conAlerta = 0, yaExistianAnuladas = 0;
        int canceladasGuardadas = 0, canceladasYaRegistradas = 0, canceladasDeCarpeta = 0;
        var conCarpetaEnEstaCarga = new HashSet<Guid>();
        var rutInvalido = new List<int>();
        var errores = new List<string>();

        for (var n = filaEncabezado + 1; n <= tabla.Filas.Count; n++)
        {
            ct.ThrowIfCancellationRequested();
            var row = tabla.Filas[n - 1];
            if (row.All(c => c.EstaVacia)) continue;
            leidas++;

            var estado = Normalizar(Texto(row, cols.Estado));
            var fecha = LeerFecha(Celda(row, cols.Fecha));
            var sede = MapearSede(Texto(row, cols.Ubicacion));
            var toma = LeerToma(Celda(row, cols.Toma));
            var rutValido = Rut.TryParse(Texto(row, cols.Rut), out var rut);

            if (estado.Contains("CANCEL") || estado.Contains("ANULAD") || !string.IsNullOrWhiteSpace(Texto(row, cols.Motivo)))
            {
                canceladas++;
                if (fecha is null || sede is null)
                {
                    errores.Add($"Fila {n}: cancelada sin fecha de cita o sede válida; no se contó en las estadísticas.");
                    continue;
                }

                var cita = new CitaCancelada(
                    fecha.Value, toma, LeerHora(Celda(row, cols.Hora)), sede.Value, Texto(row, cols.Tramite), Texto(row, cols.Motivo), now);
                if (!canceladasConocidas.Add((cita.FechaTomaCita, cita.FechaCita, cita.HoraCita, cita.Sede, cita.Tramite)))
                {
                    canceladasYaRegistradas++;
                    continue;
                }

                db.CitasCanceladas.Add(cita);
                canceladasGuardadas++;
                if (rutValido && ciudadanos.TryGetValue(rut!.Value, out var previo)
                    && await AvisarCarpetaCanceladaAsync(previo.Id, cita, usuario, now, ct))
                {
                    canceladasDeCarpeta++;
                }
                continue;
            }

            if (!rutValido)
            {
                rutInvalido.Add(n);
                continue;
            }

            if (fecha is null)
            {
                errores.Add($"Fila {n}: fecha de cita vacía o inválida.");
                continue;
            }

            if (sede is null)
            {
                errores.Add($"Fila {n}: ubicación (sede) no reconocida.");
                continue;
            }

            // The RUT may have been corrected after the first import (it changes for all the citizen's carpetas): the same listing
            // arriving again with the original RUT is the same appointment, not a new person.
            var rutOriginal = rut!.Value;
            var fechaCorrecta = fecha.Value;
            var sedeCorrecta = sede.Value;
            var anuladaPorRutOriginal = await db.Carpetas.IgnoreQueryFilters()
                .Where(c => c.RutImportacion == rutOriginal)
                .Where(MismaCita(toma, sedeCorrecta, fechaCorrecta))
                .Select(c => (bool?)c.Anulada)
                .OrderBy(a => a)
                .FirstOrDefaultAsync(ct);
            if (anuladaPorRutOriginal is { } anuladaOriginal)
            {
                yaExistian++;
                if (anuladaOriginal) yaExistianAnuladas++;
                continue;
            }

            if (!ciudadanos.TryGetValue(rut.Value, out var ciudadano))
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
            var repetidaEnArchivo = !clavesCita.Add((ciudadanoId, sedeValor, fechaValor));
            if (toma is { } t && !clavesToma.Add((ciudadanoId, t))) repetidaEnArchivo = true;
            bool? anuladaPrevia = repetidaEnArchivo
                ? false
                : await db.Carpetas.IgnoreQueryFilters()
                    .Where(c => c.CiudadanoId == ciudadanoId)
                    .Where(MismaCita(toma, sedeValor, fechaValor))
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
                ciudadano, sedeValor, fechaValor, LeerHora(Celda(row, cols.Hora)),
                MapearTramite(Texto(row, cols.Tramite)), usuario, now, toma, Texto(row, cols.Email), Texto(row, cols.Telefono)));
            creadas++;
            if (!conCarpetaEnEstaCarga.Add(ciudadanoId)
                || await db.Carpetas.Where(c => c.CiudadanoId == ciudadanoId).AnyAsync(Carpeta.AbiertaSinArchivarExpresion, ct))
            {
                conAlerta++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new AgendaImportResult(
            leidas, creadas, yaExistian, canceladas, ciudadanosCreados, rutInvalido, errores, conAlerta, yaExistianAnuladas,
            canceladasGuardadas, canceladasYaRegistradas, canceladasDeCarpeta);
    }

    /// <summary>
    /// Whether a carpeta is the same appointment as an agenda row: same taking date, or the old key (sede + appointment date)
    /// when either side has no taking date (legacy carpetas imported before that column existed). Two carpetas that both
    /// have a taking date are told apart by it alone. Used by both the "already exists" lookup and the cancellation notice.
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<Carpeta, bool>> MismaCita(DateTime? toma, Sede sede, DateOnly fechaCita) =>
        c => (toma != null && c.FechaTomaCita == toma)
            || ((toma == null || c.FechaTomaCita == null) && c.Sede == sede && c.FechaCitacion == fechaCita);

    /// <summary>A row that was already imported as a carpeta now arrives cancelled: the carpeta stays, its history notes it.</summary>
    private async Task<bool> AvisarCarpetaCanceladaAsync(Guid ciudadanoId, CitaCancelada cita, string usuario, DateTime now, CancellationToken ct)
    {
        var toma = cita.FechaTomaCita;
        var sede = cita.Sede;
        var fechaCita = cita.FechaCita;
        var carpeta = await db.Carpetas
            .Where(c => c.CiudadanoId == ciudadanoId)
            .Where(MismaCita(toma, sede, fechaCita))
            .FirstOrDefaultAsync(ct);
        if (carpeta is null) return false;
        carpeta.RegistrarEvento("CitaCancelada", null, "Cancelada en la agenda", usuario, now);
        return true;
    }

    private static bool DetectarColumnas(AgendaTabla tabla, out Columnas cols, out int filaEncabezado)
    {
        cols = null!;
        filaEncabezado = 0;
        var ultima = Math.Min(MaxFilasEncabezado, tabla.Filas.Count);
        for (var r = 1; r <= ultima; r++)
        {
            var mapa = new Dictionary<string, int>();
            var row = tabla.Filas[r - 1];
            for (var c = 1; c <= row.Length; c++)
            {
                var h = Normalizar(row[c - 1].Texto);
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
                Col("ESTADO"), Col("MOTIVO CANCELACION"), Col("FECHA TOMA CITA"), Col("EMAIL", "CORREO"), Col("TELEFONO", "CELULAR"));
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

    private static CeldaAgenda Celda(CeldaAgenda[] row, int? col) =>
        col is { } c && c <= row.Length ? row[c - 1] : CeldaAgenda.Vacia;

    private static DateOnly? LeerFecha(CeldaAgenda cell)
    {
        if (cell.EstaVacia) return null;
        if (cell.FechaHora is { } fh) return DateOnly.FromDateTime(fh);
        if (cell.Numero is { } num) return DateOnly.FromDateTime(DateTime.FromOADate(num));
        string[] formatos = ["dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d-M-yyyy", "d/M/yyyy", "yyyy/MM/dd"];
        var soloFecha = cell.Texto.Trim().Split(' ')[0];
        return DateOnly.TryParseExact(soloFecha, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static TimeOnly? LeerHora(CeldaAgenda cell)
    {
        if (cell.EstaVacia) return null;
        if (cell.FechaHora is { } fh) return TimeOnly.FromDateTime(fh);
        if (cell.Hora is { } ts) return TimeOnly.FromTimeSpan(ts);
        if (cell.Numero is { } num)
        {
            var frac = num % 1;
            return frac < 0 ? null : TimeOnly.FromTimeSpan(TimeSpan.FromDays(frac));
        }
        string[] formatos = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"];
        return TimeOnly.TryParseExact(cell.Texto.Trim(), formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
    }

    /// <summary>Booking moment, truncated to the minute (the CSV has no seconds) so xlsx and CSV give the same key.</summary>
    private static DateTime? LeerToma(CeldaAgenda cell)
    {
        if (cell.EstaVacia) return null;
        DateTime? valor = cell.FechaHora;
        if (valor is null && cell.Numero is { } num) valor = DateTime.FromOADate(num);
        if (valor is null)
        {
            string[] formatos =
            [
                "dd/MM/yyyy HH:mm", "dd/MM/yyyy H:mm", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm", "d/M/yyyy H:mm:ss",
                "dd-MM-yyyy HH:mm", "dd-MM-yyyy HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
            ];
            if (!DateTime.TryParseExact(cell.Texto.Trim(), formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return null;
            valor = t;
        }
        var v = valor.Value;
        return new DateTime(v.Year, v.Month, v.Day, v.Hour, v.Minute, 0, DateTimeKind.Unspecified);
    }

    private static string? Texto(CeldaAgenda[] row, int? col)
    {
        var c = Celda(row, col);
        return c.Texto.Length == 0 ? null : c.Texto;
    }

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
