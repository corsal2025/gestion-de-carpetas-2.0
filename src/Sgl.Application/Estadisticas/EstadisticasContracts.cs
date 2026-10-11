using Sgl.Domain;

namespace Sgl.Application.Estadisticas;

/// <summary>
/// Global filters for the statistics page. <see cref="Desde"/>/<see cref="Hasta"/> restrict carpetas by
/// citación date and audit-based metrics (búsquedas, archivo, uso) by the date of the audit entry.
/// </summary>
public sealed record FiltroEstadisticas(
    Sede? Sede = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    int DiasSinSubir = 30,
    int DiasBusquedaAbierta = 1)
{
    /// <summary>Red traffic-light threshold (days since citación) per open tray.</summary>
    public IReadOnlyDictionary<Bandeja, int> UmbralRojo { get; init; } = new Dictionary<Bandeja, int>();
}

public interface IEstadisticasQuery
{
    Task<EstadisticasDto> ObtenerAsync(FiltroEstadisticas filtro, DateOnly hoy, CancellationToken ct = default);
}

public sealed record EstadisticasDto(
    KpisDto Kpis,
    DatosActualesDto Actuales,
    BusquedasDto Busquedas,
    ArchivoDto Archivo,
    UsoSistemaDto Uso);

public sealed record KpisDto(int SubidasEsteMes, int CarpetasAtrasadas, int BusquedasAbiertas, double? MedianaDiasCitacionSubida);

// ---- A. Datos actuales ----
public sealed record DatosActualesDto(
    IReadOnlyList<SubidasMesDto> SubidasPorMes,
    IReadOnlyList<ConteoSedeDto> EstadoPorSede,
    TiemposDto CitacionSubida,
    IReadOnlyList<CarpetaLentaDto> MasLentas,
    IReadOnlyList<ConteoSedeDto> CitadasSinSubir,
    IReadOnlyList<DecisionSedeDto> DecisionPorSede,
    IReadOnlyList<DecisionMesDto> DecisionPorMes,
    IReadOnlyList<CambioDomicilioSedeDto> CambiosDomicilio,
    IReadOnlyList<ConteoDto> ComunasTop,
    int PersonasConVariasAbiertas,
    int DiasSinSubir);

/// <summary>Uploads in one month for one sede, split by channel (vía).</summary>
public sealed record SubidasMesDto(int Anio, int Mes, Sede Sede, int Conaset, int F8, int Oficio, int Correo, int Otra)
{
    public int Total => Conaset + F8 + Oficio + Correo + Otra;
}

public sealed record ConteoSedeDto(Sede Sede, string Clave, int Cantidad);

public sealed record ConteoDto(string Clave, int Cantidad);

public sealed record TiemposDto(int Muestras, double? Promedio, double? Mediana, double? Maximo);

/// <summary>Slow carpeta reference without personal data (only id for navigation).</summary>
public sealed record CarpetaLentaDto(Guid Id, Sede Sede, DateOnly FechaCitacion, DateOnly FechaSubida, int Dias);

public sealed record DecisionSedeDto(Sede Sede, int Otorgados, int Denegados)
{
    public double? TasaOtorgamiento => Otorgados + Denegados == 0 ? null : (double)Otorgados / (Otorgados + Denegados);
}

public sealed record DecisionMesDto(int Anio, int Mes, int Otorgados, int Denegados);

public sealed record CambioDomicilioSedeDto(Sede Sede, int Solicitados, int Subidos);

// ---- B. Búsqueda ----
public sealed record BusquedasDto(
    bool HayDatos,
    int Iniciadas,
    int Encontradas,
    int NoEncontradas,
    int Abiertas,
    int AbiertasVencidas,
    TiemposDto HorasParaEncontrar,
    int EncontradasArchivo,
    int EncontradasOficina43,
    int EncontradasSectorDesconocido);

// ---- C. Archivo / cajas ----
public sealed record ArchivoDto(
    IReadOnlyList<ArchivoMesDto> PorMes,
    int CajasConCarpetas,
    double? OcupacionPromedio,
    int CajasReabiertas,
    int EnColaDeCaja);

public sealed record ArchivoMesDto(int Anio, int Mes, int CarpetasArchivadas, int CajasCerradas);

// ---- D. Uso del sistema ----
public sealed record UsoSistemaDto(
    IReadOnlyList<ConteoDto> CambiosPorFuncionario,
    IReadOnlyList<ConteoDto> CambiosPorDia,
    int RegistradasAMano,
    int Importadas,
    IReadOnlyList<ImportacionDto> Importaciones,
    bool ImportacionesInferidas,
    int CarpetasConObservacion,
    IReadOnlyList<ConteoDto> TerminosObservacion,
    IReadOnlyList<ClaseSedeDto> ClasePorSede);

public sealed record ImportacionDto(DateTime Fecha, string Usuario, int FilasLeidas, int Creadas, int Actualizadas, int Errores);

public sealed record ClaseSedeDto(Sede Sede, int ConClase, int SinClase)
{
    public double Porcentaje => ConClase + SinClase == 0 ? 0 : (double)ConClase / (ConClase + SinClase);
}
