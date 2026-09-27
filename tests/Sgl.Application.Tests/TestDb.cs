using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>Shared in-memory SQLite database; each context/service opens over the same connection.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public FixedTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    public SglDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SglDbContext>().UseSqlite(_connection).Options);

    /// <summary>Service over a fresh context so assertions never read stale tracked entities.</summary>
    public CarpetaService NewService() => new(new CarpetaRepository(NewContext()), Clock);

    public void Dispose() => _connection.Dispose();
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
