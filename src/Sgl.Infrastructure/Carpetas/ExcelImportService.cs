using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Infrastructure.Carpetas;

public sealed record ExcelImportResult(
    int FilasLeidas,
    int CarpetasCreadas,
    int CarpetasActualizadas,
    int CiudadanosCreados,
    IReadOnlyList<string> SedesProcesadas,
    IReadOnlyList<string> Errores
);

public sealed class ExcelImportService(SglDbContext db, TimeProvider clock)
{
    public async Task<ExcelImportResult> ImportarExcelAsync(Stream stream, string autor, CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook(stream);
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

                try
                {
                    // 1. Extraer y normalizar RUT
                    var rawRut = ObtenerValorCelda(row, colMap.Rut);
                    var rutObj = NormalizarRut(rawRut);
                    if (rutObj is null)
                    {
                        continue; // Fila sin RUT válido, se omite
                    }

                    // 2. Extraer Nombres y Apellidos en MAYÚSCULAS
                    var rawNombre = ObtenerValorCelda(row, colMap.Nombre);
                    var rawApellido = ObtenerValorCelda(row, colMap.Apellido);
                    var rawCompleto = ObtenerValorCelda(row, colMap.NombreCompleto);

                    string nombre;
                    string apellido;

                    if (string.IsNullOrWhiteSpace(rawNombre) && string.IsNullOrWhiteSpace(rawApellido))
                    {
                        if (!string.IsNullOrWhiteSpace(rawCompleto))
                        {
                            var parts = rawCompleto.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            nombre = parts[0].ToUpperInvariant();
                            apellido = (parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "VECINO").ToUpperInvariant();
                        }
                        else
                        {
                            nombre = "VECINO";
                            apellido = "SIN APELLIDO";
                        }
                    }
                    else
                    {
                        nombre = (string.IsNullOrWhiteSpace(rawNombre) ? "VECINO" : rawNombre.Trim()).ToUpperInvariant();
                        apellido = (string.IsNullOrWhiteSpace(rawApellido) ? "VECINO" : rawApellido.Trim()).ToUpperInvariant();
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
                        ciudadanosCreados++;
                    }

                    // 4. Fechas y Comuna
                    var fechaCitacion = ParsearFecha(ObtenerValorCelda(row, colMap.FechaCitacion)) ?? DateOnly.FromDateTime(now);
                    var fechaSubida = ParsearFecha(ObtenerValorCelda(row, colMap.FechaSubida));
                    var rawUltimaCarpeta = ObtenerValorCelda(row, colMap.UltimaCarpeta);
                    var fechaUltimaCarpeta = string.IsNullOrWhiteSpace(rawUltimaCarpeta) ? null : rawUltimaCarpeta.Trim().ToUpperInvariant();
                    var rawComuna = ObtenerValorCelda(row, colMap.Comuna);
                    var comuna = string.IsNullOrWhiteSpace(rawComuna) ? null : rawComuna.Trim().ToUpperInvariant();
                    if (comuna?.Length > Carpeta.MaxComunaLength) comuna = comuna[..Carpeta.MaxComunaLength];

                    // 5. Estado y Decisión
                    var rawEstado = ObtenerValorCelda(row, colMap.Estado);
                    var rawDecision = ObtenerValorCelda(row, colMap.Decision);
                    var (estado, decision, tipoTramite) = MapearEstadoYDecision(rawEstado, rawDecision);

                    // Si el estado es de subida y no viene fecha de subida, se asigna hoy si ya está subido
                    if (fechaSubida is null && (estado is EstadoCarpeta.SubidaConaset or EstadoCarpeta.SubidaConF8 or EstadoCarpeta.CambioDomSubidoConaset or EstadoCarpeta.CambioDomSubidoCorreo or EstadoCarpeta.SubidaConOficio))
                    {
                        fechaSubida = DateOnly.FromDateTime(now);
                    }

                    var rawObs = ObtenerValorCelda(row, colMap.Observacion);
                    var obs = string.IsNullOrWhiteSpace(rawObs) ? null : rawObs.Trim().ToUpperInvariant();
                    if (obs?.Length > Carpeta.MaxIdoneidadLength) obs = obs[..Carpeta.MaxIdoneidadLength];

                    // 6. Buscar si ya existe la carpeta para este ciudadano en esta sede y citación
                    var carpetaExistente = await db.Carpetas
                        .FirstOrDefaultAsync(c => c.CiudadanoId == ciudadanoId && c.Sede == sede && c.FechaCitacion == fechaCitacion, ct);

                    if (carpetaExistente is null)
                    {
                        var nuevaCarpeta = Carpeta.CrearImportada(
                            Guid.NewGuid(),
                            null!,
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
                        // Asignar CiudadanoId directamente para evitar tracking issues
                        typeof(Carpeta).GetProperty(nameof(Carpeta.CiudadanoId))!.SetValue(nuevaCarpeta, ciudadanoId);
                        db.Carpetas.Add(nuevaCarpeta);
                        carpetasCreadas++;
                    }
                    else
                    {
                        carpetaExistente.ForzarEstado(estado, usuario, now);
                        carpetaExistente.CambiarDecision(decision, usuario, now);
                        if (!string.IsNullOrEmpty(comuna)) carpetaExistente.AsignarComuna(comuna, usuario, now);
                        if (!string.IsNullOrEmpty(fechaUltimaCarpeta)) carpetaExistente.ModificarFechaUltimaCarpeta(fechaUltimaCarpeta, usuario, now);
                        if (!string.IsNullOrEmpty(obs)) carpetaExistente.ModificarObservacion(obs, usuario, now);
                        carpetasActualizadas++;
                    }

                    // Flush incremental en lotes para mantener memoria controlada
                    if (totalLeidas % 500 == 0)
                    {
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception ex)
                {
                    if (errores.Count < 20)
                    {
                        errores.Add($"Fila {rowNum} ({ws.Name}): {ex.Message}");
                    }
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
            errores
        );
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

        // Calcular dígito verificador automático
        var expectedDv = Rut.ComputeCheckDigit(body);
        return Rut.TryParse($"{body}-{expectedDv}", out var rut) ? rut : null;
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
                return new ColumnMapping(
                    colCitacion ?? 1,
                    colSubida ?? 2,
                    colUltima ?? 3,
                    colNombre ?? 4,
                    colApellido ?? 5,
                    colCompleto ?? 6,
                    colRut,
                    colComuna ?? 8,
                    colEstado ?? 9,
                    colDecision ?? 10,
                    colObs ?? 11
                );
            }
        }

        // Si no se detectan nombres de encabezados, usar las posiciones estándar de import_excel.py
        headerRowIndex = 2;
        return new ColumnMapping(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
    }
}
