using Microsoft.EntityFrameworkCore;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>One Entrada carpeta with its base tray (by state) and its final tray (Entrantes or a REVISIÓN submodule).</summary>
internal sealed record CarpetaEntradaClasificada(Guid Id, DateOnly FechaCitacion, Bandeja BandejaBase, Bandeja Bandeja);

/// <summary>
/// Reads the signals of the Entrada carpetas in SQL (one round-trip per base tray) and applies
/// <see cref="RevisionEntrada.Clasificar"/> in memory. The duplicate rule is the one of the red alert in Por clasificar:
/// another open, non-archived carpeta of the same citizen, unless a "mantener ambas" review is newer than all of them.
/// </summary>
internal static class RevisionEntradaQuery
{
    public static async Task<IReadOnlyList<CarpetaEntradaClasificada>> ClasificarAsync(
        SglDbContext db, IQueryable<Carpeta> filtrada, DateOnly hoy, CancellationToken ct)
    {
        var resultado = new List<CarpetaEntradaClasificada>();
        foreach (var bandejaBase in new[] { Bandeja.PorClasificar, Bandeja.ParaRevision })
        {
            var abiertas = db.Carpetas.AsNoTracking().Where(Carpeta.AbiertaSinArchivarExpresion);
            var filas = await filtrada
                .Where(Carpeta.EnBandeja(bandejaBase))
                .Select(c => new
                {
                    c.Id,
                    c.FechaCitacion,
                    c.TramiteAgenda,
                    Rut = c.Ciudadano.Rut.Value,
                    OtraAbierta = abiertas.Any(o => o.CiudadanoId == c.CiudadanoId && o.Id != c.Id),
                    UltimoRegistro = abiertas.Where(o => o.CiudadanoId == c.CiudadanoId).Max(o => (DateTime?)o.FechaRegistro),
                    RevisadoEn = db.RevisionesDuplicado.Where(r => r.CiudadanoId == c.CiudadanoId).Max(r => (DateTime?)r.RevisadoEn),
                })
                .ToListAsync(ct);

            resultado.AddRange(filas.Select(f =>
            {
                var revisada = f.RevisadoEn is { } r && f.UltimoRegistro is { } u && u <= r;
                var senales = new SenalesRevision(f.OtraAbierta && !revisada, f.Rut, f.TramiteAgenda, f.FechaCitacion);
                return new CarpetaEntradaClasificada(f.Id, f.FechaCitacion, bandejaBase, RevisionEntrada.Clasificar(bandejaBase, senales, hoy));
            }));
        }

        return resultado;
    }
}
