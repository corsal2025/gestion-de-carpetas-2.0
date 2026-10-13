using Microsoft.EntityFrameworkCore;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>
/// Idempotent repair of a legacy Excel artefact: a comuna name stored in <c>FechaUltimaCarpeta</c> of
/// change-of-address carpetas. The value moves to <c>Comuna</c> (official spelling) only when it is a
/// recognized comuna and <c>Comuna</c> is empty; anything else is left untouched.
/// </summary>
public static class LegacyComunaRepair
{
    /// <summary>User shown in the import history for the automatic repair (a system job, not a person).</summary>
    public const string UsuarioRegistro = "Sistema · Reparación de comunas";

    private static readonly EstadoCarpeta[] EstadosCambioDomicilio =
        Enum.GetValues<EstadoCarpeta>().Where(Carpeta.EsCambioDomicilio).ToArray();

    private static IQueryable<Carpeta> Candidatas(SglDbContext db) =>
        db.Carpetas.IgnoreQueryFilters().Where(c =>
            c.FechaUltimaCarpeta != null
            && c.FechaUltimaCarpeta != ""
            && !EF.Functions.Like(c.FechaUltimaCarpeta, "____-__-__")
            && (c.Comuna == null || c.Comuna == "")
            && EstadosCambioDomicilio.Contains(c.Estado));

    /// <summary>Distinct legacy values that would be moved (to decide whether a backup is needed).</summary>
    public static async Task<int> PendientesAsync(SglDbContext db, CancellationToken ct = default)
    {
        var valores = await Candidatas(db).Select(c => c.FechaUltimaCarpeta!).Distinct().ToListAsync(ct);
        var total = 0;
        foreach (var v in valores)
        {
            if (ComunasChile.Normalizar(v) is null) continue;
            total += await Candidatas(db).CountAsync(c => c.FechaUltimaCarpeta == v, ct);
        }

        return total;
    }

    /// <returns>Number of carpetas repaired.</returns>
    public static async Task<int> RepararAsync(SglDbContext db, CancellationToken ct = default)
    {
        var valores = await Candidatas(db).Select(c => c.FechaUltimaCarpeta!).Distinct().ToListAsync(ct);
        var total = 0;
        foreach (var v in valores)
        {
            var comuna = ComunasChile.Normalizar(v);
            if (comuna is null) continue;
            total += await Candidatas(db)
                .Where(c => c.FechaUltimaCarpeta == v)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Comuna, comuna).SetProperty(c => c.FechaUltimaCarpeta, (string?)null), ct);
        }

        return total;
    }

    /// <summary>
    /// Repairs and leaves one summary row in <c>ImportacionLogs</c> (counts only, no personal data):
    /// rows read = candidates, updated = repaired, errors = candidates left untouched (unrecognized value).
    /// Writes nothing when no carpeta was repaired, so a clean database never grows log noise.
    /// </summary>
    public static async Task<int> RepararYRegistrarAsync(SglDbContext db, DateTime ahoraUtc, CancellationToken ct = default)
    {
        var candidatas = await Candidatas(db).CountAsync(ct);
        var reparadas = await RepararAsync(db, ct);
        if (reparadas > 0)
        {
            db.ImportacionLogs.Add(new ImportacionLog(ahoraUtc, UsuarioRegistro, candidatas, 0, reparadas, Math.Max(0, candidatas - reparadas)));
            await db.SaveChangesAsync(ct);
        }

        return reparadas;
    }
}
