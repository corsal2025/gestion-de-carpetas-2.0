using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>Shared query for a citizen's open carpetas (no final decision).</summary>
internal static class CarpetasAbiertasQuery
{
    public static async Task<IReadOnlyList<CarpetaAbiertaDto>> ForCiudadanoAsync(SglDbContext db, Guid ciudadanoId, CancellationToken ct) =>
        await db.Carpetas
            .AsNoTracking()
            .Where(c => c.CiudadanoId == ciudadanoId && c.Decision == Decision.Pendiente)
            .OrderByDescending(c => c.FechaCitacion)
            .Select(c => new CarpetaAbiertaDto(c.Id, c.Sede, c.FechaCitacion, c.Estado, c.Clases))
            .ToListAsync(ct);
}
