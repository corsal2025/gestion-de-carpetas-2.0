namespace Sgl.Domain;

/// <summary>
/// "Pedir": a carpeta in Crear certificado / oficio is requested from Dirección and Secretaría. The request is
/// audited and its date kept; asking again (a reminder) restamps it.
/// </summary>
public sealed partial class Carpeta
{
    /// <summary>When the certificate / oficio was last requested (UTC). Only meaningful in Crear certificado / oficio.</summary>
    public DateTime? FechaPeticion { get; private set; }

    public void RegistrarPeticion(string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (Estado != EstadoCarpeta.CrearCertificado)
        {
            throw new DomainException("Solo se puede pedir una carpeta en «Crear certificado / oficio».");
        }

        Audit(now, usuario, nameof(FechaPeticion), FormatImpresion(FechaPeticion), FormatImpresion(now));
        FechaPeticion = now;
    }

    /// <summary>
    /// Every (re)entry into Crear certificado starts a fresh cycle: a request left over from a previous one would hide the
    /// carpeta from the tray with no open certificate behind it. The earlier request stays in the audit trail.
    /// </summary>
    private void LimpiarPeticionSiEntraACertificado(string usuario, DateTime now)
    {
        if (Estado == EstadoCarpeta.CrearCertificado && FechaPeticion is not null)
        {
            Audit(now, usuario, nameof(FechaPeticion), FormatImpresion(FechaPeticion), null);
            FechaPeticion = null;
        }
    }
}
