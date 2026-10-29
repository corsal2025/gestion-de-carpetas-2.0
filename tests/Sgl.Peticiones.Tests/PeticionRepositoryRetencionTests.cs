using Microsoft.Data.Sqlite;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Tests;

public sealed class PeticionRepositoryRetencionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public PeticionRepositoryRetencionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* archivo aún tomado */ }
    }

    [Fact]
    public void LosRespaldosAutomaticosNoDesplazanALosManuales()
    {
        var respaldos = Path.Combine(_dir, "respaldo");
        using var repo = new PeticionRepository(Path.Combine(_dir, "peticiones.db"), respaldos);
        repo.AddIfNew(new Peticion { NombreCompleto = "A", Rut = "11111111-1", Comuna = "ARICA" });

        repo.Backup("manual");
        repo.Backup("manual");
        for (var i = 0; i < 32; i++)
        {
            Thread.Sleep(20); // nombres distintos al milisegundo (el temporizador de Windows ronda los 15 ms)
            repo.Backup("automatico");
        }

        var archivos = Directory.GetFiles(respaldos, "peticiones-*.db").Select(Path.GetFileName).ToList();
        Assert.Equal(2, archivos.Count(n => n!.EndsWith("-manual.db")));
        Assert.Equal(30, archivos.Count(n => n!.EndsWith("-automatico.db")));
    }

    [Fact]
    public void LeerMuchasVeces_NoDejaConexionesAbiertasYLaBaseSePuedeBorrar()
    {
        var ruta = Path.Combine(_dir, "peticiones.db");
        var repo = new PeticionRepository(ruta);
        repo.AddIfNew(new Peticion { NombreCompleto = "A", Rut = "11111111-1", Comuna = "ARICA" });
        for (var i = 0; i < 200; i++)
        {
            repo.All();
        }

        repo.Dispose();

        File.Delete(ruta); // en Windows falla si quedó alguna conexión viva
        Assert.False(File.Exists(ruta));
    }
}
