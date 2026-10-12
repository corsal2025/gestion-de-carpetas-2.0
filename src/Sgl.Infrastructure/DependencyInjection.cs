using Sgl.Application;
using Sgl.Infrastructure.Carpetas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sgl.Application.Carpetas;
using Sgl.Application.Estadisticas;
using Sgl.Application.Panel;
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
        services.AddScoped<ICarpetaRepository, CarpetaRepository>();
        services.AddScoped<IDuplicadoRepository, DuplicadoRepository>();
        services.AddScoped<CarpetaService>();
        services.AddScoped<DuplicadosService>();
        services.AddScoped<IPanelQuery, PanelQuery>();
        services.AddScoped<PanelService>();
        services.AddScoped<IEstadisticasQuery, EstadisticasQuery>();
        services.AddScoped<EstadisticasService>();
        services.AddSingleton(BindLicenciasOptions(configuration));
        services.AddScoped<ExcelImportService>();
        services.AddScoped<AgendaImportService>();
        services.AddSingleton<TimeProvider>(RelojChile.Instancia);
        return services;
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
        options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        _ = provider switch
        {
            _ when provider.Equals(Sqlite, StringComparison.OrdinalIgnoreCase) => options.UseSqlite(connectionString),
            _ when provider.Equals(SqlServer, StringComparison.OrdinalIgnoreCase) => options.UseSqlServer(connectionString),
            _ => throw new InvalidOperationException($"Proveedor de base de datos no soportado: '{provider}'. Use Sqlite o SqlServer."),
        };
    }
}
