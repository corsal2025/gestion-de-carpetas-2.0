namespace Sgl.Domain;

/// <summary>Persisted outcome of one bulk Excel import (counts only, no personal data).</summary>
public sealed class ImportacionLog
{
    public const int MaxUsuarioLength = Carpeta.MaxUsuarioLength;

    public long Id { get; private set; }

    public DateTime Fecha { get; private set; }

    public string Usuario { get; private set; } = string.Empty;

    public int FilasLeidas { get; private set; }

    public int CarpetasCreadas { get; private set; }

    public int CarpetasActualizadas { get; private set; }

    public int Errores { get; private set; }

    private ImportacionLog()
    {
    }

    public ImportacionLog(DateTime fecha, string usuario, int filasLeidas, int creadas, int actualizadas, int errores)
    {
        Fecha = fecha;
        Usuario = string.IsNullOrWhiteSpace(usuario) ? "Desconocido" : usuario.Trim()[..Math.Min(usuario.Trim().Length, MaxUsuarioLength)];
        FilasLeidas = Math.Max(0, filasLeidas);
        CarpetasCreadas = Math.Max(0, creadas);
        CarpetasActualizadas = Math.Max(0, actualizadas);
        Errores = Math.Max(0, errores);
    }
}
