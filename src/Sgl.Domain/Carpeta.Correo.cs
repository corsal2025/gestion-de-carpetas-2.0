namespace Sgl.Domain;

public sealed partial class Carpeta
{
    /// <summary>Observation left on a carpeta created from an email whose name could not be read with confidence.</summary>
    public const string ObservacionRevisarNombreCorreo = "Revisar nombre (extraído del correo)";

    /// <summary>
    /// Change-of-address request received by email from another comuna: born directly in
    /// <see cref="EstadoCarpeta.CambioDomicilio"/>. The request date is the date the email was received. An unknown
    /// comuna is left empty (never invented); <paramref name="revisarNombre"/> leaves the review observation.
    /// </summary>
    public static Carpeta RegistrarDesdeCorreo(
        Ciudadano ciudadano,
        Sede sede,
        DateOnly fechaSolicitud,
        string? comuna,
        bool revisarNombre,
        string autor,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(ciudadano);
        var usuario = RequireAutor(autor);
        RequireSede(sede);

        var carpeta = new Carpeta
        {
            Id = Guid.NewGuid(),
            Ciudadano = ciudadano,
            CiudadanoId = ciudadano.Id,
            Sede = sede,
            FechaCitacion = fechaSolicitud,
            Estado = EstadoCarpeta.CambioDomicilio,
            Decision = Decision.Pendiente,
            Comuna = ComunasChile.Normalizar(comuna),
            IdoneidadMoral = revisarNombre ? ObservacionRevisarNombreCorreo : null,
            FechaRegistro = now,
            FechaEstadoActual = now,
        };
        carpeta.Audit(now, usuario, "Registro", null, $"{EstadoCarpeta.CambioDomicilio} ({sede}) desde correo");
        if (carpeta.Comuna is not null)
        {
            carpeta.Audit(now, usuario, nameof(Comuna), null, carpeta.Comuna);
        }
        if (revisarNombre)
        {
            carpeta.Audit(now, usuario, "Observación", null, ObservacionRevisarNombreCorreo);
        }
        return carpeta;
    }
}
