using Sgl.Application.Integridad;

namespace Sgl.Web.Services;

/// <summary>
/// Background integrity watchdog: runs once at startup, after every import (requested through
/// <see cref="IntegridadSolicitudes"/>) and at least once a day. A failing run is logged and recorded, never fatal.
/// </summary>
public sealed class IntegridadHostedService(
    IServiceScopeFactory scopeFactory,
    IntegridadSolicitudes solicitudes,
    ILogger<IntegridadHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);
            await CorrerAsync(OrigenIntegridad.Inicio, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var pedido = await solicitudes.EsperarAsync(Intervalo, stoppingToken);
                await CorrerAsync(pedido ?? OrigenIntegridad.Diaria, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task CorrerAsync(string origen, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var resultado = await scope.ServiceProvider.GetRequiredService<IntegridadService>().EjecutarAsync(origen, ct);
            if (resultado.Corrida.TieneProblemas)
            {
                logger.LogWarning("Vigilante de integridad ({Origen}): {Problemas} problema(s) detectado(s).", origen, resultado.Corrida.Problemas);
            }
            else
            {
                logger.LogInformation("Vigilante de integridad ({Origen}): sin problemas.", origen);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "El vigilante de integridad ({Origen}) no pudo completar la revisión.", origen);
        }
    }
}
