using Sgl.Domain;

namespace Sgl.Application.Solicitudes;

/// <summary>Open-work figures shown as menu badges of the SOLICITUDES group.</summary>
public sealed record ContadoresSolicitudes(int F8Solicitados, int CertificadosPorFirmar)
{
    public static ContadoresSolicitudes Vacio { get; } = new(0, 0);

    public static ContadoresSolicitudes Desde(
        IReadOnlyDictionary<EstadoSolicitudF8, int> f8,
        IReadOnlyDictionary<EstadoCertificado, int> certificados)
    {
        ArgumentNullException.ThrowIfNull(f8);
        ArgumentNullException.ThrowIfNull(certificados);
        return new ContadoresSolicitudes(
            f8.GetValueOrDefault(EstadoSolicitudF8.Solicitado),
            certificados.GetValueOrDefault(EstadoCertificado.PorFirmar));
    }
}

/// <summary>Spanish labels of the request enums (single place for both pages).</summary>
public static class EtiquetasSolicitudes
{
    public static string Of(EstadoSolicitudF8 estado) => estado switch
    {
        EstadoSolicitudF8.Solicitado => "Solicitado",
        EstadoSolicitudF8.Subido => "Subido",
        EstadoSolicitudF8.NoSeGenero => "No se generó",
        _ => estado.ToString(),
    };

    public static string Of(TipoCertificado tipo) => tipo switch
    {
        TipoCertificado.SinCarpeta => "Sin carpeta",
        TipoCertificado.RegistroCivil => "Registro Civil",
        TipoCertificado.Oficio => "Oficio",
        _ => tipo.ToString(),
    };

    public static string Of(OrigenCertificado origen) => origen switch
    {
        OrigenCertificado.DesdeF8 => "Desde F8",
        OrigenCertificado.Manual => "Manual",
        _ => origen.ToString(),
    };

    public static string Of(EstadoCertificado estado) => estado switch
    {
        EstadoCertificado.PorFirmar => "Por firmar",
        EstadoCertificado.Firmado => "Firmado",
        _ => estado.ToString(),
    };
}

/// <summary>User-facing messages after a request action (trays and modules show the same text).</summary>
public static class MensajesSolicitudes
{
    public static string F8Solicitada(SolicitarF8Resultado resultado, string nombre)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        if (resultado.Creada)
        {
            return $"Se creó la solicitud de F8 para {nombre}.";
        }

        return resultado.Mensaje ?? $"{nombre} ya tiene una solicitud de F8 abierta.";
    }

    public static string NoSeGenero(NoSeGeneroResultado resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var baseMsg = resultado.CertificadoYaExistia
            ? "El F8 quedó como «No se generó»; la solicitud de certificado ya existía."
            : "El F8 quedó como «No se generó» y se abrió una solicitud de certificado.";
        return resultado.CertificadoYaExistia
            ? baseMsg
            : $"{baseMsg} {EstadoCorreo(resultado.EnviadoADirector, resultado.ErrorEnvio)}";
    }

    /// <summary>How the mail to the Director went; the error text is already generic (no internals).</summary>
    public static string EstadoCorreo(bool enviado, string? error) =>
        error is { Length: > 0 }
            ? $"No se pudo avisar al director: {error}"
            : enviado
                ? "Correo enviado al director."
                : "Correo: no enviado (modo prueba / sin correo del Director).";
}
