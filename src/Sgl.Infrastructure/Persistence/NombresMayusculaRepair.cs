using Microsoft.EntityFrameworkCore;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>
/// Idempotent repair of legacy citizen names: uppercase (accents kept) and the "Vecino" surname placeholder of the
/// first Excel importer split into real surnames. Done in memory because SQLite's upper() ignores accented letters.
/// </summary>
public static class NombresMayusculaRepair
{
    private const int TamanoLote = 500;

    public static async Task<int> PendientesAsync(SglDbContext db, CancellationToken ct = default) =>
        (await IdsPendientesAsync(db, ct)).Count;

    /// <returns>Number of citizens repaired.</returns>
    public static async Task<int> RepararAsync(SglDbContext db, CancellationToken ct = default)
    {
        var ids = await IdsPendientesAsync(db, ct);
        var total = 0;
        foreach (var lote in ids.Chunk(TamanoLote))
        {
            var ciudadanos = await db.Ciudadanos.Where(c => lote.Contains(c.Id)).ToListAsync(ct);
            total += ciudadanos.Count(c => c.NormalizarNombre());
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return total;
    }

    private static async Task<List<Guid>> IdsPendientesAsync(SglDbContext db, CancellationToken ct)
    {
        var filas = await db.Ciudadanos.AsNoTracking()
            .Select(c => new { c.Id, c.Nombre, c.Apellido })
            .ToListAsync(ct);
        return filas.Where(f => Ciudadano.RequiereNormalizar(f.Nombre, f.Apellido)).Select(f => f.Id).ToList();
    }
}
