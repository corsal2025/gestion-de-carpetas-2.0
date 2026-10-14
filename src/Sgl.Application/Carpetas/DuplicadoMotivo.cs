namespace Sgl.Application.Carpetas;

/// <summary>Plain-language reason why a citizen shows up as a possible duplicate.</summary>
public static class DuplicadoMotivo
{
    public static string Describir(bool rutValido, DateOnly primeraCita, DateOnly ultimaCita, bool mismaSede)
    {
        if (!rutValido)
        {
            return "RUT inválido: revisar los datos del ciudadano";
        }

        var mismaFecha = primeraCita == ultimaCita;
        return (mismaFecha, mismaSede) switch
        {
            (true, true) => $"Misma cita repetida ({primeraCita:dd-MM-yyyy})",
            (true, false) => $"Misma fecha en sedes distintas ({primeraCita:dd-MM-yyyy})",
            (false, true) => $"Citas en fechas distintas ({primeraCita:dd-MM-yyyy} a {ultimaCita:dd-MM-yyyy})",
            _ => $"Fechas y sedes distintas ({primeraCita:dd-MM-yyyy} a {ultimaCita:dd-MM-yyyy})",
        };
    }
}
