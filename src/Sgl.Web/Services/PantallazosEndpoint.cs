using Sgl.Application.CambioDomicilio;

namespace Sgl.Web.Services;

/// <summary>
/// GET /cambio-domicilio/pantallazos/{envioId}: download of the screenshot of a sent mail. Same protections as the certificate download:
/// the Origin must be this server, nothing is cached (the image can show names and RUT), the file is always an attachment with a generic name
/// (never the mail id, a name or a RUT) and the browser must not guess its type. 404 when the mail has no screenshot or the file is gone.
/// </summary>
public static class PantallazosEndpoint
{
    public const string Ruta = "/cambio-domicilio/pantallazos/{envioId:guid}";

    public static string RutaDe(Guid envioId) => $"/cambio-domicilio/pantallazos/{envioId}";

    public static async Task<IResult> ServirAsync(HttpContext context, Guid envioId, SeguimientoService seguimiento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(seguimiento);
        if (!SeguridadWeb.OrigenPermitido(context.Request.Headers.Origin.ToString(), context.Request.Host.Value))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";

        var pantallazo = await seguimiento.AbrirPantallazoAsync(envioId, ct);
        if (pantallazo is null)
        {
            return Results.NotFound();
        }

        var extension = pantallazo.ContentType == "image/jpeg" ? "jpg" : "png";
        return Results.File(pantallazo.Contenido, pantallazo.ContentType, $"pantallazo.{extension}");
    }
}
