using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sgl.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to generate migrations (SQLite).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SglDbContext>
{
    public SglDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, "Data Source=design-time.db");
        return new SglDbContext(options.Options);
    }
}
