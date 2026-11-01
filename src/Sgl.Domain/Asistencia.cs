namespace Sgl.Domain;

public enum TipoAsistencia
{
    /// <summary>The citizen had an appointment today and came.</summary>
    Vino = 1,

    /// <summary>Came without an appointment (signed the paper): a carpeta was created for it.</summary>
    Extra = 2,

    /// <summary>Signed the paper but is left for later ("la X del papel"): not counted until resolved.</summary>
    PorAgregar = 3,
}

/// <summary>
/// Permanent record of who came on a day. Never deleted: undoing a mark only flags it as <see cref="Anulada"/>. Resolving
/// a "por agregar" row annuls it and creates the Vino / Extra row.
/// </summary>
public sealed class Asistencia
{
    public Guid Id { get; private set; }

    /// <summary>Chile calendar date of the day the person came.</summary>
    public DateOnly Fecha { get; private set; }

    public TipoAsistencia Tipo { get; private set; }

    public Guid? CarpetaId { get; private set; }

    /// <summary>Normalized RUT ("12345678-5").</summary>
    public string Rut { get; private set; } = string.Empty;

    public Sede Sede { get; private set; }

    public DateTime RegistradaEn { get; private set; }

    public string Autor { get; private set; } = string.Empty;

    public bool Anulada { get; private set; }

    public string? AnuladaPor { get; private set; }

    public DateTime? AnuladaEn { get; private set; }

    private Asistencia()
    {
    }

    public static Asistencia Vino(Guid carpetaId, Rut rut, Sede sede, DateOnly fecha, string autor, DateTime now) =>
        Crear(TipoAsistencia.Vino, carpetaId, rut, sede, fecha, autor, now);

    public static Asistencia Extra(Guid carpetaId, Rut rut, Sede sede, DateOnly fecha, string autor, DateTime now) =>
        Crear(TipoAsistencia.Extra, carpetaId, rut, sede, fecha, autor, now);

    public static Asistencia PorAgregar(Rut rut, Sede sede, DateOnly fecha, string autor, DateTime now) =>
        Crear(TipoAsistencia.PorAgregar, null, rut, sede, fecha, autor, now);

    /// <summary>Follows a RUT correction: the mark keeps pointing at the same person under the corrected RUT.</summary>
    public void ReasignarRut(Rut rut)
    {
        ArgumentNullException.ThrowIfNull(rut);
        Rut = rut.Value;
    }

    public void Anular(string autor, DateTime now)
    {
        var usuario = Autores.Requerir(autor);
        if (Anulada)
        {
            throw new DomainException("La asistencia ya estaba deshecha.");
        }

        Anulada = true;
        AnuladaPor = usuario;
        AnuladaEn = now;
    }

    private static Asistencia Crear(TipoAsistencia tipo, Guid? carpetaId, Rut rut, Sede sede, DateOnly fecha, string autor, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(rut);
        if (!Enum.IsDefined(sede))
        {
            throw new DomainException("Sede inválida.");
        }

        return new Asistencia
        {
            Id = Guid.NewGuid(),
            Fecha = fecha,
            Tipo = tipo,
            CarpetaId = carpetaId,
            Rut = rut.Value,
            Sede = sede,
            RegistradaEn = now,
            Autor = Autores.Requerir(autor),
        };
    }
}
