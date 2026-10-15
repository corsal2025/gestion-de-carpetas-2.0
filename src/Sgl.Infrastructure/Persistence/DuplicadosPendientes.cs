using Microsoft.EntityFrameworkCore;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>
/// Single definition of "possible duplicate pending review": a citizen with more than one open carpeta and no
/// review recorded at or after the latest open carpeta's registration. Shared by the duplicates list and the
/// trays so they can never disagree.
/// </summary>
internal static class DuplicadosPendientes
{
    /// <summary>Open carpetas grouped by citizen with the data both consumers need.</summary>
    internal static IQueryable<Grupo> Grupos(SglDbContext db)
    {
        var grupos = db.Carpetas.AsNoTracking()
            .Where(c => c.Decision == Decision.Pendiente)
            .GroupBy(c => c.CiudadanoId)
            .Where(g => g.Count() > 1)
            .Select(g => new Grupo
            {
                CiudadanoId = g.Key,
                Cantidad = g.Count(),
                UltimoRegistro = g.Max(c => c.FechaRegistro),
                PrimeraCita = g.Min(c => c.FechaCitacion),
                UltimaCita = g.Max(c => c.FechaCitacion),
                MismaSede = g.Min(c => c.Sede) == g.Max(c => c.Sede),
            });

        // The join over the aggregated subquery keeps the review check out of the aggregate (SQLite rejects it otherwise).
        return from g in grupos
               join ci in db.Ciudadanos.AsNoTracking() on g.CiudadanoId equals ci.Id
               where !db.RevisionesDuplicado.Any(r => r.CiudadanoId == g.CiudadanoId && r.RevisadoEn >= g.UltimoRegistro)
               select g;
    }

    /// <summary>Ids of citizens that are possible duplicates awaiting review.</summary>
    internal static IQueryable<Guid> CiudadanoIds(SglDbContext db) => Grupos(db).Select(g => g.CiudadanoId);

    internal sealed class Grupo
    {
        public Guid CiudadanoId { get; init; }
        public int Cantidad { get; init; }
        public DateTime UltimoRegistro { get; init; }
        public DateOnly PrimeraCita { get; init; }
        public DateOnly UltimaCita { get; init; }
        public bool MismaSede { get; init; }
    }
}
