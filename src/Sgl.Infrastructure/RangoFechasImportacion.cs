namespace Sgl.Infrastructure;

/// <summary>
/// Rango de fechas creíble para cualquier archivo que se importa (agenda, detalle de carpetas, peticiones).
/// Una fecha fuera de él casi siempre es un error de tipeo o de formato (año 1900, 2062, mes/día invertidos),
/// y se reporta en vez de guardarse.
/// </summary>
public static class RangoFechasImportacion
{
    public static readonly DateOnly Minima = new(2015, 1, 1);

    /// <summary>Hasta un año hacia adelante: las citaciones se agendan con anticipación.</summary>
    public static DateOnly Maxima(DateOnly hoy) => hoy.AddYears(1);

    public static bool EsPlausible(DateOnly fecha, DateOnly hoy) => fecha >= Minima && fecha <= Maxima(hoy);
}
