using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sgl.Infrastructure;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>H-08: cada conexión SQLite del sistema abre en WAL, con espera ante bloqueos y sincronía NORMAL.</summary>
public sealed class SqlitePragmasInterceptorTests : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"sgl-pragmas-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var sufijo in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_ruta + sufijo); } catch { /* aún tomado por el proceso */ }
        }
    }

    private static string Pragma(SglDbContext ctx, string nombre)
    {
        ctx.Database.OpenConnection(); // por EF: dispara los interceptores de conexión
        using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"PRAGMA {nombre};";
        return Convert.ToString(cmd.ExecuteScalar())!;
    }

    [Fact]
    public void ConexionesSqlite_AbrenEnWalConBusyTimeoutYSynchronousNormal()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");

        using var ctx = new SglDbContext(options.Options);

        Assert.Equal("wal", Pragma(ctx, "journal_mode"), ignoreCase: true);
        Assert.Equal("5000", Pragma(ctx, "busy_timeout"));
        Assert.Equal("1", Pragma(ctx, "synchronous")); // 1 = NORMAL
    }
}
