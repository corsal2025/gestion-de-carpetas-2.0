namespace PeticionCambioDomicilio.Domain;

public enum EstadoPeticion
{
    Borrador,
    Enviada,
    SinCorreoComuna,
    Error,
    /// <summary>Reservada por un envío en curso (evita el doble envío). Solo existe durante el envío.</summary>
    Enviando
}

/// <summary>
/// Una petición de carpeta a otra comuna, nacida de una fila "CAMBIO DE DOMICILIO" del Excel.
/// Clave natural de deduplicación: RUT + comuna destino.
/// </summary>
public sealed class Peticion
{
    /// <summary>Oficina de las peticiones cargadas a mano (no vienen de ninguna hoja del Excel).</summary>
    public const string OficinaManual = "MANUAL";

    public long Id { get; set; }
    /// <summary>Siempre en MAYÚSCULAS: venga del Excel, de la carga manual o de la base.</summary>
    public required string NombreCompleto
    {
        get => _nombreCompleto;
        set => _nombreCompleto = (value ?? "").Trim().ToUpperInvariant();
    }

    private string _nombreCompleto = "";
    public required string Rut { get; set; }
    public required string Comuna { get; set; }
    public string? Clases { get; set; }
    public DateOnly? FechaSolicitud { get; set; }

    /// <summary>Oficina de la que salio la fila: AV. ARGENTINA, PLACILLA o MERC. PUERTO.
    /// Es lo unico que se muestra en la columna Origen; el detalle de hoja y fila va aparte.</summary>
    public string? Oficina { get; set; }

    /// <summary>Hoja + fila del Excel de origen, para trazabilidad (no se muestra en la tabla).</summary>
    public string? Origen { get; set; }

    /// <summary>Posicion en el libro: hojas en el orden en que aparecen y filas de arriba abajo.
    /// La lista se ordena por esto, para que las peticiones queden en el mismo orden del Excel
    /// y no dispersas.</summary>
    public long OrdenImportacion { get; set; }

    /// <summary>Marca personal del operador (columna Marcar del dashboard).</summary>
    public bool Marcada { get; set; }

    /// <summary>Se llena si el RUT del Excel no valida — la fila igual se guarda para revisión manual.</summary>
    public bool RutInvalido { get; set; }

    public EstadoPeticion Estado { get; set; } = EstadoPeticion.Borrador;
    public string? DetalleEstado { get; set; }
    public DateTimeOffset CreadaEn { get; set; } = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Sgl.Domain.HoraChile.Zona);
    public DateTimeOffset? EnviadaEn { get; set; }

    /// <summary>Direcciones a las que se envió (coma-separadas), para mostrar en la lista.</summary>
    public string? DestinatariosCorreo { get; set; }

    /// <summary>Estado de la carpeta física en el flujo — los mismos valores del desplegable
    /// "ESTADO DE LA CARPETA" del Excel. El operador lo cambia desde el dashboard y la
    /// importación lo hace avanzar cuando el Excel trae un estado más adelantado.
    /// Al importar entra como "CAMBIO DE DOMICILIO".</summary>
    public string EstadoCarpeta { get; set; } = EstadoCarpetaCatalog.CambioDeDomicilio;

    /// <summary>Fecha en que la carpeta se subió a CONASET. Sale de la columna
    /// "FECHA CUANDO SE SUBIO LA CARPETA" del Excel; si esa celda está vacía cuando el estado ya
    /// figura como subida, se usa la fecha de la importación (o del cambio manual). Mide la
    /// demora de cada comuna: días hábiles entre <see cref="EnviadaEn"/> y esta fecha.</summary>
    public DateOnly? SubidaEn { get; set; }
}
