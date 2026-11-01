using Sgl.Domain;
using Sgl.Web.Services;

namespace Sgl.Application.Tests;

public sealed class MenuAgrupadoTests
{
    [Fact]
    public void Mantiene_todas_las_bandejas_y_agrupa_sin_ocultar_los_dos_sentidos()
    {
        var modulos = ModulosCatalogo.Todos;
        Assert.Equal(6, ModulosCatalogo.Grupos.Count); // cinco áreas de trabajo y Control
        Assert.All(RevisionEntrada.Submodulos, b => Assert.Contains(modulos, m => m.Bandeja == b && m.Grupo == ModulosCatalogo.Entrada));
        Assert.Contains(modulos, m => m.Clave == ModulosCatalogo.ClaveNosPiden && m.Grupo == ModulosCatalogo.CambioDomicilio);
        Assert.Contains(modulos, m => m.Clave == ModulosCatalogo.ClavePedimos && m.Grupo == ModulosCatalogo.CambioDomicilio);
        Assert.Contains(modulos, m => m.Bandeja == Bandeja.SubidaConaset && m.Grupo == ModulosCatalogo.Cierre);
        Assert.Contains(modulos, m => m.Clave == ModulosCatalogo.ClaveAsistencia && m.Grupo == ModulosCatalogo.Cierre);
        Assert.Contains(modulos, m => m.Href == "respaldos-penultima");
        Assert.Equal(modulos.Count, modulos.Select(m => m.Clave).Distinct().Count());
    }
}
