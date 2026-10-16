using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;

namespace PeticionCambioDomicilio.Pages;

public sealed class ComunasModel : PageModel
{
    private readonly ComunaDirectory _directory;
    private readonly AppOptions _options;

    public ComunasModel(ComunaDirectory directory, AppOptions options)
    {
        _directory = directory;
        _options = options;
    }

    public IReadOnlyList<ComunaContact> Contactos { get; private set; } = Array.Empty<ComunaContact>();

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public string? NuevaComuna { get; set; }

    [BindProperty]
    public string? NuevoCorreo { get; set; }

    public bool ExcelDisponible => !string.IsNullOrWhiteSpace(_options.ExcelPath) && System.IO.File.Exists(_options.ExcelPath);

    public void OnGet() => Cargar();

    public IActionResult OnPostAgregar()
    {
        if (string.IsNullOrWhiteSpace(NuevaComuna) || string.IsNullOrWhiteSpace(NuevoCorreo))
        {
            TempData["Flash"] = "Completá comuna y correo.";
            return RedirectToPage();
        }

        var (_, mensaje) = _directory.AddOrUpdate(NuevaComuna, NuevoCorreo);
        TempData["Flash"] = mensaje;
        return RedirectToPage();
    }

    public IActionResult OnPostEditar(string comuna, string correoViejo, string correoNuevo)
    {
        var (_, m) = _directory.EditarCorreo(comuna ?? "", correoViejo ?? "", correoNuevo ?? "");
        TempData["Flash"] = m;
        return RedirectToPage(new { Q });
    }

    public IActionResult OnPostEliminar(string comuna, string correo)
    {
        var (_, m) = _directory.Eliminar(comuna ?? "", correo ?? "");
        TempData["Flash"] = m;
        return RedirectToPage(new { Q });
    }

    public IActionResult OnPostImportarDesdeExcel()
    {
        if (!ExcelDisponible)
        {
            TempData["Flash"] = "Configura Peticion:ExcelPath antes de importar comunas.";
            return RedirectToPage();
        }

        try
        {
            var r = _directory.ImportFromWorkbook(_options.ExcelPath!);
            TempData["Flash"] = $"Comunas del Excel — leídas: {r.Leidos} · nuevas: {r.Nuevos} · ya estaban: {r.Actualizados}."
                + (r.Avisos.Count > 0 ? " " + string.Join(" | ", r.Avisos) : "");
        }
        catch (Exception ex)
        {
            TempData["Flash"] = $"Error importando comunas: {ex.Message}";
        }

        return RedirectToPage();
    }

    private void Cargar()
    {
        var all = _directory.All();
        Contactos = string.IsNullOrWhiteSpace(Q)
            ? all
            : all.Where(c => c.Comuna.Contains(Q, StringComparison.OrdinalIgnoreCase)
                          || c.Email.Contains(Q, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
