namespace Sgl.Application.Seguridad;

/// <summary>
/// Port to the host system's permissions. GC2 creates no users or roles: it only asks whether the person using the
/// current session may take a decision. The default implementation reads a list from configuration.
/// </summary>
public interface IPermisos
{
    /// <summary>True when the current user may grant, deny or classify carpetas in «Otorgamiento».</summary>
    bool PuedeOtorgar();
}
