using System.Text.RegularExpressions;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;

namespace Sgl.Infrastructure.CambioDomicilio;

/// <summary>
/// Approved texts of the answers to the comunas that ask us for a carpeta, and how they are filled in. Markers:
/// {nombre} {rut} {fecha} and the optional {comuna_sgl}: the line that holds it disappears when there is no value.
/// The department edits the stored copy (table <c>PlantillasCorreo</c>); these are the starting texts.
/// </summary>
public static partial class PlantillasCambioDomicilio
{
    public const string ClaveSinRegistro = "SinRegistro";
    public const string ClaveDigital = "Digital";
    public const string ClaveConfirmacion = "ConfirmacionSubida";

    private const string Despedida =
        "Saludos cordiales,\nDepartamento de Licencias de Conducir\nMunicipalidad de Valparaíso";

    private static readonly PlantillaCorreoDto SinRegistro = new(
        ClaveSinRegistro,
        "Respuesta a su solicitud de carpeta: sin registro",
        "Estimados:\n\n" +
        "Junto con saludar, respecto a su solicitud de la carpeta de {nombre}, RUT {rut}, informamos que no registra " +
        "antecedentes en la Municipalidad de Valparaíso. Les solicitamos revisar nuevamente los datos en su base de datos, " +
        "ya que podría corresponder a otra comuna, o tratarse de una primera licencia otorgada por su municipalidad, caso en " +
        "que no procede un cambio de domicilio.\n" +
        "Según nuestra consulta, la persona figura registrada en la comuna de {comuna_sgl}.\n\n" +
        Despedida);

    private static readonly PlantillaCorreoDto Digital = new(
        ClaveDigital,
        "Respuesta a su solicitud de carpeta: carpeta digital",
        "Estimados:\n\n" +
        "Junto con saludar, respecto a su solicitud de la carpeta de {nombre}, RUT {rut}, informamos que su última carpeta " +
        "es de fecha {fecha}, posterior a julio de 2026. Desde esa fecha todas las carpetas se encuentran digitalizadas en SGL.\n\n" +
        Despedida);

    private static readonly PlantillaCorreoDto Confirmacion = new(
        ClaveConfirmacion,
        "Respuesta a su solicitud de carpeta: carpeta subida",
        "Estimados:\n\n" +
        "Junto con saludar, respecto a su solicitud de la carpeta de {nombre}, RUT {rut}, informamos que la carpeta fue subida " +
        "a la plataforma.\n\n" +
        Despedida);

    /// <summary>Template keys that exist.</summary>
    public static IReadOnlyList<string> Claves { get; } = [ClaveSinRegistro, ClaveDigital, ClaveConfirmacion];

    /// <summary>Template key of a mail type, or null when the type has no stored template.</summary>
    public static string? ClaveDe(TipoEnvioCorreoComuna tipo) => tipo switch
    {
        TipoEnvioCorreoComuna.SinRegistro => ClaveSinRegistro,
        TipoEnvioCorreoComuna.Digital => ClaveDigital,
        TipoEnvioCorreoComuna.ConfirmacionSubida => ClaveConfirmacion,
        _ => null,
    };

    public static bool EsClaveValida(string? clave) =>
        Claves.Contains(clave ?? string.Empty, StringComparer.Ordinal);

    /// <summary>Approved starting text of a template.</summary>
    public static PlantillaCorreoDto PorDefecto(string clave) => clave switch
    {
        ClaveSinRegistro => SinRegistro,
        ClaveDigital => Digital,
        ClaveConfirmacion => Confirmacion,
        _ => throw new DomainException($"No existe la plantilla «{clave}»."),
    };

    /// <summary>Fills the markers in one pass (a value that contains a marker is not expanded again).</summary>
    public static TextoCorreo Renderizar(PlantillaCorreoDto plantilla, string nombre, string rut, string? fecha, string? comunaSgl)
    {
        var valores = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nombre"] = nombre.Trim(),
            ["rut"] = rut.Trim(),
            ["fecha"] = (fecha ?? string.Empty).Trim(),
            ["comuna_sgl"] = (comunaSgl ?? string.Empty).Trim(),
        };

        var lineas = plantilla.Cuerpo.Replace("\r\n", "\n").Split('\n')
            .Where(l => !(l.Contains("{comuna_sgl}", StringComparison.Ordinal) && valores["comuna_sgl"].Length == 0));
        var cuerpo = ExcesoDeSaltos().Replace(string.Join("\n", lineas), "\n\n");

        return new TextoCorreo(Marcador().Replace(plantilla.Asunto, m => valores[m.Groups[1].Value]),
            Marcador().Replace(cuerpo, m => valores[m.Groups[1].Value]));
    }

    [GeneratedRegex(@"\{(nombre|rut|fecha|comuna_sgl)\}")]
    private static partial Regex Marcador();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcesoDeSaltos();
}
