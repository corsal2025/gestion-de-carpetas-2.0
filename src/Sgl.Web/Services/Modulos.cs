using Sgl.Application;
using Sgl.Application.Carpetas;
using Sgl.Application.Integridad;
using Sgl.Application.Panel;
using Sgl.Application.Solicitudes;
using Sgl.Domain;

namespace Sgl.Web.Services;

/// <summary>Work-flow group of modules; drives the group color in the home panel and the side menu.</summary>
public sealed record GrupoModulo(string Clave, string Nombre, string Css, string Icono);

/// <summary>One navigable module: a tray of /carpetas, or a dedicated page (cancelled, all).</summary>
public sealed record Modulo(string Clave, GrupoModulo Grupo, string Nombre, string Corto, string Href, Bandeja? Bandeja = null);

/// <summary>Single catalog of modules shared by the home panel and the side menu (R4: one source of truth).</summary>
public static class ModulosCatalogo
{
    public const string ClaveAnuladas = "anuladas";
    public const string ClaveTodas = "todas";
    public const string ClaveIntegridad = "integridad";
    public const string ClavePeticiones = "peticiones";
    public const string ClaveF8 = "f8";
    public const string ClaveCertificados = "certificados";
    public const string ClaveFirmantes = "firmantes";

    /// <summary>
    /// CSS class of a module's menu circle ("m-..."): every module has its own colour (see modulos.css) and all
    /// circles share one size. Trays use their tray name; the other pages use their key.
    /// </summary>
    public static string ClaseColor(Modulo m) => m.Bandeja is { } b
        ? $"m-{b.ToString().ToLowerInvariant()}"
        : $"m-{m.Clave}";

    // Simple 24x24 stroke icons (paths only).
    public static readonly GrupoModulo Entrada = new("entrada", "Peticiones entrantes", "grupo-entrada", "M4 4h16v6H4zM4 14h7v6H4zM15 14h5v6h-5z");
    public static readonly GrupoModulo Revision = new("revision", "Revisión y corrección de errores", "grupo-revision", "M9 11l2 2 4-4M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z");
    public static readonly GrupoModulo Preparacion = new("preparacion", "Tramitaciones de licencias", "grupo-preparacion", "M4 7a2 2 0 0 1 2-2h4l2 2h6a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z");
    public static readonly GrupoModulo CambioDomicilio = new("domicilio", "Solicitudes de cambio de domicilio", "grupo-domicilio", "M3 11l9-7 9 7M5 10v10h14V10");
    public static readonly GrupoModulo Subidas = new("subidas", "Licencias ya subidas a Conaset", "grupo-subidas", "M12 19V5M6 11l6-6 6 6");
    public static readonly GrupoModulo Cierre = new("cierre", "Cierre", "grupo-cierre", "M21 8l-9-5-9 5 9 5zM3 8v8l9 5 9-5V8");

    public static readonly GrupoModulo Sistema = new("sistema", "Control", "grupo-sistema", "M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z M9 12l2 2 4-4");

    public static readonly GrupoModulo Solicitudes = new("solicitudes", "Solicitudes especiales", "grupo-solicitudes", "M7 3h8l4 4v14H7zM15 3v4h4M10 12h6M10 16h6");

    public static readonly IReadOnlyList<GrupoModulo> Grupos = [Entrada, Revision, Preparacion, CambioDomicilio, Solicitudes, Subidas, Cierre, Sistema];

    public static readonly IReadOnlyList<Modulo> Todos =
    [
        Tray(Entrada, Bandeja.PorClasificar, "Licencias para clasificar", "PC"),
        // REVISIÓN: one submodule per problem, in precedence order (RevisionEntrada.Submodulos); Citadas is the old "Para revisión".
        Tray(Revision, Bandeja.RevisionDuplicadas, "Duplicadas", "DU"),
        Tray(Revision, Bandeja.RevisionRutInvalido, "RUT inválido", "RI"),
        Tray(Revision, Bandeja.RevisionSinRut, "Sin RUT", "SR"),
        Tray(Revision, Bandeja.RevisionNoIdentificada, "Licencia no identificada", "NI"),
        Tray(Revision, Bandeja.RevisionNoEvaluadas, "No evaluadas", "NE"),
        Tray(Revision, Bandeja.ParaRevision, "Sin término", "PR"),
        Tray(Preparacion, Bandeja.PorPreparar, "1° Licencia", "1L"),
        Tray(Preparacion, Bandeja.SeEncuentraEnArchivos, "Se encuentra en Archivos", "AR"),
        Tray(Preparacion, Bandeja.SeEncuentraEnOf43, "Se encuentra en Of. 43", "43"),
        Tray(Preparacion, Bandeja.SinCarpetaF8, "No existe carpeta (F8)", "F8"),
        Tray(Preparacion, Bandeja.CrearCertificado, "Crear certificado / oficio", "CE"),
        Tray(Preparacion, Bandeja.CanjeLicExtranjera, "Canje lic. extranjera", "CJ"),
        Tray(CambioDomicilio, Bandeja.CambioDomicilio, "Cambios de domicilio que nos solicitan por correo", "CD"),
        new(ClavePeticiones, CambioDomicilio, "Peticiones de cambio de domicilio a otras comunas", "PE", "peticiones"),
        Tray(CambioDomicilio, Bandeja.CambioDomicilioSolicitado, "Cambio dom. solicitado", "CS"),
        new(ClaveCertificados, Solicitudes, "Certificado / Oficio", "CF", "certificados"),
        new("peticiones-comunas", Sistema, "Comunas y correos", "CO", "peticiones/Comunas"),
        new("peticiones-estadisticas", Sistema, "Estadísticas de peticiones", "EP", "peticiones/Estadisticas"),
        new("peticiones-configuracion", Sistema, "Configuración de correo", "CC", "peticiones/Configuracion"),
        Tray(Subidas, Bandeja.SubidaConaset, "Subida a Conaset", "SC"),
        Tray(Subidas, Bandeja.SubidaConF8, "Subida con F8", "S8"),
        Tray(Subidas, Bandeja.SubidaConOficio, "Subida con oficio", "SO"),
        Tray(Subidas, Bandeja.CambioDomSubidoConaset, "Cambio dom. subido a Conaset", "DC"),
        Tray(Subidas, Bandeja.CambioDomSubidoCorreo, "Cambio dom. subido con correo", "DM"),
        Tray(Cierre, Bandeja.Cerradas, "Cerradas", "CE"),
        Tray(Cierre, Bandeja.Archivadas, "Archivadas", "AV"),
        new(ClaveAnuladas, Cierre, "Anuladas", "AN", "anuladas"),
        new(ClaveIntegridad, Sistema, "Integridad", "IN", "integridad"),
        new(ClaveFirmantes, Sistema, "Firmantes", "FI", "configuracion/firmantes"),
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

    public PanelDto? Panel { get; private set; }

    /// <summary>Last integrity watchdog run (null until one exists).</summary>
    public CorridaIntegridadDto? Integridad { get; private set; }

    /// <summary>Open requests of the SOLICITUDES group (badges of F8 and Certificado / Oficio).</summary>
    public ContadoresSolicitudes Solicitudes { get; private set; } = ContadoresSolicitudes.Vacio;

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
            ModulosCatalogo.ClaveAnuladas => new EstadoModulo(p.Anuladas, null, 0, p.SerieAnuladas),
            ModulosCatalogo.ClaveIntegridad => new EstadoModulo(
                Integridad?.Problemas ?? 0,
                Integridad is { TieneProblemas: true } ? Semaforo.Rojo : Semaforo.Verde,
                Integridad?.Problemas ?? 0,
                SinSerie),
            ModulosCatalogo.ClaveF8 => new EstadoModulo(Solicitudes.F8Solicitados, null, 0, SinSerie),
            ModulosCatalogo.ClaveCertificados => new EstadoModulo(Solicitudes.CertificadosPorFirmar, null, 0, SinSerie),
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

    private RefrescoCoalescido? _refresco;

    /// <summary>
    /// Reloads the figures. Bursts (navigation + mutation + notifier) are coalesced: at most one extra run, and a
    /// request arriving at the very end is never lost (see <see cref="RefrescoCoalescido"/>).
    /// </summary>
    public Task RefrescarAsync(CancellationToken ct = default)
    {
        _refresco ??= new RefrescoCoalescido(CargarAsync);
        return _refresco.SolicitarAsync();
    }

    private async Task CargarAsync()
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            Panel = await scope.ServiceProvider.GetRequiredService<PanelService>().ObtenerAsync();
            Integridad = await scope.ServiceProvider.GetRequiredService<IntegridadService>().UltimaAsync();
            Solicitudes = ContadoresSolicitudes.Desde(
                await scope.ServiceProvider.GetRequiredService<SolicitudesF8Service>().ContarPorEstadoAsync(),
                await scope.ServiceProvider.GetRequiredService<CertificadosService>().ContarPorEstadoAsync());
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo actualizar el panel de módulos.");
        }
    }

    /// <summary>Loads once per circuit; later calls reuse the current figures.</summary>
    public Task AsegurarAsync(CancellationToken ct = default) => Panel is null ? RefrescarAsync(ct) : Task.CompletedTask;
}
