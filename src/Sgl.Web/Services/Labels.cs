using Sgl.Domain;

namespace Sgl.Web.Services;

/// <summary>Spanish UI labels for domain enums.</summary>
public static class Labels
{
    public static string Of(Sede sede) => sede switch
    {
        Sede.AvArgentina => "Av. Argentina",
        Sede.Placilla => "Placilla",
        Sede.MercadoPuerto => "Merc. Puerto",
        _ => sede.ToString(),
    };

    public static string Of(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.Citada => "Citada",
        EstadoCarpeta.Revision => "Revisión",
        EstadoCarpeta.Alertada => "Alertada",
        EstadoCarpeta.SubidaConaset => "SUBIDA A CONASET",
        EstadoCarpeta.PrimeraLicencia => "1° LICENCIA",
        EstadoCarpeta.CambioDomicilio => "CAMBIO DE DOMICILIO",
        EstadoCarpeta.EsperaExamen => "ESPERA EXAMEN",
        EstadoCarpeta.ClasePendiente => "CLASE PENDIENTE",
        EstadoCarpeta.Otorgado => "OTORGADO",
        EstadoCarpeta.ParaDenegar => "PARA DENEGAR",
        EstadoCarpeta.Denegado => "DENEGADO",
        _ => estado.ToString(),
    };

    public static string Of(Decision decision) => decision switch
    {
        Decision.Pendiente => "Pendiente",
        Decision.Otorgado => "OTORGADO",
        Decision.Denegado => "DENEGADO",
        _ => decision.ToString(),
    };

    public static string EstadoRowCss(EstadoCarpeta estado) => $"estado-{estado.ToString().ToLowerInvariant()}";

    public static string Campo(string campo) => campo switch
    {
        "Registro" => "Registro",
        "Estado" => "Estado",
        "Decision" => "Decisión",
        "Sede" => "Sede",
        "FechaCitacion" => "Fecha de citación",
        "FechaSubida" => "Fecha de subida",
        "IdoneidadMoral" => "Idoneidad moral",
        _ => campo,
    };

    public static string Css(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.Otorgado => "badge ok",
        EstadoCarpeta.Denegado or EstadoCarpeta.ParaDenegar or EstadoCarpeta.Alertada => "badge bad",
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
