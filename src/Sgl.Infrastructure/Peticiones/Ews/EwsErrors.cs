using System.Net;

namespace PeticionCambioDomicilio.Ews;

/// <summary>Falló un envío por EWS. Base para que el llamador distinga la causa.</summary>
public class EwsSendException : Exception
{
    public EwsSendException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// El servidor rechazó las credenciales (401 / 403). NO se reintenta: cada intento fallido
/// suma al bloqueo de la cuenta de dominio. El operador tiene que revisar usuario/clave.
/// </summary>
public sealed class EwsAuthException : EwsSendException
{
    public EwsAuthException(string message) : base(message) { }
}

/// <summary>
/// Decide si un fallo de EWS se puede reintentar. Extraído a función pura para poder testearlo
/// sin tocar la red: el bug histórico era reintentar 4 veces un 401 y acelerar el bloqueo.
/// </summary>
public static class EwsRetryPolicy
{
    /// <summary>Códigos donde reintentar tiene sentido: el servidor está sobrecargado o caído
    /// un momento, no que la petición esté mal.</summary>
    private static readonly HashSet<int> Transient = new() { 408, 425, 429, 500, 502, 503, 504 };

    /// <summary>Credenciales rechazadas. Reintentar solo suma al bloqueo de la cuenta.</summary>
    public static bool IsAuthFailure(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public static bool IsAuthFailure(int status) => status is 401 or 403;

    /// <summary>true si conviene reintentar ese código HTTP.</summary>
    public static bool IsTransient(HttpStatusCode status) => Transient.Contains((int)status);

    public static bool IsTransient(int status) => Transient.Contains(status);

    /// <summary>
    /// Qué hacer ante una respuesta HTTP de EWS.
    /// <list type="bullet">
    ///   <item><see cref="EwsAction.Accept"/> — 2xx, seguir.</item>
    ///   <item><see cref="EwsAction.Retry"/> — transitorio, reintentar con backoff.</item>
    ///   <item><see cref="EwsAction.FailAuth"/> — 401/403, cortar ya y avisar de credenciales.</item>
    ///   <item><see cref="EwsAction.FailPermanent"/> — cualquier otro error, cortar sin reintentar.</item>
    /// </list>
    /// </summary>
    public static EwsAction Classify(HttpStatusCode status)
    {
        var code = (int)status;
        if (code is >= 200 and < 300)
        {
            return EwsAction.Accept;
        }

        if (IsAuthFailure(status))
        {
            return EwsAction.FailAuth;
        }

        return IsTransient(status) ? EwsAction.Retry : EwsAction.FailPermanent;
    }
}

public enum EwsAction
{
    Accept,
    Retry,
    FailAuth,
    FailPermanent,
}
