namespace PeticionCambioDomicilio;

/// <summary>
/// Configuración de la app. Todo es opcional: sin configurar, la app arranca igual
/// y cada pantalla indica qué falta. Se lee de la sección "Peticion" de
/// appsettings.json / appsettings.Local.json.
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "Peticion";

    /// <summary>Ruta al libro Excel (DETALLE CARPETAS ... .xlsx) que se importa desde la pantalla Peticiones.</summary>
    public string? ExcelPath { get; set; }

    /// <summary>Mapeo de columnas del Excel. Se busca por encabezado (sin distinguir mayúsculas/tildes),
    /// así que el orden de las columnas en la hoja no importa. La fila de encabezado se detecta sola
    /// (primera fila que contiene "RUT").</summary>
    public ColumnMap Columns { get; set; } = new();

    /// <summary>Valor de la columna "Estado de la carpeta" que marca una fila como
    /// "hay que pedir la carpeta a la otra comuna". Comparación sin distinguir mayúsculas ni tildes.</summary>
    public string EstadoCambioDomicilio { get; set; } = "CAMBIO DE DOMICILIO";

    /// <summary>Nombres de hoja que se ignoran (no son agenda mensual).</summary>
    public string[] HojasIgnoradas { get; set; } =
        ["PLANTILLA", "HOJA ESTADISTICAS", "CORREOS CAMBIO DE DOMICLIO", "CORREOS CAMBIO DE DOMICILIO"];

    /// <summary>Ruta al CSV del directorio de comunas. Vacío = data/comunas.csv junto al ejecutable.</summary>
    public string? ComunaDirectoryCsvPath { get; set; }

    /// <summary>Carpeta para copias locales de la base. Vacío = Documentos/RespaldoPeticionCambioDomicilio.</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>Correo institucional (aparece como firma / remitente).</summary>
    public string MailboxAddress { get; set; } = "cambiodedomicilio@munivalpo.cl";

    /// <summary>
    /// MODO PRUEBA. Si tiene una dirección, NINGÚN correo sale hacia las municipalidades:
    /// todos se desvían a esta casilla, con el asunto prefijado [PRUEBA] y una cabecera en el
    /// cuerpo que dice a qué comuna y a qué direcciones habría ido. Vacío = envío real.
    /// Es la red de seguridad para probar sin escribirle a 300 municipios por error.
    /// </summary>
    public string? TestModeEmail { get; set; }

    public EwsOptions? Ews { get; set; }
}

public sealed class ColumnMap
{
    public string NombreCompleto { get; set; } = "NOMBRE COMPLETO";
    public string Rut { get; set; } = "RUT";
    public string EstadoCarpeta { get; set; } = "ESTADO DE LA CARPETA";

    /// <summary>Columna que, cuando trae texto en vez de fecha, contiene la comuna de origen de la persona.</summary>
    public string ComunaOrigen { get; set; } = "FECHA ULTIMA CARPETA";

    /// <summary>Columna de la que sale la "fecha de solicitud" de la petición.</summary>
    public string FechaSolicitud { get; set; } = "FECHA DE LA CITACION";

    /// <summary>Columna con la fecha en que la comuna subió la carpeta a CONASET. Se usa para medir
    /// cuánto se demora cada comuna. Si está vacía cuando el estado ya figura como subida, se toma
    /// la fecha de la importación.</summary>
    public string FechaSubidaCarpeta { get; set; } = "FECHA CUANDO SE SUBIO LA CARPETA";

    /// <summary>Opcional: clases de licencia. El libro DETALLE CARPETAS no la trae; queda vacía.</summary>
    public string Clases { get; set; } = "CLASES";
}

public sealed class EwsOptions
{
    public string? Url { get; set; }

    /// <summary>
    /// Usuario con el que autenticarse. Exchange on-premises con Basic suele querer
    /// DOMINIO\usuario o el UPN, no siempre la direccion de correo.
    /// Si el buzon del departamento es COMPARTIDO no tiene clave propia: hay que poner aca el
    /// usuario personal de dominio (el que tiene permiso Send As) y usar SendAsAddress.
    /// </summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// Direccion desde la que sale el correo, cuando es distinta de la cuenta autenticada.
    /// Caso tipico: te autenticas con tu usuario y envias como el buzon compartido
    /// cambiodedomicilio@munivalpo.cl. Requiere permiso "Send As" sobre ese buzon.
    /// Vacio = sale como la cuenta autenticada.
    /// </summary>
    public string? SendAsAddress { get; set; }
}
