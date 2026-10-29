using System.Globalization;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sgl.Application.Integridad;
using Sgl.Domain;
using PeticionCambioDomicilio.Excel;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Infrastructure.Carpetas;

public sealed record ExcelImportResult(
    int FilasLeidas,
    int CarpetasCreadas,
    int CarpetasActualizadas,
    int CiudadanosCreados,
    IReadOnlyList<string> SedesProcesadas,
    IReadOnlyList<string> Errores,
    int TotalErrores = 0,
    int ComunasNoReconocidas = 0,
    IReadOnlyList<string>? AvisosImportacion = null,
    int AvisosTotal = 0,
    int EstadosNoAplicados = 0
)
{
    /// <summary>Observaciones que no impiden importar la fila (p. ej. subida anterior a la citación).</summary>
    public IReadOnlyList<string> Avisos { get; } = AvisosImportacion ?? [];
}

public sealed class ExcelImportService(
    SglDbContext db, TimeProvider clock, ILogger<ExcelImportService> logger, IntegridadSolicitudes? integridad = null)
{
    private const int MaxMensajesError = 20;
    private const int TamanoLoteTracker = 500;

    public async Task<ExcelImportResult> ImportarExcelAsync(Stream stream, string autor, CancellationToken ct = default)
    {
        try
        {
            using var sanitized = await QuitarValidacionesDeDatosAsync(stream, ct);
            using var workbook = new XLWorkbook(sanitized);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await ImportarWorkbookAsync(workbook, autor, ct);
            db.ImportacionLogs.Add(new ImportacionLog(
                clock.GetUtcNow().UtcDateTime, autor, result.FilasLeidas, result.CarpetasCreadas,
                result.CarpetasActualizadas, Math.Max(result.TotalErrores, result.Errores.Count)));
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
                logger.LogError(ex, "Excel import failed for author {Autor}", autor);
            }
            throw;
        }
    }

    /// <summary>
    /// ClosedXML rejects data validations whose formula exceeds 255 chars (Excel itself allows them in lists
    /// referencing long inline values). The importer never uses validations, so they are stripped via the
    /// OpenXML SDK before ClosedXML loads the workbook.
    /// </summary>
    internal static async Task<MemoryStream> QuitarValidacionesDeDatosAsync(Stream source, CancellationToken ct)
    {
        var copy = new MemoryStream();
        await source.CopyToAsync(copy, ct);
        WorkbookSanitizer.ValidarStream(copy);

        using (var doc = SpreadsheetDocument.Open(copy, true))
        {
            foreach (var wsPart in doc.WorkbookPart?.WorksheetParts ?? [])
            {
                var validations = wsPart.Worksheet.Elements<DataValidations>().ToList();
                if (validations.Count == 0) continue;
                validations.ForEach(v => v.Remove());
                wsPart.Worksheet.Save();
            }
        }

        copy.Position = 0;
        return copy;
    }

    private async Task<ExcelImportResult> ImportarWorkbookAsync(XLWorkbook workbook, string autor, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var usuario = string.IsNullOrWhiteSpace(autor) ? "IMPORTACION_EXCEL" : autor.Trim().ToUpperInvariant();

        var sheetsToProcess = workbook.Worksheets
            .Where(ws => !ws.Name.StartsWith("PLANTILLA", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sheetsToProcess.Count == 0)
        {
            return new ExcelImportResult(0, 0, 0, 0, [], ["El archivo Excel no contiene hojas válidas para importar."]);
        }

        var totalLeidas = 0;
        var carpetasCreadas = 0;
        var carpetasActualizadas = 0;
        var ciudadanosCreados = 0;
        var sedesProcesadas = new HashSet<string>();
        var errores = new List<string>();
        var avisos = new List<string>();
        var totalErrores = 0;
        var avisosTotal = 0;
        var estadosNoAplicados = 0;
        var hoy = HoraChile.Fecha(now);

        void Rechazar(int fila, string hoja, string motivo)
        {
            totalErrores++;
            if (errores.Count < MaxMensajesError)
            {
                errores.Add($"Fila {fila} ({hoja}): {motivo}");
            }
        }
        void Avisar(string mensaje)
        {
            avisosTotal++;
            if (avisos.Count < MaxMensajesError)
            {
                avisos.Add(mensaje);
            }
        }
        var comunasNoReconocidas = 0;

        // Cache existing ciudadanos by Rut to optimize lookups
        var rutsInDb = await db.Ciudadanos
            .Select(c => new { c.Id, Rut = c.Rut.Value })
            .ToDictionaryAsync(x => x.Rut, x => x.Id, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var ws in sheetsToProcess)
        {
            var sede = DeterminarSede(ws.Name);
            sedesProcesadas.Add(sede.ToString().ToUpperInvariant());

            var lastRowUsed = ws.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRowUsed < 2) continue;

            // Detect header row: usually row 1 or 2
            var headerRowIndex = 1;
            var colMap = DetectarColumnas(ws, ref headerRowIndex);

            for (var rowNum = headerRowIndex + 1; rowNum <= lastRowUsed; rowNum++)
            {
                ct.ThrowIfCancellationRequested();
                var row = ws.Row(rowNum);
                if (row.IsEmpty()) continue;

                totalLeidas++;
                string? rutAgregado = null;

                try
                {
                    // 1. Extraer y normalizar RUT
                    var rawRut = ObtenerValorCelda(row, colMap.Rut);
                    if (string.IsNullOrWhiteSpace(rawRut))
                    {
                        continue; // Fila sin RUT, se omite
                    }

                    var rutObj = NormalizarRut(rawRut);
                    if (rutObj is null)
                    {
                        // El RUT no se repite en el mensaje: es un dato personal.
                        Rechazar(rowNum, ws.Name, "RUT con formato o dígito verificador inválido; fila omitida.");
                        continue;
                    }

                    // La citación define la carpeta: sin una fecha válida no se inventa "hoy", se reporta y se salta.
                    var citacionLeida = ParsearFecha(ObtenerValorCelda(row, colMap.FechaCitacion));
                    if (citacionLeida is not { } fechaCitacion || !RangoFechasImportacion.EsPlausible(fechaCitacion, hoy))
                    {
                        Rechazar(rowNum, ws.Name, "sin fecha de citación válida; fila omitida.");
                        continue;
                    }

                    // 2. Extraer Nombres y Apellidos en MAYÚSCULAS
                    var rawNombre = ObtenerValorCelda(row, colMap.Nombre);
                    var rawApellido = ObtenerValorCelda(row, colMap.Apellido);
                    var rawCompleto = ObtenerValorCelda(row, colMap.NombreCompleto);

                    string nombre;
                    string apellido;

                    if (!string.IsNullOrWhiteSpace(rawNombre) && !string.IsNullOrWhiteSpace(rawApellido))
                    {
                        nombre = rawNombre.Trim().ToUpperInvariant();
                        apellido = rawApellido.Trim().ToUpperInvariant();
                    }
                    else
                    {
                        // A single name column holds the full name: split it (last two words = surnames).
                        var completo = new[] { rawCompleto, rawNombre, rawApellido }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                        (nombre, apellido) = AgendaImportService.SepararNombre(completo);
                    }

                    if (nombre.Length > Ciudadano.MaxNameLength) nombre = nombre[..Ciudadano.MaxNameLength];
                    if (apellido.Length > Ciudadano.MaxNameLength) apellido = apellido[..Ciudadano.MaxNameLength];

                    // 3. Upsert Ciudadano
                    Guid ciudadanoId;
                    if (!rutsInDb.TryGetValue(rutObj.Value, out ciudadanoId))
                    {
                        var nuevoCiudadano = new Ciudadano(rutObj, nombre, apellido);
                        db.Ciudadanos.Add(nuevoCiudadano);
                        ciudadanoId = nuevoCiudadano.Id;
                        rutsInDb[rutObj.Value] = ciudadanoId;
                        rutAgregado = rutObj.Value;
                    }

                    // 4. Fechas y Comuna
                    var fechaSubida = ParsearFecha(ObtenerValorCelda(row, colMap.FechaSubida));
                    if (fechaSubida is { } subida && !RangoFechasImportacion.EsPlausible(subida, hoy))
                    {
                        Avisar($"Fila {rowNum} ({ws.Name}): fecha de subida fuera de rango; se ignoró.");
                        fechaSubida = null;
                    }
                    else if (fechaSubida < fechaCitacion)
                    {
                        Avisar($"Fila {rowNum} ({ws.Name}): la fecha de subida es anterior a la citación; revisar el Excel.");
                    }

                    var rawUltimaCarpeta = ObtenerValorCelda(row, colMap.UltimaCarpeta);
                    var fechaUltimaCarpeta = string.IsNullOrWhiteSpace(rawUltimaCarpeta) ? null : rawUltimaCarpeta.Trim().ToUpperInvariant();
                    var rawComuna = ObtenerValorCelda(row, colMap.Comuna);
                    string? comuna = null;
                    if (!string.IsNullOrWhiteSpace(rawComuna))
                    {
                        // Unknown comunas never discard the row: the rest of the data is applied and the comuna is left unset.
                        comuna = ComunasChile.Normalizar(rawComuna);
                        if (comuna is null) comunasNoReconocidas++;
                    }

                    // 5. Estado y Decisión
                    var rawEstado = ObtenerValorCelda(row, colMap.Estado);
                    var rawDecision = ObtenerValorCelda(row, colMap.Decision);
                    var (estado, decision, tipoTramite) = MapearEstadoYDecision(rawEstado, rawDecision);

                    // FechaSubida is only taken from the Excel; it is never invented.

                    var rawObs = ObtenerValorCelda(row, colMap.Observacion);
                    var obs = string.IsNullOrWhiteSpace(rawObs) ? null : rawObs.Trim().ToUpperInvariant();
                    if (obs?.Length > Carpeta.MaxIdoneidadLength) obs = obs[..Carpeta.MaxIdoneidadLength];

                    // 6. Buscar si ya existe la carpeta para este ciudadano en esta sede y citación
                    var carpetaExistente = await db.Carpetas
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.CiudadanoId == ciudadanoId && c.Sede == sede && c.FechaCitacion == fechaCitacion, ct);

                    if (carpetaExistente is null)
                    {
                        var ciudadano = await db.Ciudadanos.FindAsync([ciudadanoId], ct)
                            ?? throw new InvalidOperationException("Ciudadano no encontrado para la carpeta importada.");
                        var nuevaCarpeta = Carpeta.CrearImportada(
                            Guid.NewGuid(),
                            ciudadano,
                            sede,
                            fechaCitacion,
                            fechaSubida,
                            fechaUltimaCarpeta,
                            estado,
                            decision,
                            obs,
                            tipoTramite,
                            null,
                            comuna,
                            now
                        );
                        db.Carpetas.Add(nuevaCarpeta);
                    }
                    else
                    {
                        // El Excel solo hace avanzar la carpeta hacia un estado alcanzable por la máquina de estados.
                        // Citada nunca es destino (un estado vacío se lee como "Citada" y no debe sacar una carpeta de
                        // Por clasificar); un estado atrasado o inalcanzable no se aplica y se cuenta. Una decisión
                        // en blanco no borra la ya tomada.
                        if (estado != EstadoCarpeta.Citada && EsAvance(carpetaExistente.Estado, estado))
                        {
                            carpetaExistente.ForzarEstado(estado, usuario, now);
                        }
                        else if (!string.IsNullOrWhiteSpace(rawEstado) && estado != carpetaExistente.Estado)
                        {
                            estadosNoAplicados++;
                        }

                        if (decision != Decision.Pendiente) carpetaExistente.CambiarDecision(decision, usuario, now);
                        if (!string.IsNullOrEmpty(comuna)) carpetaExistente.AsignarComuna(comuna, usuario, now);
                        if (!string.IsNullOrEmpty(fechaUltimaCarpeta)) carpetaExistente.ModificarFechaUltimaCarpeta(fechaUltimaCarpeta, usuario, now);
                        if (!string.IsNullOrEmpty(obs)) carpetaExistente.ModificarObservacion(obs, usuario, now);
                    }

                    // Flush per row inside the transaction so a failing row never leaves half-tracked changes.
                    // EF wraps each SaveChanges in a savepoint, so a failed flush only undoes that row.
                    await db.SaveChangesAsync(ct);

                    // Count only rows that were actually persisted.
                    if (rutAgregado is not null) ciudadanosCreados++;
                    if (carpetaExistente is null) carpetasCreadas++; else carpetasActualizadas++;
                    if (totalLeidas % TamanoLoteTracker == 0) db.ChangeTracker.Clear();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Discard everything this row added/modified (previous rows are already flushed).
                    db.ChangeTracker.Clear();
                    if (rutAgregado is not null) rutsInDb.Remove(rutAgregado);
                    Rechazar(rowNum, ws.Name, ex.Message);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        return new ExcelImportResult(
            totalLeidas,
            carpetasCreadas,
            carpetasActualizadas,
            ciudadanosCreados,
            sedesProcesadas.ToList(),
            errores,
            totalErrores,
            comunasNoReconocidas,
            avisos,
            avisosTotal,
            estadosNoAplicados
        );
    }

    /// <summary>true si <paramref name="destino"/> es alcanzable desde <paramref name="actual"/> por la máquina de estados.</summary>
    private static bool EsAvance(EstadoCarpeta actual, EstadoCarpeta destino)
    {
        if (actual == destino) return false;
        var visitados = new HashSet<EstadoCarpeta> { actual };
        var pendientes = new Queue<EstadoCarpeta>();
        pendientes.Enqueue(actual);
        while (pendientes.Count > 0)
        {
            foreach (var siguiente in EstadoTransitions.NextStates(pendientes.Dequeue()))
            {
                if (siguiente == destino) return true;
                if (visitados.Add(siguiente)) pendientes.Enqueue(siguiente);
            }
        }

        return false;
    }

    private static Sede DeterminarSede(string sheetName)
    {
        var upper = sheetName.ToUpperInvariant();
        if (upper.Contains("PLACILLA")) return Sede.Placilla;
        if (upper.Contains("MERC") || upper.Contains("PUERTO")) return Sede.MercadoPuerto;
        return Sede.AvArgentina;
    }

    private static (EstadoCarpeta Estado, Decision Decision, string? TipoTramite) MapearEstadoYDecision(string? rawEstado, string? rawDecision)
    {
        var estadoStr = (rawEstado ?? "").ToUpperInvariant().Trim();
        var decStr = (rawDecision ?? "").ToUpperInvariant().Trim();

        var decision = Decision.Pendiente;
        if (decStr.Contains("OTORGADO")) decision = Decision.Otorgado;
        else if (decStr.Contains("DENEGADO")) decision = Decision.Denegado;

        // Mapear los 12 estados oficiales municipales
        if (estadoStr.Contains("1") && (estadoStr.Contains("LIC") || estadoStr.Contains("PRIMERA")))
        {
            return (EstadoCarpeta.PrimeraLicencia, decision, "1° LICENCIA");
        }
        if (estadoStr.Contains("SUBIDA") && estadoStr.Contains("F8"))
        {
            return (EstadoCarpeta.SubidaConF8, decision, "SUBIDA CON F8");
        }
        if (estadoStr.Contains("SUBIDA") && estadoStr.Contains("OFICIO"))
        {
            return (EstadoCarpeta.SubidaConOficio, decision, "SUBIDA CON OFICIO");
        }
        if (estadoStr.Contains("SUBIDA") && estadoStr.Contains("CONASET"))
        {
            return (EstadoCarpeta.SubidaConaset, decision, "SUBIDA A CONASET");
        }
        if (estadoStr.Contains("CAMBIO") && estadoStr.Contains("CORREO"))
        {
            return (EstadoCarpeta.CambioDomSubidoCorreo, decision, "CAMBIO DOM. SUBIDO CON CORREO");
        }
        if (estadoStr.Contains("CAMBIO") && estadoStr.Contains("CONASET"))
        {
            return (EstadoCarpeta.CambioDomSubidoConaset, decision, "CAMBIO DOM. SUBIDO A CONASET");
        }
        if (estadoStr.Contains("CAMBIO") && estadoStr.Contains("SOLICITADO"))
        {
            return (EstadoCarpeta.CambioDomicilioSolicitado, decision, "CAMBIO DE DOMICILIO SOLICITADO");
        }
        if (estadoStr.Contains("CAMBIO") || estadoStr.Contains("DOMICILIO"))
        {
            return (EstadoCarpeta.CambioDomicilio, decision, "CAMBIO DE DOMICILIO");
        }
        if (estadoStr.Contains("NO EXISTE"))
        {
            return (EstadoCarpeta.NoExisteCarpeta, decision, "NO EXISTE CARPETA");
        }
        if (estadoStr.Contains("OF. 43") || estadoStr.Contains("OF.43") || estadoStr.Contains("OFICINA 43"))
        {
            return (EstadoCarpeta.SeEncuentraEnOf43, decision, "SE ENCUENTRA EN OF. 43");
        }
        if (estadoStr.Contains("ARCHIVO") || estadoStr.Contains("ARCHIVOS"))
        {
            return (EstadoCarpeta.SeEncuentraEnArchivos, decision, "SE ENCUENTRA EN ARCHIVOS");
        }
        if (estadoStr.Contains("CERTIFICADO"))
        {
            return (EstadoCarpeta.CrearCertificado, decision, "CREAR CERTIFICADO");
        }
        if (estadoStr.Contains("CANJE"))
        {
            return (EstadoCarpeta.CanjeLicExtranjera, decision, "CANJE LIC. EXTRANJERA");
        }

        // Si no hay estado claro, deducir por decisión o citada por defecto
        if (decision == Decision.Otorgado) return (EstadoCarpeta.Otorgado, decision, null);
        if (decision == Decision.Denegado) return (EstadoCarpeta.Denegado, decision, null);

        return (EstadoCarpeta.Citada, decision, null);
    }

    private static Rut? NormalizarRut(string? rawVal)
    {
        if (string.IsNullOrWhiteSpace(rawVal)) return null;
        var val = rawVal.Trim().Replace(".", "").Replace("-", "").ToUpperInvariant();
        if (val.Length < 2) return null;

        var body = val[..^1].TrimStart('0');
        if (string.IsNullOrEmpty(body) || body.Length > 9 || !body.All(char.IsAsciiDigit))
        {
            return null;
        }

        // El dígito verificador del archivo debe coincidir: antes se recalculaba y se aceptaba cualquier RUT
        // tipeado mal, asociando la carpeta a otra persona.
        return Rut.TryParse($"{body}-{val[^1]}", out var rut) ? rut : null;
    }

    private static DateOnly? ParsearFecha(string? rawVal)
    {
        if (string.IsNullOrWhiteSpace(rawVal)) return null;

        string[] formats = ["yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "yyyy/MM/dd", "d-M-yyyy", "d/M/yyyy"];
        if (DateTime.TryParseExact(rawVal.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return DateOnly.FromDateTime(dt);
        }
        if (DateTime.TryParse(rawVal.Trim(), CultureInfo.GetCultureInfo("es-CL"), DateTimeStyles.None, out dt))
        {
            return DateOnly.FromDateTime(dt);
        }

        return null;
    }

    private static string? ObtenerValorCelda(IXLRow row, int? colIndex)
    {
        if (!colIndex.HasValue || colIndex.Value < 1) return null;
        var cell = row.Cell(colIndex.Value);
        if (cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime().ToString("yyyy-MM-dd");
        }

        return cell.GetString()?.Trim();
    }

    private sealed record ColumnMapping(
        int? FechaCitacion,
        int? FechaSubida,
        int? UltimaCarpeta,
        int? Nombre,
        int? Apellido,
        int? NombreCompleto,
        int? Rut,
        int? Comuna,
        int? Estado,
        int? Decision,
        int? Observacion
    );

    private static ColumnMapping DetectarColumnas(IXLWorksheet ws, ref int headerRowIndex)
    {
        // Buscar en las primeras 5 filas para ubicar encabezados
        for (var r = 1; r <= Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 1); r++)
        {
            var row = ws.Row(r);
            int? colCitacion = null, colSubida = null, colUltima = null, colNombre = null, colApellido = null;
            int? colCompleto = null, colRut = null, colComuna = null, colEstado = null, colDecision = null, colObs = null;

            var lastCol = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
            for (var c = 1; c <= lastCol; c++)
            {
                var txt = row.Cell(c).GetString().Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(txt)) continue;

                if (txt.Contains("ATENC")) continue;
                if (txt.Contains("RUT")) colRut = c;
                else if (txt.Contains("CITAC")) colCitacion = c;
                else if (txt.Contains("SUBID")) colSubida = c;
                else if (txt.Contains("ULTIM") || txt.Contains("ÚLTIM")) colUltima = c;
                else if (txt.Contains("NOMBRE COMPLETO") || txt.Contains("CIUDADANO")) colCompleto = c;
                else if (txt.Contains("NOMBRE")) colNombre = c;
                else if (txt.Contains("APELLIDO")) colApellido = c;
                else if (txt.Contains("COMUNA")) colComuna = c;
                else if (txt.Contains("ESTADO")) colEstado = c;
                else if (txt.Contains("DECIS") || txt.Contains("RESOLU")) colDecision = c;
                else if (txt.Contains("OBSERV") || txt.Contains("IDONEIDAD") || txt.Contains("MORAL")) colObs = c;
            }

            if (colRut.HasValue)
            {
                headerRowIndex = r;
                // Only columns whose header was recognised are read; nothing is guessed by position.
                return new ColumnMapping(
                    colCitacion, colSubida, colUltima, colNombre, colApellido, colCompleto,
                    colRut, colComuna, colEstado, colDecision, colObs);
            }
        }

        // Si no se detectan nombres de encabezados, usar las posiciones estándar de import_excel.py
        headerRowIndex = 2;
        return new ColumnMapping(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
    }
}
