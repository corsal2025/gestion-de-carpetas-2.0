using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sgl.Infrastructure.Persistence;

/// <summary>
/// Al abrir cada conexión SQLite: WAL (lectores y un escritor no se bloquean entre sí, clave con el
/// módulo Peticiones y el respaldo escribiendo sobre la misma base), espera de hasta 5 s ante un
/// bloqueo en vez de fallar con "database is locked", y sincronía NORMAL (segura con WAL).
/// </summary>
public sealed class SqlitePragmasInterceptor : DbConnectionInterceptor
{
    private const string Pragmas = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Aplicar(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragmas;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void Aplicar(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragmas;
        cmd.ExecuteNonQuery();
    }
}
