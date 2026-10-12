namespace Sgl.Domain;

/// <summary>Work tray a carpeta belongs to, derived from its state (never assigned manually).</summary>
public enum Bandeja
{
    PorPreparar = 1,
    CambioDomicilio = 2,
    /// <summary>Folder not found (NoExisteCarpeta); handled by the external F8 module.</summary>
    SinCarpetaF8 = 3,
    Subidas = 4,
    Cerradas = 5,
    Archivadas = 6,
    /// <summary>Agenda-imported carpetas awaiting manual classification.</summary>
    PorClasificar = 7,
    /// <summary>Change of address requested, not yet processed (CambioDomicilioSolicitado).</summary>
    CambioDomicilioSolicitado = 8,
}
