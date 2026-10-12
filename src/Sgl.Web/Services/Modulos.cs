using Sgl.Application.Carpetas;
using Sgl.Application.Panel;
using Sgl.Domain;

namespace Sgl.Web.Services;

/// <summary>Work-flow group of modules; drives the group color in the home panel and the side menu.</summary>
public sealed record GrupoModulo(string Clave, string Nombre, string Css, string Icono);

/// <summary>One navigable module: a tray of /carpetas, or a dedicated page (duplicates, cancelled, all).</summary>
public sealed record Modulo(string Clave, GrupoModulo Grupo, string Nombre, string Corto, string Href, Bandeja? Bandeja = null);

/// <summary>Single catalog of modules shared by the home panel and the side menu (R4: one source of truth).</summary>
public static class ModulosCatalogo
{
    public const string ClaveDuplicados = "duplicados";
    public const string ClaveAnuladas = "anuladas";
    public const string ClaveTodas = "todas";

    // Simple 24x24 stroke icons (paths only).
    public static readonly GrupoModulo Entrada = new("entrada", "Entrada", "grupo-entrada", "M4 4h16v6H4zM4 14h7v6H4zM15 14h5v6h-5z");
    public static readonly GrupoModulo Preparacion = new("preparacion", "Preparación", "grupo-preparacion", "M4 7a2 2 0 0 1 2-2h4l2 2h6a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z");
    public static readonly GrupoModulo CambioDomicilio = new("domicilio", "Cambio de domicilio", "grupo-domicilio", "M3 11l9-7 9 7M5 10v10h14V10");
    public static readonly GrupoModulo Subidas = new("subidas", "Subidas", "grupo-subidas", "M12 19V5M6 11l6-6 6 6");
    public static readonly GrupoModulo Cierre = new("cierre", "Cierre", "grupo-cierre", "M21 8l-9-5-9 5 9 5zM3 8v8l9 5 9-5V8");

    public static readonly IReadOnlyList<GrupoModulo> Grupos = [Entrada, Preparacion, CambioDomicilio, Subidas, Cierre];

    public static readonly IReadOnlyList<Modulo> Todos =
    [
        Tray(Entrada, Bandeja.PorClasificar, "Por clasificar", "PC"),
        new(ClaveDuplicados, Entrada, "Posibles duplicados", "PD", "duplicados"),
        Tray(Preparacion, Bandeja.PorPreparar, "1° Licencia", "1L"),
        Tray(Preparacion, Bandeja.SeEncuentraEnArchivos, "Se encuentra en Archivos", "AR"),
        Tray(Preparacion, Bandeja.SeEncuentraEnOf43, "Se encuentra en Of. 43", "43"),
        Tray(Preparacion, Bandeja.SinCarpetaF8, "No existe carpeta (F8)", "F8"),
        Tray(Preparacion, Bandeja.CrearCertificado, "Crear certificado / oficio", "CE"),
        Tray(Preparacion, Bandeja.CanjeLicExtranjera, "Canje lic. extranjera", "CJ"),
        Tray(CambioDomicilio, Bandeja.CambioDomicilio, "Cambio de domicilio", "CD"),
        Tray(CambioDomicilio, Bandeja.CambioDomicilioSolicitado, "Cambio dom. solicitado", "CS"),
        Tray(Subidas, Bandeja.SubidaConaset, "Subida a Conaset", "SC"),
        Tray(Subidas, Bandeja.SubidaConF8, "Subida con F8", "S8"),
        Tray(Subidas, Bandeja.SubidaConOficio, "Subida con oficio", "SO"),
        Tray(Subidas, Bandeja.CambioDomSubidoConaset, "Cambio dom. subido a Conaset", "DC"),
        Tray(Subidas, Bandeja.CambioDomSubidoCorreo, "Cambio dom. subido con correo", "DM"),
        Tray(Cierre, Bandeja.Cerradas, "Cerradas", "CE"),
        Tray(Cierre, Bandeja.Archivadas, "Archivadas", "AV"),
        new(ClaveAnuladas, Cierre, "Anuladas", "AN", "anuladas"),
        new(ClaveTodas, Cierre, "Todas las carpetas", "TO", "carpetas?bandeja=todas"),
    ];

    public static Modulo? DeBandeja(Bandeja bandeja) => Todos.FirstOrDefault(m => m.Bandeja == bandeja);

    private static Modulo Tray(GrupoModulo grupo, Bandeja bandeja, string nombre, string corto) =>
        new(bandeja.ToString(), grupo, nombre, corto, $"carpetas?bandeja={bandeja}", bandeja);
}

/// <summary>Live figures of one module as shown in cards and in the side menu.</summary>
public sealed record EstadoModulo(int Conteo, Semaforo? Semaforo, int Atrasadas, IReadOnlyList<int> Serie);

/// <summary>
/// Per-circuit panel state shared by the side menu, the home panel and the module pages. Each refresh runs in its
/// own DI scope (own DbContext) so it never collides with a page query in progress.
/// </summary>
public sealed class ModulosState(IServiceScopeFactory scopeFactory, ILogger<ModulosState> logger)
{
    private static readonly IReadOnlyList<int> SinSerie = new int[SerieSieteDias.Dias];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PanelDto? Panel { get; private set; }

    public event Action? Changed;

    public EstadoModulo De(Modulo modulo)
    {
        ArgumentNullException.ThrowIfNull(modulo);
        if (Panel is not { } p)
        {
            return new EstadoModulo(0, null, 0, SinSerie);
        }

        if (modulo.Bandeja is { } b && p.Modulos.FirstOrDefault(m => m.Bandeja == b) is { } dto)
        {
            return new EstadoModulo(dto.Conteo, dto.Semaforo, dto.Atrasadas, dto.Serie);
        }

        return modulo.Clave switch
        {
            ModulosCatalogo.ClaveDuplicados => new EstadoModulo(
                p.DuplicadosPendientes, p.DuplicadosPendientes > 0 ? Semaforo.Amarillo : Semaforo.Verde, p.DuplicadosPendientes, SinSerie),
            ModulosCatalogo.ClaveAnuladas => new EstadoModulo(p.Anuladas, null, 0, p.SerieAnuladas),
            ModulosCatalogo.ClaveTodas => new EstadoModulo(p.Modulos.Sum(m => m.Conteo), null, 0, SinSerie),
            _ => new EstadoModulo(0, null, 0, SinSerie),
        };
    }

    public EstadoModulo DeGrupo(GrupoModulo grupo)
    {
        var estados = ModulosCatalogo.Todos
            .Where(m => m.Grupo == grupo && m.Clave != ModulosCatalogo.ClaveTodas)
            .Select(De)
            .ToList();
        var peor = estados.Select(e => e.Semaforo).Where(s => s is not null).DefaultIfEmpty(null).Max();
        return new EstadoModulo(estados.Sum(e => e.Conteo), peor, estados.Sum(e => e.Atrasadas), SinSerie);
    }

    public async Task RefrescarAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            Panel = await scope.ServiceProvider.GetRequiredService<PanelService>().ObtenerAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo actualizar el panel de módulos.");
            return;
        }
        finally
        {
            _lock.Release();
        }

        Changed?.Invoke();
    }

    /// <summary>Loads once per circuit; later calls reuse the current figures.</summary>
    public Task AsegurarAsync(CancellationToken ct = default) => Panel is null ? RefrescarAsync(ct) : Task.CompletedTask;
}
