namespace PeticionCambioDomicilio.Domain;

/// <summary>
/// Texto fijo del correo de solicitud, con cita legal del artículo 14 del Decreto 170 (MTT).
/// Un correo por comuna: lista todas las personas cuyas carpetas se piden a esa municipalidad.
/// Alineado con LicenciasCarpetas OutboundRequestSender.BuildBody().
/// </summary>
public static class EmailTemplate
{
    // Sin nombre ni RUT en el asunto: los datos personales van solo en el cuerpo, no en la
    // linea de asunto que queda visible en listados de correo, notificaciones y logs.
    public static string Subject() => "Solicitud de cambio de domicilio";

    /// <summary>Correo para una sola persona (diagnóstico --send-test).</summary>
    public static string Body(Peticion p, string firmaCorreo) => Body(new[] { p }, firmaCorreo);

    /// <summary>
    /// Correo para una comuna con una o varias personas. Con una, se lee en singular natural;
    /// con varias, lista numerada de Nombre + RUT.
    /// </summary>
    public static string Body(IReadOnlyList<Peticion> peticiones, string firmaCorreo)
    {
        if (peticiones.Count == 0)
        {
            throw new ArgumentException("El correo necesita al menos una petición.", nameof(peticiones));
        }

        var varias = peticiones.Count > 1;
        var sujeto = varias
            ? "las carpetas con los antecedentes de los siguientes conductores"
            : "la carpeta con los antecedentes del siguiente conductor";

        var lines = new List<string>
        {
            "Estimados,",
            "",
            "Junto con saludar, y conforme a lo establecido en el artículo 14 del Decreto N.º 170 del " +
                "Ministerio de Transportes y Telecomunicaciones, \"Reglamento para el Otorgamiento de " +
                "Licencias de Conducir\", solicito a ustedes tengan a bien hacer llegar, a través de la " +
                $"Plataforma SGL, {sujeto}, para la correspondiente emisión de su licencia de conducir:",
            "",
        };

        if (varias)
        {
            var n = 1;
            foreach (var p in peticiones)
            {
                lines.Add($"{n}. Nombre: {p.NombreCompleto} — RUT: {p.Rut}{ClaseSufijo(p)}");
                n++;
            }
        }
        else
        {
            var p = peticiones[0];
            lines.Add($"Nombre: {p.NombreCompleto}");
            lines.Add($"RUT: {p.Rut}{ClaseSufijo(p)}");
        }

        lines.Add("");
        lines.Add("Quedamos atentos a su respuesta.");
        lines.Add("");
        lines.Add("Saludos cordiales,");
        lines.Add("Departamento de Licencias de Conducir");
        lines.Add("Municipalidad de Valparaíso");
        lines.Add(firmaCorreo);

        return string.Join("\n", lines);
    }

    private static string ClaseSufijo(Peticion p) =>
        string.IsNullOrWhiteSpace(p.Clases) ? "" : $" — Clase {p.Clases}";
}
