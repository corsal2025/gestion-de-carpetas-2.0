using ClosedXML.Excel;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Excel;

public sealed record ImportResult(
    int HojasLeidas,
    int FilasLeidas,
    int FilasCambioDomicilio,
    int Nuevas,
    int Duplicadas,
    int RutInvalidos,
    int ComunaNoReconocida,
    int Obsoletas,
    int CarpetasSincronizadas,
    IReadOnlyList<string> Avisos);

/// <summary>
/// Lee el libro DETALLE CARPETAS (todas las hojas de agenda mensual: Av. Argentina, Placilla y
/// Merc. Puerto de cada mes), se queda con las filas cuya columna "Estado de la carpeta" dice
/// "CAMBIO DE DOMICILIO" (configurable) y arma una petición por fila:
///   Nombre  = NOMBRE COMPLETO
///   RUT     = RUT (normalizado y validado)
///   Comuna  = FECHA ULTIMA CARPETA (cuando trae texto de comuna, no fecha) — calzada contra el directorio
///   Fecha   = FECHA DE LA CITACION
/// Los encabezados se ubican por nombre sin distinguir mayúsculas ni tildes; la fila de encabezado
/// se detecta sola (primera que contiene "RUT"). El orden de las columnas no importa.
/// </summary>
public sealed class ExcelPeticionImporter
{
    private readonly AppOptions _options;
    private readonly ComunaDirectory _directory;

    public ExcelPeticionImporter(AppOptions options, ComunaDirectory directory)
    {
        _options = options;
        _directory = directory;
    }

    public ImportResult Import(
        string excelPath,
        Func<Peticion, bool> addIfNew,
        IReadOnlyList<Peticion>? existentes = null,
        Action<long>? borrar = null,
        Func<long, string, DateOnly?, bool>? sincronizarCarpeta = null)
    {
        var avisos = new List<string>();
        using var loaded = LoadedWorkbook.Open(excelPath);
        return ImportCore(loaded.Workbook, avisos, addIfNew, existentes, borrar, sincronizarCarpeta);
    }

    /// <summary>
    /// Pasada liviana: recorre TODO el libro buscando, por RUT (o por nombre si el RUT no valida),
    /// el estado de la carpeta de cada petición que ya tenemos. Si en el Excel figura más avanzada
    /// que en la app, la sincroniza (y anota la fecha de subida). No crea ni borra nada.
    /// Devuelve cuántas carpetas cambiaron.
    /// </summary>
    public int ActualizarEstadosCarpeta(
        string excelPath,
        IReadOnlyList<Peticion> peticiones,
        Func<long, string, DateOnly?, bool> sincronizarCarpeta)
    {
        using var loaded = LoadedWorkbook.Open(excelPath);
        var ignoradas = _options.HojasIgnoradas.Select(TextNormalization.Fold).ToArray();

        // clave (rut o nombre) -> el estado MÁS avanzado que aparezca para esa persona en el libro
        var enExcel = new Dictionary<string, (string Estado, DateOnly? Fecha, int Rango)>(StringComparer.Ordinal);

        foreach (var sheet in loaded.Workbook.Worksheets)
        {
            var folded = TextNormalization.Fold(sheet.Name);
            if (ignoradas.Any(ig => folded.Contains(ig)))
            {
                continue;
            }

            var used = sheet.RangeUsed();
            if (used is null)
            {
                continue;
            }

            var (headerRow, headers) = FindHeaderRow(used);
            if (headerRow is null)
            {
                continue;
            }

            int? Col(string name) =>
                headers.TryGetValue(TextNormalization.Fold(name), out var c) ? c : null;

            var cNombre = Col(_options.Columns.NombreCompleto);
            var cRut = Col(_options.Columns.Rut);
            var cEstado = Col(_options.Columns.EstadoCarpeta)
                          ?? (Col("DECISION FINAL") is > 1 ? Col("DECISION FINAL") - 1 : null);
            var cFechaSubida = Col(_options.Columns.FechaSubidaCarpeta);
            if (cRut is null || cEstado is null)
            {
                continue;
            }

            foreach (var row in used.Rows())
            {
                if (row.RowNumber() <= headerRow.Value || row.Cell(cRut.Value).IsEmpty())
                {
                    continue;
                }

                var estadoCrudo = row.Cell(cEstado.Value).GetString();
                var rango = EstadoCarpetaCatalog.Rango(estadoCrudo);
                if (rango < 2)
                {
                    continue; // solo lo que puede hacer avanzar
                }

                var rutRaw = row.Cell(cRut.Value).GetString().Trim();
                var nombre = cNombre is null ? "" : row.Cell(cNombre.Value).GetString().Trim();
                var rn = RutValidator.NormalizeAndValidate(rutRaw);
                var clave = rn is not null ? "rut:" + rn : "nom:" + TextNormalization.Fold(nombre);
                if (clave is "nom:")
                {
                    continue;
                }

                var fecha = cFechaSubida is null ? null : ReadDate(row.Cell(cFechaSubida.Value));
                if (!enExcel.TryGetValue(clave, out var actual) || rango > actual.Rango)
                {
                    enExcel[clave] = (estadoCrudo, fecha, rango);
                }
            }
        }

        var cambiadas = 0;
        foreach (var p in peticiones)
        {
            var clave = !p.RutInvalido ? "rut:" + p.Rut : "nom:" + TextNormalization.Fold(p.NombreCompleto);
            if (!enExcel.TryGetValue(clave, out var hit))
            {
                continue;
            }

            var subida = hit.Rango >= 3
                ? (hit.Fecha ?? DateOnly.FromDateTime(DateTime.Today))
                : (DateOnly?)null;
            if (sincronizarCarpeta(p.Id, hit.Estado, subida))
            {
                cambiadas++;
            }
        }

        return cambiadas;
    }

    private ImportResult ImportCore(
        XLWorkbook workbook,
        List<string> avisos,
        Func<Peticion, bool> addIfNew,
        IReadOnlyList<Peticion>? existentes,
        Action<long>? borrar,
        Func<long, string, DateOnly?, bool>? sincronizarCarpeta)
    {
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Índice de las peticiones que ya tenemos, por RUT|Comuna, para poder engancharles
        // el avance de estado que viene del Excel aunque su fila ya no diga "CAMBIO DE DOMICILIO".
        var porClave = existentes?
            .GroupBy(p => p.Rut + "|" + p.Comuna, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var ordenObjetivo = TextNormalization.Fold(_options.EstadoCambioDomicilio);
        var ignoradas = _options.HojasIgnoradas.Select(TextNormalization.Fold).ToArray();

        int hojas = 0, filas = 0, cd = 0, nuevas = 0, dup = 0, rutInv = 0, comunaNo = 0, sincro = 0;
        long orden = 0; // posicion global: hojas en el orden del libro, filas de arriba abajo

        foreach (var sheet in workbook.Worksheets)
        {
            var sheetFolded = TextNormalization.Fold(sheet.Name);
            if (ignoradas.Any(ig => sheetFolded.Contains(ig)))
            {
                continue;
            }

            var used = sheet.RangeUsed();
            if (used is null)
            {
                continue;
            }

            var (headerRowNumber, headers) = FindHeaderRow(used);
            if (headerRowNumber is null)
            {
                avisos.Add($"Hoja \"{sheet.Name}\": no se encontró la fila de encabezados (con \"RUT\"), se omite.");
                continue;
            }

            int? Col(string name) =>
                headers.TryGetValue(TextNormalization.Fold(name), out var c) ? c : null;

            var cNombre = Col(_options.Columns.NombreCompleto);
            var cRut = Col(_options.Columns.Rut);
            var cEstado = Col(_options.Columns.EstadoCarpeta);
            var cComuna = Col(_options.Columns.ComunaOrigen);
            var cFecha = Col(_options.Columns.FechaSolicitud);
            var cClases = Col(_options.Columns.Clases);
            var cFechaSubida = Col(_options.Columns.FechaSubidaCarpeta);

            // Red de seguridad: si alguien escribe encima del encabezado "ESTADO DE LA CARPETA"
            // (pasó de verdad: quedó con el texto "CAMBIO DE DOMICILIO SOLICITADO"), la columna
            // sigue estando donde siempre — justo antes de "DECISIÓN FINAL". Se usa esa posición
            // en vez de descartar la hoja entera.
            if (cEstado is null)
            {
                var cDecision = Col("DECISION FINAL");
                if (cDecision is > 1)
                {
                    cEstado = cDecision.Value - 1;
                    avisos.Add($"Hoja \"{sheet.Name}\": el encabezado de \"{_options.Columns.EstadoCarpeta}\" " +
                               "no dice eso (alguien escribió un valor encima) — se usó la columna junto a " +
                               "\"DECISIÓN FINAL\" igual. Conviene corregir el encabezado en el Excel.");
                }
            }

            if (cNombre is null || cRut is null || cEstado is null || cComuna is null)
            {
                avisos.Add($"Hoja \"{sheet.Name}\": faltan columnas obligatorias " +
                           $"(Nombre/{cNombre}, RUT/{cRut}, Estado/{cEstado}, Comuna/{cComuna}), se omite.");
                continue;
            }

            hojas++;
            var oficina = OficinaDeHoja(sheet.Name);

            foreach (var row in used.Rows())
            {
                if (row.RowNumber() <= headerRowNumber.Value)
                {
                    continue;
                }

                if (cRut is null || row.Cell(cRut.Value).IsEmpty())
                {
                    continue;
                }

                filas++;
                orden++;

                var estadoCrudo = row.Cell(cEstado.Value).GetString().Trim();
                var esCambioDomicilio = TextNormalization.Fold(estadoCrudo) == ordenObjetivo;
                var rangoExcel = EstadoCarpetaCatalog.Rango(estadoCrudo);

                // Fila de otro trámite (1ª licencia, canje…) o vacía que no pertenece al flujo:
                if (!esCambioDomicilio && rangoExcel < 2)
                {
                    continue;
                }

                var nombre = row.Cell(cNombre.Value).GetString().Trim();
                var rutRaw = row.Cell(cRut.Value).GetString().Trim();
                var comunaCell = row.Cell(cComuna.Value);

                // Si la celda de comuna es una fecha real (formato DateTime o texto de fecha),
                // significa que no es un cambio de domicilio donde anotaron la comuna en esa columna:
                if (comunaCell.DataType == XLDataType.DateTime || ReadDate(comunaCell) != null)
                {
                    continue;
                }

                var comunaRaw = comunaCell.GetString().Trim();
                var clases = cClases is null ? null : row.Cell(cClases.Value).GetString().Trim();
                var fecha = cFecha is null ? null : ReadDate(row.Cell(cFecha.Value));
                var fechaSubida = cFechaSubida is null ? null : ReadDate(row.Cell(cFechaSubida.Value));

                var rutNormalizado = RutValidator.NormalizeAndValidate(rutRaw);
                var rutInvalido = rutNormalizado is null;
                var comunaCanonica = _directory.ResolveComunaName(comunaRaw);

                var clave = (rutNormalizado ?? (rutRaw.Length > 0 ? rutRaw : "SIN RUT"))
                          + "|"
                          + (comunaCanonica ?? (comunaRaw.Length > 0 ? comunaRaw : "(sin comuna)"));

                // Si dice "CAMBIO DE DOMICILIO SOLICITADO" y ya fue pedida a otra comuna:
                // Si ya la teníamos en la base, avanzamos su estado. Si no estaba, se ingresa como Enviada/Solicitada.
                if (!esCambioDomicilio)
                {
                    if (sincronizarCarpeta is not null && porClave is not null
                        && porClave.TryGetValue(clave, out var existente))
                    {
                        var subida = rangoExcel >= 3
                            ? (fechaSubida ?? DateOnly.FromDateTime(DateTime.Today))
                            : (DateOnly?)null;
                        if (sincronizarCarpeta(existente.Id, estadoCrudo, subida))
                        {
                            sincro++;
                        }
                        vistas.Add(clave);
                        continue;
                    }

                    // Si ya está subida a CONASET/resuelta y no existía, no la traemos como petición activa
                    if (rangoExcel >= 3)
                    {
                        continue;
                    }
                }

                cd++;

                if (rutInvalido)
                {
                    rutInv++;
                    avisos.Add($"{sheet.Name}!fila {row.RowNumber()}: RUT \"{rutRaw}\" no valida — se guarda para revisión.");
                }

                if (comunaCanonica is null)
                {
                    comunaNo++;
                    avisos.Add($"{sheet.Name}!fila {row.RowNumber()}: comuna \"{comunaRaw}\" no se reconoce en el directorio — se guarda para revisión.");
                }

                var estadoNorm = EstadoCarpetaCatalog.Normalizar(estadoCrudo);
                var esSolicitado = string.Equals(estadoNorm, "CAMBIO DE DOMICILIO SOLICITADO", StringComparison.OrdinalIgnoreCase);

                var estadoInicial = comunaCanonica is null 
                    ? EstadoPeticion.SinCorreoComuna 
                    : (esSolicitado ? EstadoPeticion.Enviada : EstadoPeticion.Borrador);

                DateTimeOffset? enviadaEn = esSolicitado 
                    ? (fecha.HasValue ? new DateTimeOffset(fecha.Value.ToDateTime(TimeOnly.MinValue)) : DateTimeOffset.Now) 
                    : null;

                var peticion = new Peticion
                {
                    NombreCompleto = nombre.Length > 0 ? nombre : "(sin nombre)",
                    Rut = rutNormalizado ?? (rutRaw.Length > 0 ? rutRaw : "SIN RUT"),
                    Comuna = comunaCanonica ?? (comunaRaw.Length > 0 ? comunaRaw : "(sin comuna)"),
                    Clases = string.IsNullOrWhiteSpace(clases) ? null : clases,
                    FechaSolicitud = fecha,
                    Origen = $"{sheet.Name}!fila {row.RowNumber()}",
                    Oficina = oficina,
                    OrdenImportacion = orden,
                    RutInvalido = rutInvalido,
                    Estado = estadoInicial,
                    EnviadaEn = enviadaEn,
                    DetalleEstado = comunaCanonica is null ? $"Comuna del Excel: \"{comunaRaw}\"" : (esSolicitado ? "Registrada como solicitada en planilla Excel" : null),
                    EstadoCarpeta = estadoNorm,
                };

                vistas.Add(clave);
                if (addIfNew(peticion))
                {
                    nuevas++;
                }
                else
                {
                    dup++;
                }
            }
        }

        // Historial completo: sólo se da de baja lo que ya estaba cargado y no reaparece en el libro
        // actual. Las peticiones cargadas a mano quedan protegidas aunque no estén en el Excel.
        var obsoletas = 0;
        if (existentes is not null && borrar is not null)
        {
            foreach (var existente in existentes)
            {
                if (existente.Oficina == Peticion.OficinaManual)
                {
                    continue;
                }

                var clave = existente.Rut + "|" + existente.Comuna;
                if (!vistas.Contains(clave) && !vistas.Contains(TextNormalization.Fold(existente.Rut) + "|" + TextNormalization.Fold(existente.Comuna)))
                {
                    borrar(existente.Id);
                    obsoletas++;
                }
            }
        }

        return new ImportResult(hojas, filas, cd, nuevas, dup, rutInv, comunaNo, obsoletas, sincro, avisos);
    }

    /// <summary>
    /// De "2DO SEM. AV. ARGENTINA" o "ENERO PLACILLA" saca solo la oficina. Son tres y solo tres:
    /// AV. ARGENTINA, PLACILLA y MERC. PUERTO. Es lo unico que se muestra en la columna Origen.
    /// </summary>
    private static string OficinaDeHoja(string sheetName)
    {
        var folded = TextNormalization.Fold(sheetName);
        if (folded.Contains("argentina")) return "AV. ARGENTINA";
        if (folded.Contains("placilla")) return "PLACILLA";
        if (folded.Contains("merc") || folded.Contains("puerto")) return "MERC. PUERTO";
        return sheetName.Trim().ToUpperInvariant();
    }

    private static (int? RowNumber, Dictionary<string, int> Headers) FindHeaderRow(IXLRange used)
    {
        foreach (var row in used.Rows().Take(8))
        {
            var cells = row.Cells().ToList();
            var hasRut = cells.Any(c => TextNormalization.Fold(c.GetString()) == "rut");
            if (!hasRut)
            {
                continue;
            }

            var headers = new Dictionary<string, int>();
            foreach (var cell in cells)
            {
                var key = TextNormalization.Fold(cell.GetString());
                if (key.Length > 0 && !headers.ContainsKey(key))
                {
                    headers[key] = cell.Address.ColumnNumber;
                }
            }

            return (row.RowNumber(), headers);
        }

        return (null, new Dictionary<string, int>());
    }

    private static DateOnly? ReadDate(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var dt))
        {
            return DateOnly.FromDateTime(dt);
        }

        var text = cell.GetString().Trim();
        return DateOnly.TryParse(text, out var parsed) ? parsed : null;
    }
}
