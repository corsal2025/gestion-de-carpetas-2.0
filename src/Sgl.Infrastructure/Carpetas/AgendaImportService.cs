using System.Security.Cryptography;
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
    int CanceladasDeCarpetaExistente = 0)
{
    public int CanceladasAnuladas { get; init; }
    public int CanceladasConTrabajoPendiente { get; init; }
    public IReadOnlyList<int> FilasActivasCanceladas { get; init; } = [];
    public int Actualizadas { get; init; }
    public IReadOnlyList<int> FilasConCambiosPorRevisar { get; init; } = [];
    public IReadOnlyList<int> FilasSinImportarPorRevision { get; init; } = [];
    public IReadOnlyList<int> FilasCanceladasConCarpetaAmbigua { get; init; } = [];
}

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
        int? Toma, int? Email, int? Telefono, int? Clase);

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
        var clavesCita = new HashSet<(Guid Ciudadano, Sede Sede, DateOnly Fecha, TimeOnly? Hora, DateTime? Toma)>();
        var filasSinToma = new HashSet<(string Rut, Sede Sede, DateOnly Fecha, TimeOnly? Hora)>();
        var canceladasPrevias = await db.CitasCanceladas.ToListAsync(ct);
        var canceladasConocidas = canceladasPrevias.Where(c => c.IdentidadAgenda != null).Select(c => c.IdentidadAgenda!).ToHashSet();

        int leidas = 0, creadas = 0, yaExistian = 0, canceladas = 0, ciudadanosCreados = 0, conAlerta = 0, yaExistianAnuladas = 0;
        int canceladasGuardadas = 0, canceladasYaRegistradas = 0, canceladasDeCarpeta = 0, canceladasAnuladas = 0, canceladasConTrabajo = 0;
        var conCarpetaEnEstaCarga = new HashSet<Guid>();
        var rutInvalido = new List<int>();
        var errores = new List<string>();
        var filasPorRevisar = new List<int>();
        var filasSinImportar = new List<int>();
        var canceladasAmbiguas = new List<int>();
        var activasCanceladas = new List<int>();
        var actualizadas = 0;

        bool Cancelada(CeldaAgenda[] fila) => Normalizar(Texto(fila, cols.Estado)).Contains("CANCEL")
            || Normalizar(Texto(fila, cols.Estado)).Contains("ANULAD") || !string.IsNullOrWhiteSpace(Texto(fila, cols.Motivo));
        var ordenadas = Enumerable.Range(filaEncabezado + 1, tabla.Filas.Count - filaEncabezado)
            .OrderBy(n => Cancelada(tabla.Filas[n - 1]) ? 0 : 1).ToArray();
        var canceladasEnArchivo = ordenadas.Where(n => Cancelada(tabla.Filas[n - 1]))
            .Select(n => tabla.Filas[n - 1]).ToList();
        var activasIniciadas = false;
        foreach (var n in ordenadas)
        {
            ct.ThrowIfCancellationRequested();
            var row = tabla.Filas[n - 1];
            if (row.All(c => c.EstaVacia)) continue;
            leidas++;
            // Persist cancellations inside the same transaction before duplicate queries read active appointments.
            if (!Cancelada(row) && !activasIniciadas)
            {
                await db.SaveChangesAsync(ct);
                activasIniciadas = true;
            }
            var claseTexto = Texto(row, cols.Clase);
            var clasesAgenda = LeerClases(claseTexto);
            if (!string.IsNullOrWhiteSpace(claseTexto) && clasesAgenda is null) filasPorRevisar.Add(n);

            var estado = Normalizar(Texto(row, cols.Estado));
            var fecha = LeerFecha(Celda(row, cols.Fecha));
            var sede = MapearSede(Texto(row, cols.Ubicacion));
            var toma = LeerToma(Celda(row, cols.Toma));
            var hora = LeerHora(Celda(row, cols.Hora));
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
                var identidad = IdentidadCancelada(row, cols);
                var yaRegistrada = !canceladasConocidas.Add(identidad);
                if (!yaRegistrada && toma is null)
                {
                    // A later Excel export may omit the booking timestamp carried by CSV.
                    // Match its person fingerprint against the original booking, never by slot alone.
                    var coincidenciasPrevias = canceladasPrevias.Where(x => x.FechaTomaCita is not null
                        && x.FechaCita == cita.FechaCita && x.HoraCita == cita.HoraCita && x.Sede == cita.Sede
                        && x.IdentidadAgenda == IdentidadCancelada(row, cols, x.FechaTomaCita)).Take(2).Count();
                    if (coincidenciasPrevias == 1) yaRegistrada = true;
                }
                if (!yaRegistrada)
                {
                    // Old anonymous statistical rows can be assigned one matching reservation fingerprint.
                    // Do not drop them or infer a link to a citizen/carpeta.
                    var legado = canceladasPrevias.FirstOrDefault(x => x.IdentidadAgenda is null
                        && x.FechaTomaCita == cita.FechaTomaCita && x.FechaCita == cita.FechaCita
                        && x.HoraCita == cita.HoraCita && x.Sede == cita.Sede && x.Tramite == cita.Tramite);
                    if (legado is not null) { legado.VincularIdentidad(identidad); yaRegistrada = true; }
                }
                if (yaRegistrada) canceladasYaRegistradas++;
                else
                {
                    cita.VincularIdentidad(identidad);
                    db.CitasCanceladas.Add(cita);
                    canceladasGuardadas++;
                }
                if (rutValido)
                {
                    var avisadas = await AvisarCarpetaCanceladaAsync(rut!.Value, cita, usuario, now, yaRegistrada, ct);
                    if (avisadas == 1) { canceladasDeCarpeta++; canceladasAnuladas++; }
                    else if (avisadas == -1) { canceladasDeCarpeta++; canceladasConTrabajo++; }
                    else if (avisadas == -2) canceladasConTrabajo++;
                    else if (avisadas > 1) canceladasAmbiguas.Add(n);
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

            // A contradictory snapshot must never recreate the same explicitly cancelled reservation.
            if (canceladasConocidas.Contains(IdentidadCancelada(row, cols)) || canceladasEnArchivo.Any(c => Rut.TryParse(Texto(c, cols.Rut), out var canceladoRut)
                && canceladoRut!.Value == rut!.Value
                && ((toma is not null && LeerToma(Celda(c, cols.Toma)) == toma)
                    || (toma is null && LeerToma(Celda(c, cols.Toma)) is null
                        && LeerFecha(Celda(c, cols.Fecha)) == fecha && MapearSede(Texto(c, cols.Ubicacion)) == sede
                        && LeerHora(Celda(c, cols.Hora)) == hora))))
            {
                activasCanceladas.Add(n);
                continue;
            }

            // The RUT may have been corrected after the first import (it changes for all the citizen's carpetas): the same listing
            // arriving again with the original RUT is the same appointment, not a new person.
            var rutOriginal = rut!.Value;
            var fechaCorrecta = fecha.Value;
            var sedeCorrecta = sede.Value;
            if (toma is null && !filasSinToma.Add((rutOriginal, sedeCorrecta, fechaCorrecta, hora)))
            {
                filasSinImportar.Add(n);
                continue;
            }
            var coincidencias = await db.Carpetas.IgnoreQueryFilters()
                .Include(c => c.Ciudadano)
                .Where(c => c.RutImportacion == rutOriginal || c.Ciudadano.Rut.Value == rutOriginal)
                .Where(MismaCita(toma, sedeCorrecta, fechaCorrecta, hora))
                .OrderBy(c => c.Anulada)
                .Take(2).ToListAsync(ct);
            if (coincidencias.Count > 1)
            {
                filasSinImportar.Add(n);
                continue;
            }
            if (coincidencias.Count == 1)
            {
                var carpetaOriginal = coincidencias[0];
                yaExistian++;
                if (carpetaOriginal.Anulada) yaExistianAnuladas++;
                else RevisarExistente(carpetaOriginal, row, cols, fecha.Value, sede.Value, hora, usuario, now, n, filasPorRevisar, ref actualizadas);
                continue;
            }

            // Sin identificador de reserva, otra hora puede ser una segunda cita o un cambio de la anterior.
            // No se decide automáticamente: se señala la fila para que el funcionario la revise.
            if (toma is null && hora is not null && await db.Carpetas.IgnoreQueryFilters()
                .AnyAsync(c => (c.RutImportacion == rutOriginal || c.Ciudadano.Rut.Value == rutOriginal)
                    && c.FechaTomaCita == null && c.Sede == sedeCorrecta && c.FechaCitacion == fechaCorrecta
                    && c.HoraCita != null && c.HoraCita != hora, ct))
            {
                filasSinImportar.Add(n);
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
            var repetidaEnArchivo = clavesCita.Any(k => k.Ciudadano == ciudadanoId
                && (k.Toma is not null && toma is not null ? k.Toma == toma
                    : k.Sede == sedeValor && k.Fecha == fechaValor && (k.Hora == hora || k.Hora is null || hora is null)));
            clavesCita.Add((ciudadanoId, sedeValor, fechaValor, hora, toma));
            if (repetidaEnArchivo)
            {
                if (toma is null) filasSinImportar.Add(n); else yaExistian++;
                continue;
            }
            var nueva = Carpeta.CrearDesdeAgenda(
                ciudadano, sedeValor, fechaValor, hora,
                MapearTramite(Texto(row, cols.Tramite)), usuario, now, toma, Texto(row, cols.Email), LeerTelefono(Celda(row, cols.Telefono)));
            nueva.CompletarClasesAgenda(clasesAgenda, usuario, now);
            db.Carpetas.Add(nueva);
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
            canceladasGuardadas, canceladasYaRegistradas, canceladasDeCarpeta)
        { FilasActivasCanceladas = activasCanceladas, CanceladasAnuladas = canceladasAnuladas, CanceladasConTrabajoPendiente = canceladasConTrabajo, Actualizadas = actualizadas, FilasConCambiosPorRevisar = filasPorRevisar, FilasSinImportarPorRevision = filasSinImportar, FilasCanceladasConCarpetaAmbigua = canceladasAmbiguas };
    }

    private static void RevisarExistente(Carpeta carpeta, CeldaAgenda[] row, Columnas cols, DateOnly fecha, Sede sede,
        TimeOnly? hora, string usuario, DateTime now, int fila, List<int> revisiones, ref int actualizadas)
    {
        var email = Texto(row, cols.Email)?.Trim();
        var telefono = LeerTelefono(Celda(row, cols.Telefono));
        var tramite = MapearTramite(Texto(row, cols.Tramite));
        var nombre = Texto(row, cols.Nombre);
        var clases = LeerClases(Texto(row, cols.Clase));
        if (clases is not null && carpeta.ClasesAgenda is not null && clases != carpeta.ClasesAgenda) revisiones.Add(fila);
        var actualizada = carpeta.CompletarClasesAgenda(clases, usuario, now);
        if (carpeta.FechaCitacion != fecha || carpeta.Sede != sede
            || (!string.IsNullOrWhiteSpace(nombre) && Normalizar(nombre) != Normalizar(carpeta.Ciudadano.NombreCompleto))
            || (hora is not null && carpeta.HoraCita is not null && carpeta.HoraCita != hora)
            || (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(carpeta.EmailContacto) && !string.Equals(email, carpeta.EmailContacto, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(telefono) && !string.IsNullOrEmpty(carpeta.TelefonoContacto) && !MismoTelefono(telefono, carpeta.TelefonoContacto))
            || (tramite is not null && carpeta.TramiteAgenda is not null && tramite != carpeta.TramiteAgenda))
            revisiones.Add(fila);
        if (carpeta.CompletarDatosAgenda(email, telefono, tramite, usuario, now) || actualizada) actualizadas++;
    }

    /// <summary>
    /// Whether a carpeta is the same appointment as an agenda row: same taking date, or the old key (sede + appointment date)
    /// when either side has no taking date (legacy carpetas imported before that column existed). Two carpetas that both
    /// have a taking date are told apart by it alone. Used by both the "already exists" lookup and the cancellation notice.
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<Carpeta, bool>> MismaCita(DateTime? toma, Sede sede, DateOnly fechaCita, TimeOnly? hora) =>
        c => (toma != null && c.FechaTomaCita == toma)
            || ((toma == null || c.FechaTomaCita == null) && c.Sede == sede && c.FechaCitacion == fechaCita
                && (hora == null || c.HoraCita == null || c.HoraCita == hora));

    /// <summary>Reconciles one exact reservation: preserve work for review, otherwise soft-delete the untouched agenda carpeta.</summary>
    private async Task<int> AvisarCarpetaCanceladaAsync(string rutOriginal, CitaCancelada cita, string usuario, DateTime now, bool yaRegistrada, CancellationToken ct)
    {
        var toma = cita.FechaTomaCita;
        var sede = cita.Sede;
        var fechaCita = cita.FechaCita;
        var carpetas = await db.Carpetas
            .Include(c => c.Historial)
            .Where(c => c.RutImportacion == rutOriginal || c.Ciudadano.Rut.Value == rutOriginal)
            .Where(MismaCita(toma, sede, fechaCita, cita.HoraCita))
            .Take(2).ToListAsync(ct);
        if (carpetas.Count != 1) return carpetas.Count;
        var referencia = $"{cita.FechaCita:yyyy-MM-dd}|{cita.HoraCita:HH\\:mm}|{cita.FechaTomaCita:yyyy-MM-dd HH:mm:ss}";
        if (carpetas[0].Historial.Any(h => h.Campo == "CitaCancelada" && (h.Anterior == referencia || (yaRegistrada && h.Anterior is null))))
        {
            var ultima = carpetas[0].Historial.Where(h => h.Campo == "CitaCancelada").Max(h => h.Id);
            return carpetas[0].Historial.Any(r => r.Campo == "CitaCanceladaResuelta" && r.Id > ultima
                && !carpetas[0].Historial.Any(a => a.Campo == "Anulacion" && a.Nuevo == "Restaurada" && a.Id > r.Id)) ? 0 : -2;
        }
        var carpeta = carpetas[0];
        var sinTrabajo = carpeta.Estado == EstadoCarpeta.PorClasificar && carpeta.Decision == Decision.Pendiente
            && !carpeta.EsExtra && carpeta.RutImportacion is not null
            && carpeta.Historial.All(h => h.Campo == "Agenda" || h.Campo == "ClasesAgenda")
            && !await db.Asistencias.AnyAsync(a => a.CarpetaId == carpeta.Id, ct)
            && !await db.SolicitudesF8.AnyAsync(x => x.CarpetaId == carpeta.Id, ct)
            && !await db.SolicitudesCertificado.AnyAsync(x => x.CarpetaId == carpeta.Id, ct);
        carpeta.RegistrarEvento("CitaCancelada", referencia, sinTrabajo ? "Cancelada en la agenda; carpeta sin trabajo anulada" : "Cancelada en la agenda; revisar trabajo existente", usuario, now);
        if (!sinTrabajo) return -1;
        carpeta.Anular("Cita cancelada en agenda municipal", usuario, now);
        return 1;
    }

    private static string IdentidadCancelada(CeldaAgenda[] row, Columnas cols, DateTime? tomaOriginal = null)
    {
        var persona = Rut.TryParse(Texto(row, cols.Rut), out var rut)
            ? rut!.Value : $"{Normalizar(Texto(row, cols.Rut))}|{Normalizar(Texto(row, cols.Nombre))}";
        var toma = tomaOriginal ?? LeerToma(Celda(row, cols.Toma));
        var clave = toma is not null ? FormattableString.Invariant($"{persona}|{toma:O}")
            : FormattableString.Invariant($"{persona}|{LeerFecha(Celda(row, cols.Fecha)):O}|{LeerHora(Celda(row, cols.Hora)):O}|{MapearSede(Texto(row, cols.Ubicacion))}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave)));
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
                Col("ESTADO"), Col("MOTIVO CANCELACION"), Col("FECHA TOMA CITA"), Col("EMAIL", "CORREO"), Col("TELEFONO", "CELULAR"), Col("CLASE DE LICENCIA", "CLASE LICENCIA", "CLASES LICENCIA", "CLASE", "CLASES"));
            filaEncabezado = r;
            return true;
        }
        return false;
    }

    private static ClaseLicencia? LeerClases(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var partes = texto.Trim().ToUpperInvariant().Split([',', ';', '/', '+', ' ', '|'], StringSplitOptions.RemoveEmptyEntries);
        var resultado = ClaseLicencia.Ninguna;
        foreach (var parte in partes)
        {
            if (!Enum.TryParse<ClaseLicencia>(parte, out var clase) || !ClasesLicencia.Todas.Contains(clase) || parte != clase.ToString()) return null;
            resultado |= clase;
        }
        return resultado == ClaseLicencia.Ninguna ? null : resultado;
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
        if (s.Contains("DOMICILIO")) return TramiteAgenda.CambioDomicilio;
        if (s.Contains("CANJE")) return TramiteAgenda.Canje;
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

    private static string? LeerTelefono(CeldaAgenda celda) =>
        celda.Numero is { } numero && double.IsFinite(numero) && numero >= 0 && numero == Math.Truncate(numero)
            ? numero.ToString("0", CultureInfo.InvariantCulture)
            : string.IsNullOrWhiteSpace(celda.Texto) ? null : celda.Texto.Trim();

    private static bool MismoTelefono(string a, string b)
    {
        // Ignore presentation only; never discard digits or infer a country prefix.
        static string Canonico(string valor) => new(valor.Where(c => !char.IsWhiteSpace(c) && c is not ('+' or '-' or '(' or ')')).ToArray());
        return Canonico(a) == Canonico(b);
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
