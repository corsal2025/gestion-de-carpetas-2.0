using Sgl.Peticiones;
using Sgl.Web.Services;

namespace Sgl.Web.Peticiones;

/// <summary>The system menu (ModulosCatalogo), handed to the Peticiones pages so they share the side menu.</summary>
public sealed class MenuSistemaCatalogo : IMenuSistema
{
    public IReadOnlyList<GrupoMenu> Grupos { get; } = ModulosCatalogo.Grupos
        .Select(g => new GrupoMenu(
            g.Clave, g.Nombre, g.Css, g.Icono,
            ModulosCatalogo.Todos.Where(m => m.Grupo == g)
                .Select(m => new ItemMenu(m.Nombre, m.Href, ClaseColor(m)))
                .ToList()))
        .ToList();

    private static string ClaseColor(Modulo m) => ModulosCatalogo.ClaseColor(m);
}
