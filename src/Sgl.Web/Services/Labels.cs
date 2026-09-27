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
        EstadoCarpeta.SubidaConaset => "Subida CONASET",
        EstadoCarpeta.PrimeraLicencia => "Primera licencia",
        EstadoCarpeta.CambioDomicilio => "Cambio de domicilio",
        EstadoCarpeta.EsperaExamen => "Espera examen",
        EstadoCarpeta.ClasePendiente => "Clase pendiente",
        EstadoCarpeta.Otorgado => "Otorgado",
        EstadoCarpeta.ParaDenegar => "Para denegar",
        EstadoCarpeta.Denegado => "Denegado",
        _ => estado.ToString(),
    };

    public static string Of(Decision decision) => decision switch
    {
        Decision.Pendiente => "Pendiente",
        Decision.Otorgado => "Otorgado",
        Decision.Denegado => "Denegado",
        _ => decision.ToString(),
    };

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
}
