using System.Diagnostics;
using System.Threading.Channels;
using Sgl.Domain;

namespace Sgl.Application.Integridad;

/// <summary>Why a watchdog run happened.</summary>
public static class OrigenIntegridad
{
    public const string Inicio = "Inicio";
    public const string Importacion = "Importacion";
    public const string Diaria = "Diaria";
    public const string Manual = "Manual";
}

/// <summary>A broken rule with the minimum identification an official needs to find the carpeta inside the system.</summary>
public sealed record ProblemaIntegridadDto(
    ReglaIntegridad Regla,
    string Detalle,
    Guid? CarpetaId,
    string? Rut,
    string? NombreCompleto,
    Sede? Sede,
    EstadoCarpeta? Estado);

public sealed record CorridaIntegridadDto(
    DateTime Fecha,
    string Origen,
    int Problemas,
    bool Fallo,
    int DuracionMs,
    string Detalle)
{
    public bool TieneProblemas => Fallo || Problemas > 0;
}

/// <summary>Result of the live check shown in the "Integridad" view.</summary>
public sealed record ResultadoIntegridadDto(
    CorridaIntegridadDto Corrida,
    IReadOnlyDictionary<ReglaIntegridad, int> PorRegla,
    IReadOnlyList<ProblemaIntegridadDto> Problemas,
    int Ocultos = 0);

/// <summary>Persistence/read port of the watchdog. Implemented in Infrastructure.</summary>
public interface IIntegridadRepository
{
    Task<EntradaIntegridad> ObtenerEntradaAsync(CancellationToken ct = default);

    Task GuardarCorridaAsync(IntegridadCorrida corrida, CancellationToken ct = default);

    Task<IntegridadCorrida?> UltimaCorridaAsync(CancellationToken ct = default);

    Task<IReadOnlyList<IntegridadCorrida>> UltimasCorridasAsync(int cantidad, CancellationToken ct = default);

    /// <summary>Identification (RUT, name, sede, state) of the given carpetas, cancelled ones included.</summary>
    Task<IReadOnlyDictionary<Guid, ProblemaIntegridadDto>> IdentificarAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}

/// <summary>
/// Integrity watchdog use cases: run the five rules, record the run, expose the last result and the live list of
/// affected carpetas. Never mutates a carpeta: it only observes and reports.
/// </summary>
public sealed class IntegridadService(IIntegridadRepository repositorio, TimeProvider clock)
{
    public const int CorridasVisibles = 15;

    /// <summary>Rows listed in the view; the rest are only counted (a legacy data problem can touch thousands).</summary>
    public const int ProblemasListados = 200;

    /// <summary>Runs the rules, stores the run (counts only) and returns the full result.</summary>
    public async Task<ResultadoIntegridadDto> EjecutarAsync(string origen, CancellationToken ct = default)
    {
        var reloj = Stopwatch.StartNew();
        var fecha = clock.GetUtcNow().UtcDateTime;
        try
        {
            var entrada = await repositorio.ObtenerEntradaAsync(ct);
            var violaciones = ReglasIntegridad.Evaluar(entrada, clock.HoyChile());
            var porRegla = Enum.GetValues<ReglaIntegridad>().ToDictionary(r => r, r => violaciones.Count(x => x.Regla == r));
            var corrida = new IntegridadCorrida(
                fecha, origen, violaciones.Count, false, (int)reloj.ElapsedMilliseconds,
                string.Join(';', porRegla.Select(p => $"{ReglasIntegridadInfo.Codigo(p.Key)}={p.Value}")));
            await repositorio.GuardarCorridaAsync(corrida, ct);

            var listadas = violaciones.Take(ProblemasListados).ToList();
            var ids = listadas.Where(x => x.CarpetaId is not null).Select(x => x.CarpetaId!.Value).Distinct().ToList();
            var quien = await repositorio.IdentificarAsync(ids, ct);
            var problemas = listadas
                .Select(x => x.CarpetaId is { } id && quien.TryGetValue(id, out var info)
                    ? info with { Regla = x.Regla, Detalle = x.Detalle }
                    : new ProblemaIntegridadDto(x.Regla, x.Detalle, x.CarpetaId, null, null, null, null))
                .ToList();
            return new ResultadoIntegridadDto(Mapear(corrida), porRegla, problemas, violaciones.Count - listadas.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The failure is recorded (type only, never data) so the home banner cannot claim "all clear".
            var corrida = new IntegridadCorrida(fecha, origen, 0, true, (int)reloj.ElapsedMilliseconds, $"Error: {ex.GetType().Name}");
            try
            {
                await repositorio.GuardarCorridaAsync(corrida, CancellationToken.None);
            }
            catch (Exception)
            {
                // Recording the failure must never hide the original exception (e.g. the database itself is down).
            }

            throw;
        }
    }

    public async Task<CorridaIntegridadDto?> UltimaAsync(CancellationToken ct = default) =>
        await repositorio.UltimaCorridaAsync(ct) is { } c ? Mapear(c) : null;

    public async Task<IReadOnlyList<CorridaIntegridadDto>> HistorialAsync(CancellationToken ct = default) =>
        (await repositorio.UltimasCorridasAsync(CorridasVisibles, ct)).Select(Mapear).ToList();

    private static CorridaIntegridadDto Mapear(IntegridadCorrida c) =>
        new(c.Fecha, c.Origen, c.Problemas, c.Fallo, c.DuracionMs, c.Detalle);
}

/// <summary>
/// Singleton mailbox that asks the background watchdog for a run (after each import). Requests are coalesced:
/// while one is pending, further requests add nothing.
/// </summary>
public sealed class IntegridadSolicitudes
{
    private readonly Channel<string> _canal = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Solicitar(string origen) => _canal.Writer.TryWrite(origen);

    public ValueTask<string> EsperarAsync(CancellationToken ct) => _canal.Reader.ReadAsync(ct);

    /// <summary>Waits for a request or until <paramref name="limite"/> elapses (returns null on timeout).</summary>
    public async Task<string?> EsperarAsync(TimeSpan limite, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(limite);
        try
        {
            return await _canal.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
