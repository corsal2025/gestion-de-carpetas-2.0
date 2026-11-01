using Sgl.Domain;

namespace Sgl.Web.Services;

/// <summary>Row rules of the «Por clasificar» classifier, kept outside the page so they can be tested.</summary>
public static class ClasificacionFila
{
    /// <summary>
    /// Destination currently chosen for a row. An explicit pick (even «none») always wins. Otherwise a «1° vez / extensión»
    /// appointment defaults to Primera Licencia; a typed date only takes that default away when it is readable and decides a
    /// physical destination by itself (Archivos / Of. 43). A digital or unreadable date leaves the default in place.
    /// </summary>
    public static EstadoCarpeta? DestinoElegido(bool hayEleccion, EstadoCarpeta? eleccion, TramiteAgenda? tramite, bool fechaTecleada, string? fechaTexto)
    {
        if (hayEleccion) return eleccion;
        if (tramite != TramiteAgenda.PrimeraVezExtension) return null;
        return fechaTecleada && Carpeta.DestinoPorFechaUltimaCarpeta(fechaTexto) is not null ? null : EstadoCarpeta.PrimeraLicencia;
    }
}
