using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class EtapaBackfillSinPendientesTests
{
    private sealed class ContadorDeConsultas : DbCommandInterceptor
    {
        public List<string> Comandos { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Comandos.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Comandos.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task SinPendientes_NoRecorreElHistorialEnCadaArranque()
    {
        using var db = new TestDb();
        var contador = new ContadorDeConsultas();
        var options = new DbContextOptionsBuilder<SglDbContext>()
            .UseSqlite(db.NewContext().Database.GetDbConnection())
            .AddInterceptors(contador)
            .Options;
        using var ctx = new SglDbContext(options);

        var n = await EtapaBackfill.EjecutarAsync(ctx, DateTime.UtcNow);

        Assert.Equal(0, n);
        Assert.DoesNotContain(contador.Comandos, c => c.Contains("HistorialCambios"));
    }
}
