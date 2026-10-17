namespace Sgl.Domain;

/// <summary>Work tray a carpeta belongs to, derived from its state (never assigned manually).</summary>
public enum Bandeja
{
    /// <summary>1° Licencia: PrimeraLicencia, Citada and any estado without its own tray.</summary>
    PorPreparar = 1,
    CambioDomicilio = 2,
    /// <summary>Folder not found (NoExisteCarpeta); handled by the external F8 module.</summary>
    SinCarpetaF8 = 3,
    // 4 was the grouped "Subidas" tray, replaced by one tray per subida estado.
    Cerradas = 5,
    Archivadas = 6,
    /// <summary>Agenda-imported carpetas awaiting manual classification.</summary>
    PorClasificar = 7,
    /// <summary>Change of address requested, not yet processed (CambioDomicilioSolicitado).</summary>
    CambioDomicilioSolicitado = 8,
    SubidaConaset = 9,
    SubidaConF8 = 10,
    SubidaConOficio = 11,
    CambioDomSubidoConaset = 12,
    CambioDomSubidoCorreo = 13,
    SeEncuentraEnArchivos = 14,
    SeEncuentraEnOf43 = 15,
    CrearCertificado = 16,
    CanjeLicExtranjera = 17,
    /// <summary>Carpetas left in Citada after the initial classification: no resolution yet (automatic).</summary>
    ParaRevision = 18,
    /// <summary>
    /// Crear certificado carpeta already requested with "Pedir": it lives in Solicitudes · Certificado / Oficio until it is
    /// signed. Derived (never assigned) and not a menu entry; the certificates module is its screen.
    /// </summary>
    EnCertificado = 19,

    // REVISIÓN: Entrada carpetas (Por clasificar or Citada) with a problem. Derived by RevisionEntrada (it needs other
    // rows, e.g. duplicates), never by BandejaExpresion, which still returns PorClasificar / ParaRevision for them.

    /// <summary>More than one open carpeta for the same RUT (same rule as the red duplicate alert).</summary>
    RevisionDuplicadas = 20,
    /// <summary>RUT present but its check digit (or format) is wrong.</summary>
    RevisionRutInvalido = 21,
    /// <summary>No RUT at all.</summary>
    RevisionSinRut = 22,
    /// <summary>Agenda carpeta whose trámite could not be identified (renovación / primera vez).</summary>
    RevisionNoIdentificada = 23,
    /// <summary>Appointment still in the future: the person has not been evaluated yet.</summary>
    RevisionNoEvaluadas = 24,
}
