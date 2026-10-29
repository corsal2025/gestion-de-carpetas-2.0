using Sgl.Application;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Web.Services;

/// <summary>
/// Un solo criterio para los mensajes de error de las pantallas: los errores de negocio (DomainException,
/// NotFoundException, CarpetaDuplicadaException) traen un texto pensado para el funcionario y se muestran tal cual;
/// cualquier otro error se registra con su detalle y la pantalla solo ve un mensaje genérico.
/// </summary>
public static class ErroresUi
{
    public const string Generico = "No se pudo completar la operación. Intente nuevamente; si el problema persiste, avise al administrador.";

    public static bool EsDeNegocio(Exception ex) => ex is DomainException or NotFoundException or CarpetaDuplicadaException;

    /// <summary>Mensaje para mostrar; los errores inesperados se registran aquí (sin datos personales en el texto).</summary>
    public static string Mensaje(Exception ex, ILogger logger, string contexto)
    {
        if (EsDeNegocio(ex)) return ex.Message;
        logger.LogError(ex, "{Contexto}.", contexto);
        return Generico;
    }
}
