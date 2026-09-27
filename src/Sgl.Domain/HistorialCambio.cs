namespace Sgl.Domain;

/// <summary>Immutable audit entry: who changed which field of a carpeta, when, from what to what.</summary>
public sealed class HistorialCambio
{
    public long Id { get; private set; }

    public Guid CarpetaId { get; private set; }

    public DateTime Fecha { get; private set; }

    public string Usuario { get; private set; } = string.Empty;

    public string Campo { get; private set; } = string.Empty;

    public string? Anterior { get; private set; }

    public string? Nuevo { get; private set; }

    private HistorialCambio()
    {
    }

    internal HistorialCambio(Guid carpetaId, DateTime fecha, string usuario, string campo, string? anterior, string? nuevo)
    {
        CarpetaId = carpetaId;
        Fecha = fecha;
        Usuario = usuario;
        Campo = campo;
        Anterior = anterior;
        Nuevo = nuevo;
    }
}
