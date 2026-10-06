using Sgl.Infrastructure.Carpetas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sgl.Application.Carpetas;
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
        services.AddScoped<CarpetaService>();
        services.AddScoped<ExcelImportService>();
        services.AddSingleton(TimeProvider.System);
        return services;
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
