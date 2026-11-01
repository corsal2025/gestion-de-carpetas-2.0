using PeticionCambioDomicilio.Comunas;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;

namespace Sgl.Infrastructure.CambioDomicilio;

/// <summary>The comuna directory of the Peticiones module (CSV) seen through the application port.</summary>
public sealed class DirectorioComunasAdapter(ComunaDirectory directorio) : IDirectorioComunas
{
    public IReadOnlyList<string> CorreosDe(string? comuna) => directorio.EmailsFor(NombreDe(comuna));

    public string NombreDe(string? comuna)
    {
        var texto = (comuna ?? string.Empty).Trim();
        return texto.Length == 0 ? string.Empty : directorio.ResolveComunaName(texto) ?? texto;
    }

    public (bool Ok, string Mensaje) AgregarCorreo(string comuna, string correo) => directorio.AddOrUpdate(comuna, correo);

    public IReadOnlyList<ContactoComunaDto> Listar() =>
        directorio.All()
            .Select(c => new ContactoComunaDto(c.Comuna, c.Email, c.Domain))
            .OrderBy(c => c.Comuna, StringComparer.CurrentCulture)
            .ThenBy(c => c.Correo, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public (bool Ok, string Mensaje) CrearComuna(string comuna, string correo)
    {
        var existente = NombreDe(comuna);
        if (existente.Length > 0 && directorio.EmailsFor(existente).Count > 0)
        {
            return (false, $"{existente} ya tiene un correo: edítelo en la lista.");
        }

        return directorio.AddOrUpdate(comuna, correo);
    }

    public (bool Ok, string Mensaje) EditarCorreo(string comuna, string correoViejo, string correoNuevo) =>
        directorio.EditarCorreo(comuna, correoViejo, correoNuevo);

    public (bool Ok, string Mensaje) Eliminar(string comuna, string correo) => directorio.Eliminar(comuna, correo);

    public IReadOnlyList<string> ComunasSinCorreo() =>
        ComunasChile.Todas.Where(c => directorio.EmailsFor(NombreDe(c)).Count == 0).ToList();
}
