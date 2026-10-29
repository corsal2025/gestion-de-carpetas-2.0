namespace Sgl.Application;

/// <summary>
/// One gate per circuit (register it Scoped). A Blazor circuit shares a single DbContext among all its services, and a
/// DbContext is not thread-safe: every use case that reads or writes through it takes this gate, so two operations
/// never overlap and a <c>ChangeTracker.Clear()</c> after a failure never runs in the middle of another SaveChanges.
/// The gate is NOT reentrant: a gated method must never call another gated method of the same circuit.
/// </summary>
public sealed class UnidadDeTrabajoGate
{
    private readonly SemaphoreSlim _semaforo = new(1, 1);

    public Task WaitAsync(CancellationToken ct = default) => _semaforo.WaitAsync(ct);

    public void Release() => _semaforo.Release();

    /// <summary>Runs <paramref name="lectura"/> holding the gate.</summary>
    public async Task<T> LeerAsync<T>(Func<Task<T>> lectura, CancellationToken ct = default)
    {
        await _semaforo.WaitAsync(ct);
        try
        {
            return await lectura();
        }
        finally
        {
            _semaforo.Release();
        }
    }
}
