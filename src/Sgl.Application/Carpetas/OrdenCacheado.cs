namespace Sgl.Application.Carpetas;

/// <summary>
/// Ordered ids of a big tray, loaded once so every virtualized window is read by primary key (no OFFSET scans).
/// The cache is only valid while nobody else changed the data: it carries the data version it was built from, is
/// re-validated against a cheap version stamp at most once per interval, and is dropped as soon as a requested
/// window comes back short (a row moved out of the tray, was annulled, etc.). Not thread-safe by itself: it lives
/// in one circuit and is touched from its dispatcher.
/// </summary>
public sealed class OrdenCacheado(TimeSpan? intervaloVerificacion = null)
{
    public static readonly TimeSpan IntervaloPorDefecto = TimeSpan.FromSeconds(3);

    private readonly TimeSpan _intervalo = intervaloVerificacion ?? IntervaloPorDefecto;
    private DateTimeOffset _ultimaVerificacion;
    private bool _enCarga;
    private int _generacion;

    public IReadOnlyList<Guid>? Ids { get; private set; }

    public long Version { get; private set; }

    public bool Tiene => Ids is not null;

    /// <summary>Marks the start of a load; false when ids are already cached or a load is in flight.</summary>
    public bool IniciarCarga(out int generacion)
    {
        generacion = _generacion;
        if (_enCarga || Ids is not null)
        {
            return false;
        }

        _enCarga = true;
        return true;
    }

    /// <summary>Stores a finished load; ignored (false) when the cache was invalidated while it ran.</summary>
    public bool Completar(int generacion, IReadOnlyList<Guid> ids, long version, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (generacion != _generacion)
        {
            return false;
        }

        Ids = ids;
        Version = version;
        _ultimaVerificacion = ahora;
        _enCarga = false;
        return true;
    }

    public void CargaFallida(int generacion)
    {
        if (generacion == _generacion)
        {
            _enCarga = false;
        }
    }

    public void Invalidar()
    {
        Ids = null;
        _generacion++;
        _enCarga = false;
    }

    /// <summary>True when the cached ids are old enough that the data version must be compared again.</summary>
    public bool DebeVerificar(DateTimeOffset ahora) => Ids is not null && ahora - _ultimaVerificacion >= _intervalo;

    /// <summary>Compares the cached version with the current one; invalidates (false) when they differ.</summary>
    public bool Verificar(long versionActual, DateTimeOffset ahora)
    {
        if (Ids is null)
        {
            return false;
        }

        if (versionActual != Version)
        {
            Invalidar();
            return false;
        }

        _ultimaVerificacion = ahora;
        return true;
    }

    /// <summary>A window read by id must return every id asked for; a short window means the order is stale.</summary>
    public bool ValidarVentana(int esperadas, int recibidas)
    {
        if (esperadas == recibidas)
        {
            return true;
        }

        Invalidar();
        return false;
    }
}
