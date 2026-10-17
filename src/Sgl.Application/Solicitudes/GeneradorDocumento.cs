using Sgl.Domain;

namespace Sgl.Application.Solicitudes;

/// <summary>What a certificate / oficio document needs: the citizen, the tipo, the oficio number and the parameters.</summary>
public sealed record DatosCertificado(
    TipoCertificado Tipo,
    string Rut,
    string NombreCompleto,
    string? NumeroOficio,
    IReadOnlyDictionary<string, string> Parametros)
{
    public static DatosCertificado Desde(SolicitudCertificadoDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new(dto.Tipo, dto.Rut, dto.NombreCompleto, dto.NumeroOficio, dto.Parametros);
    }

    public static DatosCertificado Desde(NotificacionCertificado notificacion)
    {
        ArgumentNullException.ThrowIfNull(notificacion);
        return new(notificacion.Tipo, notificacion.Rut, notificacion.NombreCompleto, notificacion.NumeroOficio, notificacion.Parametros);
    }
}

/// <summary>A generated .docx and the file name to offer for it.</summary>
public sealed record DocumentoGenerado(byte[] Contenido, string NombreArchivo);

/// <summary>
/// Port that fills the Word template of a certificate / oficio. Throws <see cref="DomainException"/> with a message
/// meant for the user when a required value (a parameter or a signer) is missing, instead of producing a broken document.
/// </summary>
public interface IGeneradorDocumentoCertificado
{
    DocumentoGenerado Generar(DatosCertificado datos, Firmantes firmantes);

    /// <summary>Why the document cannot be generated yet (the text <see cref="Generar"/> would throw), or null when it can.</summary>
    string? MotivoNoGenerable(DatosCertificado datos, Firmantes firmantes);
}

public static class GeneradorDocumentoCertificadoExtensions
{
    public static DocumentoGenerado Generar(this IGeneradorDocumentoCertificado generador, SolicitudCertificadoDto dto, Firmantes firmantes) =>
        generador.Generar(DatosCertificado.Desde(dto), firmantes);
}
