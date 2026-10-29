namespace Sgl.Web.Services;

/// <summary>
/// Reglas de seguridad de los endpoints HTTP sueltos (descarga de certificado y exportación de estadísticas).
/// Son funciones puras para poder probarlas sin levantar el servidor.
/// </summary>
public static class SeguridadWeb
{
    public const int DiasMinimo = 1;
    public const int DiasMaximo = 3650;
    public const int DiasPorDefecto = 30;

    /// <summary>
    /// Sin cabecera Origin la petición es del mismo sitio (navegación directa). Con Origin, su host y puerto
    /// deben ser exactamente los del servidor: "localhost.evil.com" ya no pasa por contener "localhost".
    /// </summary>
    public static bool OrigenPermitido(string? origin, string? hostSolicitud)
    {
        if (string.IsNullOrWhiteSpace(origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/") return false;

        var esperado = (hostSolicitud ?? string.Empty).Trim();
        // Authority omite el puerto por defecto del esquema; el Host de la petición también lo omite.
        return string.Equals(uri.Authority, esperado, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Devuelve el motivo del rechazo, o null si el filtro es válido.</summary>
    public static string? ValidarFiltroEstadisticas(int? dias, DateOnly? desde, DateOnly? hasta)
    {
        if (dias is { } d && (d < DiasMinimo || d > DiasMaximo))
        {
            return $"El parámetro 'dias' debe estar entre {DiasMinimo} y {DiasMaximo}.";
        }

        if (desde is { } a && hasta is { } b && a > b)
        {
            return "La fecha 'desde' no puede ser posterior a 'hasta'.";
        }

        return null;
    }

    /// <summary>Cabeceras de endurecimiento para todas las respuestas.</summary>
    public static IApplicationBuilder UseCabecerasSeguridad(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            await next();
        });
}
