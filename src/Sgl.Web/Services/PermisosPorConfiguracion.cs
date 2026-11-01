using Sgl.Application.Seguridad;

namespace Sgl.Web.Services;

/// <summary>
/// Users allowed to decide in «Otorgamiento», read once from <c>Seguridad:Otorgadores</c> (a list of user names; empty
/// means "everyone", for development only). Stands in until the host system provides the real roles.
/// </summary>
public sealed class OtorgadoresOptions(IEnumerable<string?>? nombres = null)
{
    public const string Seccion = "Seguridad:Otorgadores";

    private int _advertido;

    public IReadOnlyList<string> Nombres { get; } =
        (nombres ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).ToList();

    /// <summary>True the first time it is asked: the "empty list" warning is written once per process.</summary>
    public bool TomarAdvertencia() => Interlocked.Exchange(ref _advertido, 1) == 0;

    /// <summary>Case-insensitive; a Windows login (DOMINIO\usuario) also matches by its user part.</summary>
    public bool Contiene(string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return false;
        var completo = usuario.Trim();
        var barra = completo.LastIndexOf('\\');
        var corto = barra >= 0 ? completo[(barra + 1)..] : completo;
        return Nombres.Any(n => n.Equals(completo, StringComparison.OrdinalIgnoreCase) || n.Equals(corto, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Default <see cref="IPermisos"/>: the current author (typed name or Windows user) must be in <c>Seguridad:Otorgadores</c>.</summary>
public sealed class PermisosPorConfiguracion(OtorgadoresOptions opciones, AutorState autor, ILogger<PermisosPorConfiguracion> logger) : IPermisos
{
    public bool PuedeOtorgar()
    {
        if (opciones.Nombres.Count == 0)
        {
            if (opciones.TomarAdvertencia())
            {
                logger.LogWarning("Seguridad:Otorgadores está vacío: cualquier usuario puede otorgar. Úselo solo en desarrollo.");
            }
            return true;
        }

        return opciones.Contiene(autor.Nombre);
    }
}
