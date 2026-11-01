using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Data;

namespace Sgl.Application.Tests;

/// <summary>Interruptor «Peticion:RespaldoAutomaticoActivo»: apagado, el respaldo automático no copia nada ni crea la carpeta de destino.</summary>
public sealed class PeticionBackupServiceTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "sgl-backup-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch (IOException) { }
    }

    private (PeticionBackupService Servicio, string Destino) Crear(bool activo)
    {
        var destino = Path.Combine(_raiz, "respaldo");
        var repo = new PeticionRepository(Path.Combine(_raiz, "data", "peticiones.db"), destino);
        var servicio = new PeticionBackupService(repo, new AppOptions { RespaldoAutomaticoActivo = activo }, NullLogger<PeticionBackupService>.Instance);
        return (servicio, destino);
    }

    [Fact]
    public void Por_defecto_el_respaldo_automatico_esta_activo()
    {
        Assert.True(new AppOptions().RespaldoAutomaticoActivo);
    }

    [Fact]
    public void Apagado_no_copia_nada_ni_crea_la_carpeta_de_destino()
    {
        var (servicio, destino) = Crear(activo: false);

        var resultado = servicio.RespaldarSiCorresponde();

        Assert.Null(resultado);
        Assert.False(Directory.Exists(destino));
    }

    [Fact]
    public void Activo_conserva_el_comportamiento_actual_y_deja_una_copia()
    {
        var (servicio, destino) = Crear(activo: true);

        var resultado = servicio.RespaldarSiCorresponde();

        Assert.NotNull(resultado);
        Assert.Single(Directory.GetFiles(destino, "peticiones-*.db"));
    }

    [Fact]
    public void La_configuracion_lee_el_interruptor_desde_Peticion_RespaldoAutomaticoActivo()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Peticion:RespaldoAutomaticoActivo"] = "false" })
            .Build();

        var opciones = config.GetSection(AppOptions.SectionName).Get<AppOptions>();

        Assert.False(opciones!.RespaldoAutomaticoActivo);
    }
}
