namespace Sgl.Peticiones;

/// <summary>One entry of the system side menu, as the module's static layout needs it.</summary>
public sealed record ItemMenu(string Nombre, string Href, string ClaseColor);

/// <summary>A work-flow group of the system side menu with its entries.</summary>
public sealed record GrupoMenu(string Clave, string Nombre, string Css, string Icono, IReadOnlyList<ItemMenu> Items);

/// <summary>
/// The system's menu, provided by the host. The module's pages render the same side menu as the rest of the
/// system, so Peticiones, Comunas, Estadísticas and Configuración live inside "Cambio de domicilio".
/// </summary>
public interface IMenuSistema
{
    IReadOnlyList<GrupoMenu> Grupos { get; }
}
