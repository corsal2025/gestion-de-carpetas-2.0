namespace Sgl.Domain;

/// <summary>Work tray a carpeta belongs to, derived from its state (never assigned manually).</summary>
public enum Bandeja
{
    PorPreparar = 1,
    CambioDomicilio = 2,
    EnBusqueda = 3,
    Subidas = 4,
    Cerradas = 5,
    Archivadas = 6,
}
