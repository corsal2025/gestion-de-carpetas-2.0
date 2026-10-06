using Sgl.Domain;

namespace Sgl.Web.Services;

/// <summary>Spanish UI labels for domain enums (siempre en MAYÚSCULAS).</summary>
public static class Labels
{
    public static readonly IReadOnlyList<EstadoCarpeta> OpcionesDesplegable =
    [
        EstadoCarpeta.SubidaConaset,
        EstadoCarpeta.SubidaConF8,
        EstadoCarpeta.CambioDomSubidoConaset,
        EstadoCarpeta.CambioDomSubidoCorreo,
        EstadoCarpeta.SubidaConOficio,
        EstadoCarpeta.SeEncuentraEnArchivos,
        EstadoCarpeta.SeEncuentraEnOf43,
        EstadoCarpeta.CambioDomicilioSolicitado,
        EstadoCarpeta.CambioDomicilio,
        EstadoCarpeta.NoExisteCarpeta,
        EstadoCarpeta.CrearCertificado,
        EstadoCarpeta.CanjeLicExtranjera,
    ];

    public static string Of(Sede sede) => sede switch
    {
        Sede.AvArgentina => "AV. ARGENTINA",
        Sede.Placilla => "PLACILLA",
        Sede.MercadoPuerto => "MERC. PUERTO",
        _ => sede.ToString().ToUpperInvariant(),
    };

    public static string Of(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.SubidaConaset => "SUBIDA A CONASET",
        EstadoCarpeta.SubidaConF8 => "SUBIDA CON F8",
        EstadoCarpeta.CambioDomSubidoConaset => "CAMBIO DOM. SUBIDO A CONASET",
        EstadoCarpeta.CambioDomSubidoCorreo => "CAMBIO DOM. SUBIDO CON CORREO",
        EstadoCarpeta.SubidaConOficio => "SUBIDA CON OFICIO",
        EstadoCarpeta.SeEncuentraEnArchivos => "SE ENCUENTRA EN ARCHIVOS",
        EstadoCarpeta.SeEncuentraEnOf43 => "SE ENCUENTRA EN OF. 43",
        EstadoCarpeta.CambioDomicilioSolicitado => "CAMBIO DE DOMICILIO SOLICITADO",
        EstadoCarpeta.CambioDomicilio => "CAMBIO DE DOMICILIO",
        EstadoCarpeta.NoExisteCarpeta => "NO EXISTE CARPETA",
        EstadoCarpeta.CrearCertificado => "CREAR CERTIFICADO",
        EstadoCarpeta.CanjeLicExtranjera => "CANJE LIC. EXTRANJERA",

        EstadoCarpeta.Citada => "CITADA",
        EstadoCarpeta.Revision => "REVISIÓN",
        EstadoCarpeta.Alertada => "ALERTADA",
        EstadoCarpeta.PrimeraLicencia => "1° LICENCIA",
        EstadoCarpeta.EsperaExamen => "ESPERA EXAMEN",
        EstadoCarpeta.ClasePendiente => "CLASE PENDIENTE",
        EstadoCarpeta.Otorgado => "OTORGADO",
        EstadoCarpeta.ParaDenegar => "PARA DENEGAR",
        EstadoCarpeta.Denegado => "DENEGADO",
        _ => estado.ToString().ToUpperInvariant(),
    };

    public static string Of(Decision decision) => decision switch
    {
        Decision.Pendiente => "PENDIENTE",
        Decision.Otorgado => "OTORGADO",
        Decision.Denegado => "DENEGADO",
        _ => decision.ToString().ToUpperInvariant(),
    };

    public static string EstadoRowCss(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.SubidaConaset => "estado-subidaaconaset",
        EstadoCarpeta.SubidaConF8 => "estado-subidaconf8",
        EstadoCarpeta.CambioDomSubidoConaset => "estado-cambiodomiciliosubidoaconaset",
        EstadoCarpeta.CambioDomSubidoCorreo => "estado-cambiodomiciliosubidoconcorreo",
        EstadoCarpeta.SubidaConOficio => "estado-subidaconoficio",
        EstadoCarpeta.SeEncuentraEnArchivos => "estado-seencuentraenarchivos",
        EstadoCarpeta.SeEncuentraEnOf43 => "estado-seencuentraenoficina43",
        EstadoCarpeta.CambioDomicilioSolicitado => "estado-cambiodomiciliosolicitado",
        EstadoCarpeta.CambioDomicilio => "estado-cambiodomicilio",
        EstadoCarpeta.NoExisteCarpeta => "estado-noexistecarpeta",
        EstadoCarpeta.CrearCertificado => "estado-crearcertificado",
        EstadoCarpeta.CanjeLicExtranjera => "estado-canjelicenciaextranjera",
        _ => $"estado-{estado.ToString().ToLowerInvariant()}",
    };

    public static string Campo(string campo) => campo switch
    {
        "Registro" => "REGISTRO",
        "Estado" => "ESTADO",
        "Decision" => "DECISIÓN",
        "Sede" => "SEDE",
        "FechaCitacion" => "FECHA DE CITACIÓN",
        "FechaSubida" => "FECHA DE SUBIDA",
        "IdoneidadMoral" => "IDONEIDAD MORAL",
        _ => campo.ToUpperInvariant(),
    };

    public static string Css(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.Otorgado => "badge ok",
        EstadoCarpeta.Denegado or EstadoCarpeta.ParaDenegar or EstadoCarpeta.Alertada or EstadoCarpeta.NoExisteCarpeta => "badge bad",
        _ => "badge",
    };

    public static string DecisionCss(Decision decision) => decision switch
    {
        Decision.Otorgado => "badge ok",
        Decision.Denegado => "badge bad",
        _ => "badge muted-badge",
    };

    public static string IdoneidadCss(string? idoneidad)
    {
        if (string.IsNullOrWhiteSpace(idoneidad)) return "";
        if (idoneidad.Contains("ALERTADA", StringComparison.OrdinalIgnoreCase)) return "badge bad";
        if (idoneidad.Contains("REVISAR", StringComparison.OrdinalIgnoreCase)) return "badge warn";
        return "badge info";
    }

    public static string TramiteCss(string? tramite)
    {
        if (string.IsNullOrWhiteSpace(tramite)) return "badge";
        if (tramite.Contains("CONASET", StringComparison.OrdinalIgnoreCase)) return "badge conaset";
        if (tramite.Contains("1°", StringComparison.OrdinalIgnoreCase)) return "badge primera";
        if (tramite.Contains("DOMICILIO", StringComparison.OrdinalIgnoreCase)) return "badge domicilio";
        return "badge";
    }
}
