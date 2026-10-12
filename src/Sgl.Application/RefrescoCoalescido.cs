namespace Sgl.Application;

/// <summary>
/// Runs an async refresh at most once at a time. Requests arriving while it runs are coalesced into exactly one
/// more run, and a request arriving right at the end is never lost (re-checked after releasing).
/// </summary>
public sealed class RefrescoCoalescido(Func<Task> refrescar)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile bool _pendiente;

    public async Task SolicitarAsync()
    {
        _pendiente = true;
        while (_pendiente)
        {
            if (!await _lock.WaitAsync(0))
            {
                return; // the running loop will see _pendiente
            }

            try
            {
                while (_pendiente)
                {
                    _pendiente = false;
                    await refrescar();
                }
            }
            finally
            {
                _lock.Release();
            }
            // A request may have set _pendiente between the inner check and Release: loop and retry.
        }
    }
}
