using Sgl.Application.SincronizacionCorreo;

namespace PeticionCambioDomicilio.Comunas;

/// <summary>
/// Sender address → comuna, from the comuna directory ("Comunas y correos"). Exact address first; otherwise the
/// sender's domain, but only when a single comuna uses it (a shared domain such as gmail.com is never guessed).
/// </summary>
public sealed class ComunaRemitente(ComunaDirectory directorio) : IComunaRemitente
{
    public string? ComunaDe(string remitente)
    {
        var correo = (remitente ?? string.Empty).Trim();
        var arroba = correo.LastIndexOf('@');
        if (arroba <= 0 || arroba == correo.Length - 1)
        {
            return null;
        }

        var contactos = directorio.All();
        var exacto = contactos
            .Where(c => string.Equals(c.Email?.Trim(), correo, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Comuna)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (exacto.Count == 1)
        {
            return exacto[0];
        }
        if (exacto.Count > 1)
        {
            return null;
        }

        var dominio = correo[(arroba + 1)..];
        var porDominio = contactos
            .Where(c => string.Equals(Dominio(c), dominio, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Comuna)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return porDominio.Count == 1 ? porDominio[0] : null;
    }

    private static string? Dominio(ComunaContact c)
    {
        if (!string.IsNullOrWhiteSpace(c.Domain))
        {
            return c.Domain.Trim().TrimStart('@');
        }
        var email = c.Email ?? string.Empty;
        var i = email.LastIndexOf('@');
        return i >= 0 ? email[(i + 1)..].Trim() : null;
    }
}
