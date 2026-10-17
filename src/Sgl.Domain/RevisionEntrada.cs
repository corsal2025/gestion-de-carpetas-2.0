namespace Sgl.Domain;

/// <summary>Facts about one Entrada carpeta that decide whether it needs review. <see cref="Duplicada"/> comes from other rows.</summary>
public sealed record SenalesRevision(bool Duplicada, string? Rut, TramiteAgenda? TramiteAgenda, DateOnly FechaCitacion);

/// <summary>
/// Splits the Entrada carpetas (base tray <see cref="Bandeja.PorClasificar"/> or <see cref="Bandeja.ParaRevision"/>) into
/// "Licencias para clasificar" (clean) and the REVISIÓN submodules. A carpeta lands in the first matching submodule, in
/// the order of <see cref="Submodulos"/>; once fixed it goes back to its base tray automatically (nothing is stored).
/// </summary>
public static class RevisionEntrada
{
    /// <summary>REVISIÓN submodules in menu and precedence order; Citadas (ParaRevision) is the last one.</summary>
    public static readonly IReadOnlyList<Bandeja> Submodulos =
    [
        Bandeja.RevisionDuplicadas,
        Bandeja.RevisionRutInvalido,
        Bandeja.RevisionSinRut,
        Bandeja.RevisionNoIdentificada,
        Bandeja.RevisionNoEvaluadas,
        Bandeja.ParaRevision,
    ];

    /// <summary>True for "Licencias para clasificar" and every REVISIÓN submodule.</summary>
    public static bool EsDeEntrada(Bandeja bandeja) => bandeja == Bandeja.PorClasificar || Submodulos.Contains(bandeja);

    /// <param name="bandejaBase">Tray by state alone (<see cref="Carpeta.ClasificarBandeja"/>); any other tray is returned as is.</param>
    public static Bandeja Clasificar(Bandeja bandejaBase, SenalesRevision senales, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(senales);
        if (bandejaBase is not (Bandeja.PorClasificar or Bandeja.ParaRevision)) return bandejaBase;
        if (senales.Duplicada) return Bandeja.RevisionDuplicadas;
        if (string.IsNullOrWhiteSpace(senales.Rut)) return Bandeja.RevisionSinRut;
        if (!Domain.Rut.TryParse(senales.Rut, out _)) return Bandeja.RevisionRutInvalido;
        // Only agenda carpetas declare a trámite; legacy Excel Citadas never have one, so it is not a problem for them.
        if (bandejaBase == Bandeja.PorClasificar && senales.TramiteAgenda is null) return Bandeja.RevisionNoIdentificada;
        if (senales.FechaCitacion > hoy) return Bandeja.RevisionNoEvaluadas;
        return bandejaBase;
    }

    /// <summary>Plain-language problem shown in the PROBLEMA column of each REVISIÓN submodule.</summary>
    public static string Problema(Bandeja bandeja) => bandeja switch
    {
        Bandeja.RevisionDuplicadas => "Más de una carpeta abierta para el mismo RUT",
        Bandeja.RevisionRutInvalido => "RUT con dígito verificador incorrecto",
        Bandeja.RevisionSinRut => "Fila sin RUT",
        Bandeja.RevisionNoIdentificada => "Trámite de la agenda no reconocido",
        Bandeja.RevisionNoEvaluadas => "Citación futura: aún no evaluada",
        Bandeja.ParaRevision => "Citada sin resolución",
        _ => string.Empty,
    };
}
