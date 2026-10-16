using Microsoft.AspNetCore.Mvc.RazorPages;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Pages;

public sealed class ImprimirModel : PageModel
{
    private readonly PeticionRepository _repository;

    public ImprimirModel(PeticionRepository repository) => _repository = repository;

    public IReadOnlyList<Peticion> Filas { get; private set; } = Array.Empty<Peticion>();
    public string Titulo { get; private set; } = "Peticiones de cambio de domicilio";
    public DateTime Ahora { get; } = DateTime.Now;

    /// <param name="id">Una sola petición. Si no viene, salen varias según los otros filtros.</param>
    /// <param name="marcadas">Solo las marcadas.</param>
    /// <param name="enviadas">Solo las ya enviadas.</param>
    public void OnGet(long? id, bool marcadas, bool enviadas)
    {
        var todas = _repository.All();

        if (id is not null)
        {
            Filas = todas.Where(p => p.Id == id).ToList();
            Titulo = "Petición de cambio de domicilio";
            return;
        }

        var q = todas.AsEnumerable();
        if (marcadas)
        {
            q = q.Where(p => p.Marcada);
            Titulo = "Peticiones marcadas";
        }

        if (enviadas)
        {
            q = q.Where(p => p.Estado == EstadoPeticion.Enviada);
            Titulo = "Peticiones enviadas";
        }

        Filas = q.ToList();
    }
}
