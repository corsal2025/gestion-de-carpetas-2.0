using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Excel;
using PeticionCambioDomicilio.Ews;

namespace PeticionCambioDomicilio.Pages;

public sealed class IndexModel : PageModel
{
    /// <summary>Pausa entre comunas en el envío masivo, para no gatillar el antispam de Exchange.</summary>
    private static readonly TimeSpan PausaEntreComunas = TimeSpan.FromSeconds(2);

    private readonly PeticionRepository _repository;
    private readonly ExcelPeticionImporter _importer;
    private readonly PeticionSender _sender;
    private readonly ComunaDirectory _directory;
    private readonly IMailSender _mail;
    private readonly AppOptions _options;

    public IndexModel(
        PeticionRepository repository,
        ExcelPeticionImporter importer,
        PeticionSender sender,
        ComunaDirectory directory,
        IMailSender mail,
        AppOptions options)
    {
        _repository = repository;
        _importer = importer;
        _sender = sender;
        _directory = directory;
        _mail = mail;
        _options = options;
    }

    public IReadOnlyList<Peticion> Peticiones { get; private set; } = Array.Empty<Peticion>();
    public bool MailListo => _mail.IsConfigured;
    public bool ExcelConfigurado => !string.IsNullOrWhiteSpace(_options.ExcelPath) && System.IO.File.Exists(_options.ExcelPath);
    public string? ExcelPath => _options.ExcelPath;
    public int ComunasEnDirectorio => _directory.Count;
    public string? ModoPruebaEmail => string.IsNullOrWhiteSpace(_options.TestModeEmail) ? null : _options.TestModeEmail;

    /// <summary>Comunas con peticiones pendientes y sin correo en el directorio — hay que cargarlas.</summary>
    public IReadOnlyList<string> ComunasSinCorreo { get; private set; } = Array.Empty<string>();

    /// <summary>Peticiones pendientes (no enviadas) por comuna, para etiquetar el botón "Enviar comuna (N)".</summary>
    public IReadOnlyDictionary<string, int> PendientesPorComuna { get; private set; } =
        new Dictionary<string, int>();

    /// <summary>Marcadas (columna ✓) que todavía no se enviaron — para el botón "Enviar marcadas (N)".</summary>
    public int MarcadasPendientes { get; private set; }

    /// <summary>Todas las filas marcadas (enviadas o no) — para el botón "Borrar selección (N)".</summary>
    public int MarcadasTotal { get; private set; }

    /// <summary>Marcadas no finalizadas (pendientes o enviadas) — para "Reenviar marcadas (N)".</summary>
    public int MarcadasReenviables { get; private set; }

    /// <summary>Comunas del directorio, para el desplegable de carga manual.</summary>
    public IReadOnlyList<string> ComunaNombres { get; private set; } = Array.Empty<string>();

    private static bool EsPendiente(Peticion p) =>
        p.Estado is EstadoPeticion.Borrador or EstadoPeticion.SinCorreoComuna or EstadoPeticion.Error;

    /// <summary>Caso terminado: carpeta subida (a CONASET o con correo). No se puede seleccionar.</summary>
    public static bool EsFinalizada(Peticion p) =>
        p.SubidaEn is not null || EstadoCarpetaCatalog.EsFinalizado(p.EstadoCarpeta);

    public void OnGet()
    {
        Peticiones = _repository.All();

        // Marcas sobre casos ya terminados (carpeta subida): se limpian.
        var marcasObsoletas = Peticiones.Where(p => p.Marcada && EsFinalizada(p)).Select(p => p.Id).ToList();
        if (marcasObsoletas.Count > 0)
        {
            _repository.DesmarcarIds(marcasObsoletas);
            Peticiones = _repository.All();
        }

        ComunasSinCorreo = _sender.ComunasPendientesSinCorreo();
        PendientesPorComuna = Peticiones
            .Where(EsPendiente)
            .GroupBy(p => p.Comuna, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        MarcadasPendientes = Peticiones.Count(p => p.Marcada && EsPendiente(p));
        MarcadasTotal = Peticiones.Count(p => p.Marcada);
        MarcadasReenviables = Peticiones.Count(p => p.Marcada && !EsFinalizada(p));
        ComunaNombres = _directory.ComunaNames();
    }

    public IActionResult OnPostCrearRespaldo()
    {
        var backup = _repository.Backup("manual");
        TempData["Flash"] = backup is null
            ? "No se pudo crear el respaldo local. Revisa que la carpeta de respaldo tenga espacio y permisos de escritura."
            : $"Respaldo local creado: {Path.GetFileName(backup)}";
        return RedirectToPage();
    }

    public IActionResult OnPostImportar()
    {
        if (!ExcelConfigurado)
        {
            TempData["Flash"] = "Configura la ruta del Excel en Configuración antes de importar.";
            return RedirectToPage();
        }

        _repository.Backup("import");

        try
        {
            var result = _importer.Import(
                _options.ExcelPath!,
                _repository.AddIfNew,
                _repository.All(),
                _repository.Delete,
                _repository.SincronizarCarpetaDesdeExcel);
            var msg = $"Hojas: {result.HojasLeidas} · Filas: {result.FilasLeidas} · " +
                      $"Cambio de domicilio: {result.FilasCambioDomicilio} · Nuevas: {result.Nuevas} · " +
                      $"Actualizadas: {result.Duplicadas} · Carpetas sincronizadas: {result.CarpetasSincronizadas} · " +
                      $"Quitadas (ya no están en el Excel): {result.Obsoletas} · " +
                      $"RUT inválidos: {result.RutInvalidos} · Comuna no reconocida: {result.ComunaNoReconocida}";
            if (result.Avisos.Count > 0)
            {
                msg += $" · Avisos ({result.Avisos.Count}): " + string.Join(" | ", result.Avisos.Take(8));
            }

            TempData["Flash"] = msg;
        }
        catch (Exception ex)
        {
            TempData["Flash"] = $"Error leyendo el Excel: {ex.Message}";
        }

        return RedirectToPage();
    }

    /// <summary>Envía en UN correo todas las pendientes de la comuna de esa fila.</summary>
    public async Task<IActionResult> OnPostEnviarComuna(long id)
    {
        var p = _repository.Get(id);
        if (p is null)
        {
            TempData["Flash"] = "La petición ya no existe.";
            return RedirectToPage();
        }

        var result = await _sender.SendComunaAsync(p.Comuna, HttpContext.RequestAborted);
        TempData["Flash"] = result.Mensaje;
        return RedirectToPage();
    }

    /// <summary>
    /// Envía SOLO las peticiones marcadas (columna ✓) que estén pendientes. Sigue la regla de
    /// siempre: UN correo por comuna, con las marcadas de esa comuna. Las que ya están enviadas
    /// nunca se reenvían.
    /// </summary>
    public async Task<IActionResult> OnPostEnviarMarcadas()
    {
        var marcadasPendientes = _repository.All()
            .Where(p => p.Marcada && EsPendiente(p))
            .ToList();

        if (marcadasPendientes.Count == 0)
        {
            TempData["Flash"] = "No hay peticiones marcadas pendientes de envío. Marcá las filas con la casilla ✓.";
            return RedirectToPage();
        }

        var ids = marcadasPendientes.Select(p => p.Id).ToHashSet();
        var comunas = marcadasPendientes
            .Select(p => p.Comuna)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCulture)
            .ToList();

        int correos = 0, personas = 0, sinCorreo = 0, conError = 0;
        var primera = true;
        foreach (var comuna in comunas)
        {
            if (!primera)
            {
                await Task.Delay(PausaEntreComunas, HttpContext.RequestAborted);
            }

            primera = false;
            var result = await _sender.SendComunaAsync(comuna, HttpContext.RequestAborted, ids);
            switch (result.Estado)
            {
                case EstadoPeticion.Enviada:
                    correos++;
                    personas += result.Personas;
                    break;
                case EstadoPeticion.SinCorreoComuna:
                    sinCorreo++;
                    break;
                default:
                    conError++;
                    break;
            }
        }

        TempData["Flash"] = $"Marcadas enviadas — Correos: {correos} ({personas} persona/s) · " +
                            $"Comunas sin correo: {sinCorreo} · Con error: {conError}.";
        return RedirectToPage();
    }

    /// <summary>
    /// Reenvía una copia de las marcadas (no terminadas) al correo que elija el operador.
    /// UN correo por comuna. No cambia estados ni toca a las municipalidades.
    /// </summary>
    public async Task<IActionResult> OnPostReenviarMarcadas(string destinatario)
    {
        var ids = _repository.All()
            .Where(p => p.Marcada && !EsFinalizada(p))
            .Select(p => p.Id)
            .ToHashSet();

        if (ids.Count == 0)
        {
            TempData["Flash"] = "No hay peticiones marcadas para reenviar. Marcá las filas con la casilla ✓.";
            return RedirectToPage();
        }

        var result = await _sender.ReenviarAsync(ids, destinatario, HttpContext.RequestAborted);
        TempData["Flash"] = result.Mensaje;
        return RedirectToPage();
    }

    /// <summary>
    /// Marca como enviadas las peticiones marcadas sin disparar correo externo a la comuna.
    /// Útil para cerrar filas ya tratadas localmente sin mandar mails a otras municipalidades.
    /// </summary>
    public IActionResult OnPostMarcarMarcadasComoEnviadasSinCorreo()
    {
        var marcadasPendientes = _repository.All()
            .Where(p => p.Marcada && EsPendiente(p))
            .ToList();

        if (marcadasPendientes.Count == 0)
        {
            TempData["Flash"] = "No hay peticiones marcadas pendientes para registrar como enviadas sin correo.";
            return RedirectToPage();
        }

        var count = _repository.MarcarMarcadasComoEnviadasSinCorreo(DateTimeOffset.Now);
        TempData["Flash"] = $"Se marcaron {count} petición(es) como enviadas sin enviar correo a la comuna.";
        return RedirectToPage();
    }

    /// <summary>
    /// Marca todas las peticiones pendientes como ya subidas en el tablero, sin despachar correo.
    /// Se usa para dejar la vista general como si todo el lote estuviera ya resuelto.
    /// </summary>
    public IActionResult OnPostMarcarTodoComoEnviadasSinCorreo()
    {
        var pendientes = _repository.All()
            .Where(EsPendiente)
            .ToList();

        if (pendientes.Count == 0)
        {
            TempData["Flash"] = "No hay peticiones pendientes para dejar como ya subidas.";
            return RedirectToPage();
        }

        var count = _repository.MarcarTodasComoEnviadasSinCorreo(DateTimeOffset.Now);
        TempData["Flash"] = $"Se dejaron {count} petición(es) como ya subidas sin enviar correo a la comuna.";
        return RedirectToPage();
    }

    /// <summary>
    /// Busca en TODO el libro (por RUT, o por nombre si el RUT no valida) el estado de la carpeta
    /// de cada petición y lo sincroniza si en el Excel figura más avanzado. No importa filas nuevas.
    /// </summary>
    public IActionResult OnPostActualizarCarpetas()
    {
        if (!ExcelConfigurado)
        {
            TempData["Flash"] = "Configura la ruta del Excel en Configuración antes de actualizar.";
            return RedirectToPage();
        }

        _repository.Backup("actualizar-carpetas");

        try
        {
            var n = _importer.ActualizarEstadosCarpeta(
                _options.ExcelPath!, _repository.All(), _repository.SincronizarCarpetaDesdeExcel);
            TempData["Flash"] = n > 0
                ? $"{n} carpeta(s) actualizada(s) desde el Excel."
                : "Ninguna carpeta cambió — en el Excel están igual o menos avanzadas que acá.";
        }
        catch (Exception ex)
        {
            TempData["Flash"] = $"Error leyendo el Excel: {ex.Message}";
        }

        return RedirectToPage();
    }

    public IActionResult OnPostEstadoCarpeta(long id, string estado)
    {
        if (Domain.EstadoCarpetaCatalog.Valores.Contains(estado))
        {
            _repository.SetEstadoCarpeta(id, estado);
            if (Domain.EstadoCarpetaCatalog.EsFinalizado(estado))
            {
                _repository.DesmarcarIds(new[] { id });
            }
        }

        if (Request.Headers["X-Requested-With"] == "fetch")
        {
            return new JsonResult(new { ok = true, finalizado = Domain.EstadoCarpetaCatalog.EsFinalizado(estado) });
        }

        return RedirectToPage();
    }

    public IActionResult OnPostMarcar(long id)
    {
        // Se seleccionan los casos no terminados (pendientes o enviadas); los de carpeta subida no.
        var peticion = _repository.Get(id);
        var marcada = peticion is not null && !EsFinalizada(peticion) && _repository.ToggleMarcada(id);

        // Si vino por fetch (JS), responder JSON y no recargar la pagina.
        if (Request.Headers["X-Requested-With"] == "fetch")
        {
            return new JsonResult(new { marcada });
        }

        return RedirectToPage();
    }

    /// <summary>Borrar selección: elimina las peticiones marcadas. La confirmacion la hace la pantalla. Respalda antes.</summary>
    public IActionResult OnPostBorrarSeleccion()
    {
        var backup = _repository.Backup("borrarseleccion");
        var n = _repository.DeleteMarcadas();
        TempData["Flash"] = backup is not null
            ? $"Se borraron {n} petición(es) seleccionada(s). Respaldo guardado en {backup}."
            : $"Se borraron {n} petición(es) seleccionada(s). ATENCIÓN: no se pudo guardar respaldo.";
        return RedirectToPage();
    }

    /// <summary>Borra TODAS las peticiones. La confirmacion la hace la pantalla. Respalda antes.</summary>
    public IActionResult OnPostBorrarTodo()
    {
        var backup = _repository.Backup("borrartodo");
        var n = _repository.DeleteAll();
        TempData["Flash"] = backup is not null
            ? $"Se borraron {n} petición(es). Respaldo guardado en {backup}. Reimportá del Excel cuando quieras."
            : $"Se borraron {n} petición(es). ATENCIÓN: no se pudo guardar respaldo. El historial de envío se perdió.";
        return RedirectToPage();
    }

    /// <summary>
    /// Carga a mano una petición que el Excel no trae (caso que no se detecta de forma
    /// automática). Misma clave de deduplicación que la importación: RUT + comuna — si ya
    /// existe, se actualiza en vez de duplicar.
    /// </summary>
    public async Task<IActionResult> OnPostAgregarManual(string nombre, string rut, string comuna)
    {
        nombre = (nombre ?? "").Trim();
        rut = (rut ?? "").Trim();
        comuna = (comuna ?? "").Trim();

        if (nombre.Length == 0 || rut.Length == 0 || comuna.Length == 0)
        {
            TempData["Flash"] = "Para agregar a mano hacen falta nombre, RUT y comuna.";
            return RedirectToPage();
        }

        var comunaCanonica = _directory.ResolveComunaName(comuna) ?? comuna.ToUpperInvariant();
        var rutNormalizado = RutValidator.NormalizeAndValidate(rut);
        var rutInvalido = rutNormalizado is null;

        var orden = _repository.All().Select(p => p.OrdenImportacion).DefaultIfEmpty(0).Max() + 1;

        var peticion = new Peticion
        {
            NombreCompleto = nombre,
            Rut = rutNormalizado ?? rut,
            Comuna = comunaCanonica,
            Origen = "Ingreso manual",
            Oficina = Peticion.OficinaManual,
            OrdenImportacion = orden,
            RutInvalido = rutInvalido,
            Estado = EstadoPeticion.Borrador,
        };

        var peticionId = _repository.UpsertManual(peticion);
        var avisoRut = rutInvalido ? " ⚠ RUT no validado, revisar." : "";

        TempData["Flash"] = $"Petición agregada a la lista de pendientes: {nombre} ({peticion.Rut}) → {comunaCanonica}. Lista para enviar.{avisoRut}";

        return RedirectToPage();
    }

    public IActionResult OnPostEliminar(long id)
    {
        _repository.Delete(id);
        TempData["Flash"] = "Petición eliminada.";
        return RedirectToPage();
    }
}
