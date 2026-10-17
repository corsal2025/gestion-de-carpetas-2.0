using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Sgl.Application.Solicitudes;
using Sgl.Domain;

namespace Sgl.Infrastructure.Documentos;

/// <summary>
/// Fills the embedded Word templates (see scripts/crear_plantillas_certificados.py) with the data of a certificate.
/// Placeholders are <c>{{NAME}}</c>; Word may split one across runs, so replacement works on the paragraph text.
/// Every required value is checked BEFORE touching the template: a missing one throws a <see cref="DomainException"/>
/// with a message for the user instead of producing a document with holes.
/// </summary>
public sealed partial class GeneradorDocumentoCertificado(TimeProvider clock) : IGeneradorDocumentoCertificado
{
    private const int AnioMinimo = 1900;
    private const int AnioMaximo = 2100;

    private static readonly string[] Meses =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    private static readonly string[] FormatosFecha = ["yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy"];

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();

    public DocumentoGenerado Generar(DatosCertificado datos, Firmantes firmantes)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(firmantes);

        var hoy = HoraChile.Fecha(clock.GetUtcNow().UtcDateTime);
        var (plantilla, valores, archivo) = Preparar(datos, firmantes, hoy);
        return new DocumentoGenerado(Rellenar(plantilla, valores), archivo);
    }

    public string? MotivoNoGenerable(DatosCertificado datos, Firmantes firmantes)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(firmantes);
        try
        {
            Preparar(datos, firmantes, HoraChile.Fecha(clock.GetUtcNow().UtcDateTime));
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
    }

    private static (string Plantilla, Dictionary<string, string> Valores, string Archivo) Preparar(
        DatosCertificado d, Firmantes f, DateOnly hoy)
    {
        var rut = Rut.TryParse(d.Rut, out var r) && r is not null
            ? r
            : throw new DomainException("El RUT de la carpeta no es válido.");
        var nombre = Requerido(d.NombreCompleto, "el nombre del contribuyente").ToUpperInvariant();
        var rutArchivo = rut.Value;

        var v = new Dictionary<string, string>(StringComparer.Ordinal) { ["NOMBRE"] = nombre };
        switch (d.Tipo)
        {
            case TipoCertificado.SinCarpeta:
                v["RUN"] = rut.Formatted;
                v["ANIO_RENOVACION"] = Anio(d.Parametros.GetValueOrDefault(SolicitudCertificado.ClaveAnioRenovacion));
                FirmanteDirector(v, f, mayusculas: false);
                v["SECRETARIA_NOMBRE"] = FirmanteRequerido(f.SecretariaNombre, "nombre de la Secretaria");
                v["SECRETARIA_CARGO"] = FirmanteRequerido(f.SecretariaCargo, "cargo de la Secretaria");
                return ("sin-carpeta", v, $"CERTIFICADO-SIN-CARPETA-{rutArchivo}.docx");

            case TipoCertificado.RegistroCivil:
                v["RUN"] = rut.Formatted;
                v["CLASE"] = Requerido(d.Parametros.GetValueOrDefault("Clase"), "la clase de licencia").ToUpperInvariant();
                v["LEY"] = Requerido(d.Parametros.GetValueOrDefault("Ley"), "la ley");
                v["FECHA_OTORGAMIENTO"] = FechaLarga(
                    ParseFecha(Requerido(d.Parametros.GetValueOrDefault("FechaOtorgamiento"), "la fecha de otorgamiento")));
                v["FECHA_EMISION"] = FechaLarga(hoy);
                FirmanteDirector(v, f, mayusculas: false);
                return ("registro-civil", v, $"CERTIFICADO-REGISTRO-CIVIL-{rutArchivo}.docx");

            case TipoCertificado.Oficio:
                var numero = NumeroOficio(Requerido(d.NumeroOficio, "el número de oficio"), hoy.Year);
                v["RUT"] = rut.Formatted;
                v["NUMERO_OFICIO"] = numero;
                v["FECHA"] = FechaLarga(hoy);
                v["CLASE"] = Requerido(d.Parametros.GetValueOrDefault("Clase"), "la clase de licencia").ToUpperInvariant();
                v["MOTIVO"] = Requerido(d.Parametros.GetValueOrDefault("Motivo"), "el motivo").TrimEnd('.', ' ');
                FirmanteDirector(v, f, mayusculas: true);
                return ("oficio", v, $"OFICIO-{NombreSeguro(numero)}-{rutArchivo}.docx");

            default:
                throw new DomainException("Tipo de certificado inválido.");
        }
    }

    private static void FirmanteDirector(Dictionary<string, string> v, Firmantes f, bool mayusculas)
    {
        v["DIRECTOR_NOMBRE"] = FirmanteRequerido(f.DirectorNombre, "nombre del Director").ToUpperInvariant();
        var cargo = FirmanteRequerido(f.DirectorCargo, "cargo del Director");
        v["DIRECTOR_CARGO"] = mayusculas ? cargo.ToUpperInvariant() : cargo.ToLowerInvariant();
    }

    private static string Limpiar(string? valor) => Regex.Replace(valor ?? string.Empty, @"\s+", " ").Trim();

    private static string Requerido(string? valor, string que)
    {
        var limpio = Limpiar(valor);
        return limpio.Length > 0 ? limpio : throw new DomainException($"Falta {que}.");
    }

    private static string FirmanteRequerido(string? valor, string que)
    {
        var limpio = Limpiar(valor);
        return limpio.Length > 0
            ? limpio
            : throw new DomainException($"Faltan datos en Configuración › Firmantes: {que}.");
    }

    private static string Anio(string? valor)
    {
        var limpio = Limpiar(valor);
        if (limpio.Length == 0)
        {
            throw new DomainException("Falta el año de la última renovación.");
        }

        return int.TryParse(limpio, NumberStyles.None, CultureInfo.InvariantCulture, out var anio) && anio is >= AnioMinimo and <= AnioMaximo
            ? anio.ToString(CultureInfo.InvariantCulture)
            : throw new DomainException($"El año de la última renovación debe estar entre {AnioMinimo} y {AnioMaximo}.");
    }

    private static DateOnly ParseFecha(string valor) =>
        DateOnly.TryParseExact(valor, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha
            : throw new DomainException("La fecha de otorgamiento no es válida (use día-mes-año).");

    private static string FechaLarga(DateOnly f) => $"{f.Day} de {Meses[f.Month - 1]} del {f.Year}";

    /// <summary>"310" becomes "310/2026"; a number that already has a slash is kept as typed.</summary>
    private static string NumeroOficio(string numero, int anio) =>
        numero.All(char.IsAsciiDigit) ? $"{numero}/{anio}" : numero;

    private static string NombreSeguro(string texto) =>
        Regex.Replace(texto, @"[^A-Za-z0-9\-]+", "-").Trim('-');

    private static byte[] Rellenar(string plantilla, Dictionary<string, string> valores)
    {
        var recurso = $"Plantillas.{plantilla}.docx";
        using var origen = typeof(GeneradorDocumentoCertificado).Assembly.GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException($"Falta la plantilla incrustada {recurso}.");
        using var memoria = new MemoryStream();
        origen.CopyTo(memoria);

        using (var doc = WordprocessingDocument.Open(memoria, true))
        {
            var main = doc.MainDocumentPart ?? throw new InvalidOperationException("La plantilla no tiene cuerpo.");
            ReemplazarEn(main.Document ?? throw new InvalidOperationException("La plantilla no tiene cuerpo."), valores);
            foreach (var h in main.HeaderParts)
            {
                if (h.Header is { } header) ReemplazarEn(header, valores);
            }

            foreach (var f in main.FooterParts)
            {
                if (f.Footer is { } footer) ReemplazarEn(footer, valores);
            }

            doc.Save();
        }

        return memoria.ToArray();
    }

    private static void ReemplazarEn(OpenXmlElement raiz, Dictionary<string, string> valores)
    {
        foreach (var parrafo in raiz.Descendants<Paragraph>())
        {
            var textos = parrafo.Descendants<Text>().ToList();
            if (textos.Count == 0)
            {
                continue;
            }

            var completo = string.Concat(textos.Select(t => t.Text));
            // From the end so earlier offsets stay valid; the matches come from the ORIGINAL text, so a value that
            // happens to contain "{{X}}" is never substituted again.
            foreach (var m in Placeholder().Matches(completo).Reverse())
            {
                if (!valores.TryGetValue(m.Groups[1].Value, out var valor))
                {
                    throw new InvalidOperationException($"La plantilla usa un campo desconocido: {m.Value}.");
                }

                ReemplazarRango(textos, m.Index, m.Index + m.Length, valor);
            }
        }
    }

    /// <summary>Puts <paramref name="valor"/> in the first run the range touches and removes the range from the others.</summary>
    private static void ReemplazarRango(List<Text> textos, int inicio, int fin, string valor)
    {
        var pos = 0;
        var primero = true;
        foreach (var t in textos)
        {
            var s = t.Text;
            var lo = pos;
            pos += s.Length;
            var a = Math.Max(inicio, lo);
            var b = Math.Min(fin, pos);
            if (a >= b)
            {
                continue;
            }

            t.Text = string.Concat(s.AsSpan(0, a - lo), primero ? valor : string.Empty, s.AsSpan(b - lo));
            t.Space = SpaceProcessingModeValues.Preserve;
            primero = false;
        }
    }
}
