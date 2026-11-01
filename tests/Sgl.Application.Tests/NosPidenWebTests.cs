using Microsoft.Extensions.Hosting;
using Sgl.Application.SincronizacionCorreo;
using Sgl.Web.Services;

namespace Sgl.Application.Tests;

/// <summary>E13: la sincronización del buzón es solo manual y el menú apunta a las pantallas nuevas.</summary>
public sealed class NosPidenWebTests
{
    [Fact]
    public void Ningun_servicio_en_segundo_plano_sincroniza_el_buzon()
    {
        var asambleas = new[] { typeof(UltimaSincronizacionCorreo).Assembly, typeof(SincronizarCorreoCambioDomicilio).Assembly, typeof(Sgl.Infrastructure.DependencyInjection).Assembly };
        var enSegundoPlano = asambleas.SelectMany(a => a.GetTypes()).Where(t => typeof(IHostedService).IsAssignableFrom(t) && !t.IsAbstract).ToList();

        foreach (var tipo in enSegundoPlano)
        {
            var dependencias = tipo.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
                .Concat(tipo.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).Select(f => f.FieldType));
            Assert.DoesNotContain(dependencias, d => d == typeof(SincronizarCorreoCambioDomicilio) || d == typeof(UltimaSincronizacionCorreo) || d == typeof(IEmailReader));
        }
    }

    [Fact]
    public void La_pagina_Nos_piden_no_usa_temporizadores()
    {
        var carpeta = Path.Combine(RaizRepositorio(), "src", "Sgl.Web", "Components", "Pages", "CambioDomicilio");
        var texto = File.ReadAllText(Path.Combine(carpeta, "NosPiden.razor"));

        Assert.DoesNotContain("PeriodicTimer", texto);
        Assert.DoesNotContain("System.Threading.Timer", texto);
        Assert.DoesNotContain("new Timer", texto);
        Assert.Contains("Solo manual", texto);
    }

    [Fact]
    public void El_menu_lleva_a_las_pantallas_nuevas_y_ya_no_a_la_comunas_vieja()
    {
        Assert.DoesNotContain(ModulosCatalogo.Todos, m => m.Href.Equals("peticiones/Comunas", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ModulosCatalogo.Todos, m => m.Clave == ModulosCatalogo.ClaveComunas && m.Href == "cambio-domicilio/comunas");
        Assert.Contains(ModulosCatalogo.Todos, m => m.Clave == ModulosCatalogo.ClaveNosPiden && m.Href == "cambio-domicilio/nos-piden");
        var carpeta = Path.Combine(RaizRepositorio(), "src", "Sgl.Web", "Components", "Pages", "CambioDomicilio");
        Assert.DoesNotContain(Directory.GetFiles(carpeta, "*.razor"), f => File.ReadAllText(f).Contains("Disponible en la siguiente etapa", StringComparison.Ordinal));
    }

    private static string RaizRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sgl.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
