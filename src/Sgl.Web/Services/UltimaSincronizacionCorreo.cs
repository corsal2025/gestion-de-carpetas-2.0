using Sgl.Application.SincronizacionCorreo;

namespace Sgl.Web.Services;

/// <summary>
/// Result of the last "Sincronizar correo" run, shared by every circuit (in memory: lost on restart, which is fine
/// because a new run rebuilds it and the carpetas themselves are persisted). Also serializes runs: two people pressing
/// the button at once never sync twice in parallel.
/// </summary>
public sealed class UltimaSincronizacionCorreo : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ResultadoSincronizacionCorreo? Resultado { get; private set; }

    public bool EnCurso { get; private set; }

    public async Task<ResultadoSincronizacionCorreo> EjecutarAsync(SincronizarCorreoCambioDomicilio caso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caso);
        await _gate.WaitAsync(ct);
        EnCurso = true;
        try
        {
            Resultado = await caso.EjecutarAsync(ct);
            return Resultado;
        }
        finally
        {
            EnCurso = false;
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
