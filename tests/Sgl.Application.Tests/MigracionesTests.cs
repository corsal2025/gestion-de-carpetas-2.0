using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sgl.Infrastructure;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>H-20: las migraciones reflejan el modelo (sin el ignore de PendingModelChangesWarning) y crean los índices.</summary>
public sealed class MigracionesTests : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"sgl-migra-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var sufijo in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_ruta + sufijo); } catch { /* aún tomado por el proceso */ }
        }
    }

    [Fact]
    public void Migrate_AplicaTodoSinCambiosPendientesDelModelo_YCreaLosIndicesNuevos()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");
        using var ctx = new SglDbContext(options.Options);

        ctx.Database.Migrate(); // lanza si el modelo tiene cambios sin migración

        var indices = ctx.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index'").ToList();
        Assert.Contains("IX_Carpetas_FechaSubida", indices);
        Assert.Contains("IX_HistorialCambios_Campo_CarpetaId_Fecha", indices);
    }
}
