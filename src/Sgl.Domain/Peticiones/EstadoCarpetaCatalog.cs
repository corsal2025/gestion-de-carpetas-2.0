namespace PeticionCambioDomicilio.Domain;

/// <summary>
/// Los mismos valores del desplegable "ESTADO DE LA CARPETA" del libro DETALLE CARPETAS
/// (hojas de agenda mensual). El operador mueve la carpeta por estos estados desde el
/// dashboard, sin volver al Excel. Al importar, una fila nueva entra como "CAMBIO DE DOMICILIO".
/// </summary>
public static class EstadoCarpetaCatalog
{
    public const string CambioDeDomicilio = "CAMBIO DE DOMICILIO";

    public static readonly IReadOnlyList<string> Valores = new[]
    {
        "CAMBIO DE DOMICILIO",
        "CAMBIO DE DOMICILIO SOLICITADO",
        "CAMBIO DOM. SUBIDO A CONASET",
        "CAMBIO DOM. SUBIDO CON CORREO",
        "SUBIDA A CONASET",
        "SUBIDA CON F8",
        "SUBIDA CON OFICIO",
        "SE ENCUENTRA EN ARCHIVOS",
        "SE ENCUENTRA EN OF. 43",
        "NO EXISTE CARPETA",
        "CREAR OFICIO",
        "CREAR CERTIFICADO",
        "CANJE LIC. EXTRANJERA",
        "1° LICENCIA",
    };

    /// <summary>Estados que significan "la carpeta ya se subió / el trámite terminó".</summary>
    public static bool EsFinalizado(string? estado) => estado is
        "CAMBIO DOM. SUBIDO A CONASET" or
        "CAMBIO DOM. SUBIDO CON CORREO" or
        "SUBIDA A CONASET" or
        "SUBIDA CON F8" or
        "SUBIDA CON OFICIO";

    /// <summary>
    /// Etapa del flujo de cambio de domicilio, para decidir si un estado que viene del Excel hace
    /// AVANZAR la carpeta o no. 0 = fuera del flujo (otro trámite), 1 = recién llegada,
    /// 2 = solicitada a la comuna, 3 = subida a CONASET (cerrada). La importación solo aplica un
    /// estado del Excel si su rango es mayor al que ya tiene la petición.
    /// </summary>
    public static int Rango(string? estadoCrudo)
    {
        var e = Normalizar(estadoCrudo);
        if (string.IsNullOrWhiteSpace(e))
        {
            return 1;
        }

        return e switch
        {
            "CAMBIO DE DOMICILIO" => 1,
            "CAMBIO DE DOMICILIO SOLICITADO" => 2,
            _ => EsFinalizado(e) ? 3 : 0,
        };
    }

    /// <summary>true si el estado significa que la carpeta la subió la comuna vía SGL (no nosotros).</summary>
    public static bool SubidaPorComuna(string? estado) => estado is
        "CAMBIO DOM. SUBIDO A CONASET" or "SUBIDA A CONASET";

    /// <summary>true si el estado significa que la carpeta la tuvimos que subir nosotros (escalamiento).</summary>
    public static bool SubidaPorNosotros(string? estado) => estado is
        "CAMBIO DOM. SUBIDO CON CORREO" or "SUBIDA CON F8" or "SUBIDA CON OFICIO";

    // --- Vista del dashboard: solo interesan tres situaciones ---
    public const string SinSubir = "CAMBIO DE DOMICILIO SOLICITADO";
    public const string SubidaConaset = "CAMBIO DOM. SUBIDO A CONASET";
    public const string SubidaCorreo = "CAMBIO DOM. SUBIDO CON CORREO";

    /// <summary>Las tres opciones del desplegable de la tabla, en orden.</summary>
    public static readonly IReadOnlyList<string> OpcionesDashboard = new[]
    {
        SinSubir, SubidaConaset, SubidaCorreo,
    };

    /// <summary>
    /// Colapsa cualquier estado de carpeta a una de las tres opciones del dashboard:
    /// "sin subir", subida a CONASET (la puso la comuna) o subida con correo (la pusimos nosotros,
    /// incluye F8 y oficio).
    /// </summary>
    public static string OpcionDashboard(string? estado)
    {
        if (Rango(estado) < 3)
        {
            return SinSubir;
        }

        return SubidaPorComuna(estado) ? SubidaConaset : SubidaCorreo;
    }

    public static string Normalizar(string? crudo)
    {
        var f = TextNormalization.Fold(crudo);
        foreach (var v in Valores)
        {
            if (TextNormalization.Fold(v) == f)
            {
                return v;
            }
        }

        // variantes de tipeo conocidas del libro
        return f switch
        {
            "se encuentra en of 43" or "se encuentra en of43" => "SE ENCUENTRA EN OF. 43",
            "cambio dom subido a conaset" => "CAMBIO DOM. SUBIDO A CONASET",
            "cambio de dom subido con correo" => "CAMBIO DOM. SUBIDO CON CORREO",
            "" => string.Empty,
            _ => crudo?.Trim().ToUpperInvariant() ?? string.Empty,
        };
    }
}
