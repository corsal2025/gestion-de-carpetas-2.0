using System.Reflection;

namespace Sgl.Application.Tests;

/// <summary>
/// Locates the repository root for tests that read repository files: first the path recorded in the assembly at build time,
/// then walking up from the binaries and the working directory looking for <c>Sgl.slnx</c>. Null when neither works.
/// </summary>
internal static class RaizRepositorio
{
    public static string? Buscar()
    {
        var registrada = typeof(RaizRepositorio).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RaizRepositorio")?.Value;
        if (EsRaiz(registrada)) return registrada;

        foreach (var inicio in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = inicio; dir is not null; dir = Path.GetDirectoryName(dir))
            {
                if (EsRaiz(dir)) return dir;
            }
        }
        return null;
    }

    private static bool EsRaiz(string? dir) => dir is not null && File.Exists(Path.Combine(dir, "Sgl.slnx"));
}

/// <summary>A fact that is skipped, with an explicit reason, when the repository root cannot be located.</summary>
public sealed class FactConRepositorioAttribute : FactAttribute
{
    public FactConRepositorioAttribute()
    {
        if (RaizRepositorio.Buscar() is null)
        {
            Skip = "No se encontró la raíz del repositorio (Sgl.slnx): esta prueba lee archivos del repositorio.";
        }
    }
}
