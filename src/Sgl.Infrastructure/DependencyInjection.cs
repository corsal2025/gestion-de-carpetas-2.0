using Sgl.Application;
using Sgl.Infrastructure.Carpetas;
using Sgl.Infrastructure.Documentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sgl.Application.Asistencias;
using Sgl.Application.CambioDomicilio;
using Sgl.Application.Carpetas;
using Sgl.Application.Estadisticas;
using Sgl.Application.Integridad;
using Sgl.Application.Panel;
using Sgl.Application.Solicitudes;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Infrastructure;

public static class DependencyInjection
{
    public const string ProviderKey = "Database:Provider";
    public const string ConnectionStringName = "Sgl";

    /// <summary>
    /// Registers persistence. The provider is chosen by <c>Database:Provider</c> (Sqlite | SqlServer);
    /// switching providers only requires changing that key and the <c>ConnectionStrings:Sgl</c> value.
    /// </summary>
    public static IServiceCollection AddSglInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration[ProviderKey] ?? DatabaseProvider.Sqlite;
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Falta la cadena de conexión 'ConnectionStrings:{ConnectionStringName}'.");

        services.AddDbContext<SglDbContext>(options => DatabaseProvider.Configure(options, provider, connectionString));
        // One gate per circuit: every service sharing the scoped DbContext serializes its reads and writes through it.
        services.AddScoped<UnidadDeTrabajoGate>();
        services.AddScoped<ICarpetaRepository, CarpetaRepository>();
        services.AddScoped<IDuplicadoRepository, DuplicadoRepository>();
        services.AddScoped<CarpetaService>();
        // "Pedir" mail to Dirección / Secretaría: no mail until the real adapter is configured.
        services.AddSingleton<INotificadorPeticion, PeticionSinCorreo>();
        services.AddScoped<DuplicadosService>();
        services.AddScoped<IAsistenciaRepository, AsistenciaRepository>();
        services.AddScoped<AsistenciaService>();
        services.AddScoped<Sgl.Application.Otorgamiento.IOtorgamientoRepository, OtorgamientoRepository>();
        services.AddScoped<Sgl.Application.Otorgamiento.OtorgamientoService>();
        // Mail sync of the change-of-address module (reader / comuna ports are registered with the Peticiones module).
        services.AddScoped<Sgl.Application.SincronizacionCorreo.ICorreoCambioDomicilioRepository, CorreoCambioDomicilioRepository>();
        // F8 and Certificado / Oficio modules. All repositories share the scoped DbContext (one unit of work).
        services.AddScoped<ISolicitudF8Repository, SolicitudF8Repository>();
        services.AddScoped<ICertificadoRepository, CertificadoRepository>();
        services.AddScoped<IFirmantesRepository, FirmantesRepository>();
        services.AddScoped<SolicitudesF8Service>();
        services.AddScoped<CertificadosService>();
        services.AddScoped<FirmantesService>();
        services.AddScoped<DocumentoCertificadoService>();
        // Documents and the mail to the Director. The mail is OFF unless Certificados:EnvioCorreoActivo is true, and
        // it honours the Peticiones test mode (Peticion:TestModeEmail).
        services.AddSingleton(new CertificadosOptions
        {
            EnvioCorreoActivo = bool.TryParse(configuration[$"{CertificadosOptions.SectionName}:EnvioCorreoActivo"], out var activo) && activo,
        });
        services.AddSingleton<IGeneradorDocumentoCertificado, GeneradorDocumentoCertificado>();
        services.AddSingleton<INotificadorCertificado, NotificadorCertificadoCorreo>();
        services.AddScoped<IPanelQuery, PanelQuery>();
        services.AddScoped<PanelService>();
        services.AddScoped<IIntegridadRepository, IntegridadRepository>();
        services.AddScoped<IntegridadService>();
        services.AddSingleton<IntegridadSolicitudes>();
        services.AddScoped<IEstadisticasQuery, EstadisticasQuery>();
        services.AddScoped<EstadisticasService>();
        services.AddSingleton(BindLicenciasOptions(configuration));
        // Mails to the comunas (change of address): the transport, the comuna directory and AppOptions come from the Peticiones module.
        // The real send is OFF unless CambioDomicilio:EnvioReal (or the switch saved in the database) says otherwise.
        services.AddSingleton(BindCambioDomicilioOptions(configuration));
        services.AddScoped<ICorreoComunas, Sgl.Infrastructure.CambioDomicilio.CorreoComunasService>();
        // «Reclasificación»: the mandatory stage before «Por pedir» / «Nos piden carpetas».
        services.AddScoped<IReclasificacionRepository, ReclasificacionRepository>();
        services.AddScoped<ReclasificacionService>();
        // «Pedimos carpetas · Por pedir»: review, preview per comuna and mass send.
        services.AddSingleton<IDirectorioComunas, Sgl.Infrastructure.CambioDomicilio.DirectorioComunasAdapter>();
        services.AddScoped<IPedidosRepository, PedidosRepository>();
        services.AddScoped<PedidosService>();
        // «Pedimos carpetas · Pantallazos pendientes y Seguimiento»: the screenshot store itself is registered by the host (it needs the content root).
        services.AddScoped<ISeguimientoRepository, SeguimientoRepository>();
        services.AddScoped<SeguimientoService>();
        // «Nos piden carpetas»: incoming requests from other comunas (classify by date, search, upload, the three exits, confirmation).
        services.AddScoped<IEntrantesRepository, EntrantesRepository>();
        services.AddScoped<EntrantesService>();
        services.AddScoped<ExcelImportService>();
        services.AddScoped<AgendaImportService>();
        services.AddSingleton<TimeProvider>(RelojChile.Instancia);
        return services;
    }

    private static CambioDomicilioOptions BindCambioDomicilioOptions(IConfiguration configuration)
    {
        string? Leer(string clave) => configuration[$"{CambioDomicilioOptions.SectionName}:{clave}"];

        int Entero(string clave, int porDefecto)
        {
            var raw = Leer(clave);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return porDefecto;
            }

            return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var valor) && valor > 0
                ? valor
                : throw new InvalidOperationException($"Valor inválido en '{CambioDomicilioOptions.SectionName}:{clave}': '{raw}'. Use un entero mayor que cero.");
        }

        var porDefecto = new CambioDomicilioOptions();
        return new CambioDomicilioOptions
        {
            EnvioReal = bool.TryParse(Leer(nameof(CambioDomicilioOptions.EnvioReal)), out var real) && real,
            PlazoSalienteDias = Entero(nameof(CambioDomicilioOptions.PlazoSalienteDias), porDefecto.PlazoSalienteDias),
            PlazoEntranteDias = Entero(nameof(CambioDomicilioOptions.PlazoEntranteDias), porDefecto.PlazoEntranteDias),
            CarpetaPantallazos = string.IsNullOrWhiteSpace(Leer(nameof(CambioDomicilioOptions.CarpetaPantallazos)))
                ? porDefecto.CarpetaPantallazos
                : Leer(nameof(CambioDomicilioOptions.CarpetaPantallazos))!.Trim(),
            PantallazoMaxKB = Entero(nameof(CambioDomicilioOptions.PantallazoMaxKB), porDefecto.PantallazoMaxKB),
        };
    }

    private static LicenciasOptions BindLicenciasOptions(IConfiguration configuration)
    {
        var raw = configuration[$"{LicenciasOptions.SectionName}:{nameof(LicenciasOptions.ClaseObligatoriaDesde)}"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new LicenciasOptions();
        }

        return DateOnly.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var desde)
            ? new LicenciasOptions { ClaseObligatoriaDesde = desde }
            : throw new InvalidOperationException($"Fecha inválida en '{LicenciasOptions.SectionName}:{nameof(LicenciasOptions.ClaseObligatoriaDesde)}': '{raw}'. Use el formato yyyy-MM-dd.");
    }
}

public static class DatabaseProvider
{
    public const string Sqlite = "Sqlite";
    public const string SqlServer = "SqlServer";

    public static void Configure(DbContextOptionsBuilder options, string provider, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = provider switch
        {
            _ when provider.Equals(Sqlite, StringComparison.OrdinalIgnoreCase) => options.UseSqlite(connectionString).AddInterceptors(new SqlitePragmasInterceptor()),
            _ when provider.Equals(SqlServer, StringComparison.OrdinalIgnoreCase) => options.UseSqlServer(connectionString),
            _ => throw new InvalidOperationException($"Proveedor de base de datos no soportado: '{provider}'. Use Sqlite o SqlServer."),
        };
    }
}
