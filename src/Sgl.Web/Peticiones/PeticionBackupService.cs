using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PeticionCambioDomicilio.Data;

public sealed class PeticionBackupService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly PeticionRepository _repository;
    private readonly ILogger<PeticionBackupService> _logger;

    public PeticionBackupService(
        PeticionRepository repository,
        ILogger<PeticionBackupService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Cede el hilo de inmediato: el respaldo (E/S de disco, síncrono) no debe frenar el arranque del host.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var backup = _repository.BackupDiario();
                if (backup is null)
                {
                    _logger.LogWarning("No se pudo crear el respaldo diario de la base local.");
                }
                else
                {
                    _logger.LogInformation("Respaldo diario disponible en {BackupPath}.", backup);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un fallo del respaldo no puede tumbar la aplicación: se registra y se reintenta en la próxima vuelta.
                _logger.LogError(ex, "Falló el respaldo diario de la base local.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
