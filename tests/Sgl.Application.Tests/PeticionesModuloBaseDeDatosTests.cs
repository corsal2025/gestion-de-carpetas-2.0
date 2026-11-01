using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;
using Sgl.Web.Peticiones;

namespace Sgl.Application.Tests;

/// <summary>
/// E8 criterio 9: la tabla Peticion vive en la base de la cadena de conexión (la copia que usa un servidor de prueba),
/// nunca en un sgl.db fijo junto al contenido. Ninguna prueba abre un servidor ni toca la base real del repositorio.
/// </summary>
public sealed class PeticionesModuloBaseDeDatosTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"sgl-peticiones-{Guid.NewGuid():N}");

    public PeticionesModuloBaseDeDatosTests() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_carpeta, recursive: true); } catch { /* SQLite puede retener archivos auxiliares un momento */ }
    }

    private static IConfiguration Config(string? cadena) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(cadena is null ? [] : new Dictionary<string, string?> { ["ConnectionStrings:Sgl"] = cadena })
            .Build();

    [Fact]
    public void La_ruta_sale_de_la_cadena_de_conexion_absoluta()
    {
        var copia = Path.Combine(_carpeta, "copia.db");

        var ruta = PeticionesModulo.RutaBaseDeDatos(Config($"Data Source={copia}"), Path.Combine(_carpeta, "contenido"));

        Assert.Equal(copia, ruta);
    }

    [Fact]
    public void Una_ruta_relativa_se_resuelve_contra_el_directorio_de_contenido_y_sin_cadena_queda_sgl_db()
    {
        var contenido = Path.Combine(_carpeta, "contenido");

        Assert.Equal(Path.Combine(contenido, "sgl.db"), PeticionesModulo.RutaBaseDeDatos(Config("Data Source=sgl.db"), contenido));
        Assert.Equal(Path.Combine(contenido, "otra", "x.db"), PeticionesModulo.RutaBaseDeDatos(Config("Data Source=otra/x.db"), contenido));
        Assert.Equal(Path.Combine(contenido, "sgl.db"), PeticionesModulo.RutaBaseDeDatos(Config(null), contenido));
    }

    [Fact]
    public void Con_la_cadena_de_conexion_a_una_copia_la_tabla_Peticion_se_lee_y_escribe_en_la_copia_y_la_base_real_no_cambia()
    {
        var real = BaseRealDelRepositorio();
        var (hashAntes, fechaAntes) = Huella(real);
        var contenido = Path.Combine(_carpeta, "contenido");
        Directory.CreateDirectory(contenido);
        var copia = Path.Combine(_carpeta, "copia.db");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = contenido });
        builder.Configuration["ConnectionStrings:Sgl"] = $"Data Source={copia}";
        builder.Configuration["Peticion:BackupDirectory"] = Path.Combine(_carpeta, "respaldos");
        builder.AddPeticiones();
        using var app = builder.Build();

        var repositorio = app.Services.GetRequiredService<PeticionRepository>();
        repositorio.AddIfNew(new Peticion
        {
            NombreCompleto = "Persona de prueba",
            Rut = "77777777-7",
            Comuna = "Valparaíso",
            CreadaEn = DateTimeOffset.Parse("2026-10-01T12:00:00+00:00"),
        });

        Assert.True(File.Exists(copia));
        Assert.False(File.Exists(Path.Combine(contenido, "sgl.db")));
        Assert.Equal("77777777-7", Assert.Single(repositorio.All()).Rut);
        Assert.Equal(1, FilasPeticion(copia));
        Assert.Equal((hashAntes, fechaAntes), Huella(real));
    }

    private static string? BaseRealDelRepositorio()
    {
        var raiz = RaizRepositorio.Buscar();
        return raiz is null ? null : Path.Combine(raiz, "src", "Sgl.Web", "sgl.db");
    }

    private static (string? Hash, DateTime? Fecha) Huella(string? ruta)
    {
        if (ruta is null || !File.Exists(ruta)) return (null, null);
        using var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return (Convert.ToHexString(SHA256.HashData(flujo)), File.GetLastWriteTimeUtc(ruta));
    }

    private static long FilasPeticion(string ruta)
    {
        using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conexion.Open();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Peticion";
        return (long)cmd.ExecuteScalar()!;
    }
}
