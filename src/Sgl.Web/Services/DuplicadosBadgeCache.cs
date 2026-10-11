using Sgl.Application.Carpetas;

namespace Sgl.Web.Services;

/// <summary>
/// App-wide short-lived cache of the pending duplicates count, so navigating between pages does not
/// re-run the aggregate query. Each refresh uses its own DI scope (own DbContext).
/// </summary>
public sealed class DuplicadosBadgeCache(IServiceScopeFactory scopeFactory, TimeProvider clock)
{
    public static readonly TimeSpan Duracion = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _valor;
    private DateTimeOffset _expira = DateTimeOffset.MinValue;

    public void Invalidar() => _expira = DateTimeOffset.MinValue;

    public async Task<int> ObtenerAsync(CancellationToken ct = default)
    {
        if (clock.GetUtcNow() < _expira)
        {
            return _valor;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (clock.GetUtcNow() >= _expira)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                _valor = await scope.ServiceProvider.GetRequiredService<DuplicadosService>().ContarPendientesAsync(ct);
                _expira = clock.GetUtcNow() + Duracion;
            }

            return _valor;
        }
        finally
        {
            _lock.Release();
        }
    }
}
