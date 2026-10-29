using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PeticionCambioDomicilio.Ews;

/// <summary>
/// Limpieza de texto que viaja a XML (SOAP de EWS, documentos Word): un carácter de control
/// (p. ej. U+0001, pegado desde un Excel o PDF) vuelve inválido el documento y el envío falla entero.
/// </summary>
public static class TextoSeguro
{
    /// <summary>
    /// Quita los caracteres que XML 1.0 no admite: controles salvo tab / salto de línea, U+FFFE, U+FFFF
    /// y surrogados sueltos. Conserva tildes, ñ y los pares surrogados válidos (emoji).
    /// </summary>
    public static string SinControl(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(texto.Length);
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (char.IsHighSurrogate(c) && i + 1 < texto.Length && char.IsLowSurrogate(texto[i + 1]))
            {
                sb.Append(c).Append(texto[++i]);
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

/// <summary>Lectura de respuestas EWS: un cuerpo que no es XML (página de un proxy, error HTML) es un fallo de envío, no un crash.</summary>
public static class EwsXml
{
    public static XDocument Parsear(string xml)
    {
        try
        {
            return XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new EwsSendException("Exchange devolvió una respuesta que no es XML válido (¿proxy o error intermedio?).", ex);
        }
    }
}
