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
        while (!stoppingToken.IsCancellationRequested)
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

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}