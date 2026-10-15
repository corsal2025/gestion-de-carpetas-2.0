using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Panel;

/// <summary>Today's classification goal: carpetas classified today versus those still waiting in "Por clasificar".</summary>
public sealed record MetaDelDiaDto(int Clasificadas, int Pendientes)
{
    public int Total => Clasificadas + Pendientes;

    public int Porcentaje => Total == 0 ? 100 : (int)Math.Round(Clasificadas * 100.0 / Total, MidpointRounding.AwayFromZero);

    /// <summary>Nothing left to classify (also true on a day without agenda).</summary>
    public bool Completa => Pendientes == 0;
}

/// <summary>Traffic light of one module: color (null for closing trays) plus how many carpetas are late.</summary>
public sealed record SemaforoModuloDto(Semaforo? Color, int Amarillas, int Rojas);

/// <summary>One home-panel card: tray, current count, traffic light and entries per day over the last 7 days.</summary>
public sealed record ModuloPanelDto(Bandeja Bandeja, int Conteo, Semaforo? Semaforo, int Atrasadas, IReadOnlyList<int> Serie);

public sealed record PanelDto(
    IReadOnlyList<ModuloPanelDto> Modulos,
    MetaDelDiaDto Meta,
    int DuplicadosPendientes,
    int AlertasDuplicado,
    int Anuladas,
    IReadOnlyList<int> SerieAnuladas,
    string MensajeLico,
    IReadOnlyDictionary<Bandeja, MetaDelDiaDto> ActividadHoy);

/// <summary>Carpetas grouped by the raw columns that decide their tray, plus appointment date.</summary>
public sealed record GrupoBandejaDto(EstadoCarpeta Estado, Decision Decision, bool EnCajaReal, DateOnly FechaCitacion, int Cantidad)
{
    public Bandeja Bandeja => Carpeta.ClasificarBandeja(Estado, Decision, EnCajaReal ? "CAJA" : null);
}

/// <summary>Audit entry of a non-cancelled carpeta, reduced to what the panel needs.</summary>
public sealed record MovimientoPanelDto(DateTime Fecha, string Campo, string? Anterior, string? Nuevo);

/// <summary>Read model for the home panel. Implemented in Infrastructure.</summary>
public interface IPanelQuery
{
    Task<IReadOnlyList<GrupoBandejaDto>> GruposAsync(CancellationToken ct = default);

    /// <summary>Audit entries (from <paramref name="desdeUtc"/>) that can move a carpeta into a tray.</summary>
    Task<IReadOnlyList<MovimientoPanelDto>> MovimientosDesdeAsync(DateTime desdeUtc, CancellationToken ct = default);

    Task<int> PendientesPorClasificarAsync(CancellationToken ct = default);

    /// <summary>"Por clasificar" carpetas whose RUT already has another open, non-archived carpeta.</summary>
    Task<int> AlertasDuplicadoAsync(CancellationToken ct = default);

    Task<int> AnuladasAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DateTime>> FechasAnulacionDesdeAsync(DateTime desdeUtc, CancellationToken ct = default);
}
