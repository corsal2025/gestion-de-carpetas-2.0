using Microsoft.EntityFrameworkCore;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>Read side of «Reclasificación» over the circuit's shared DbContext.</summary>
public sealed class ReclasificacionRepository(SglDbContext db) : IReclasificacionRepository
{
    public async Task<IReadOnlyList<ReclasificacionDto>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await Pendientes()
            .OrderBy(c => c.FechaEstadoActual).ThenBy(c => c.Ciudadano.Apellido).ThenBy(c => c.Ciudadano.Nombre).ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                Sentido = c.SentidoCambioDomicilio!.Value,
                Rut = c.Ciudadano.Rut.Value,
                c.Ciudadano.Nombre,
                c.Ciudadano.Apellido,
                c.Comuna,
                c.Sede,
                c.FechaCitacion,
                c.FechaEstadoActual,
                c.FechaUltimaCarpeta,
            })
            .ToListAsync(ct);

        return filas
            .Select(f => new ReclasificacionDto(
                f.Id,
                f.Sentido,
                Rut.TryParse(f.Rut, out var rut) ? rut!.Formatted : f.Rut,
                $"{f.Nombre} {f.Apellido}".Trim(),
                f.Comuna,
                f.Sede,
                f.FechaCitacion,
                HoraChile.Fecha(f.FechaEstadoActual),
                f.FechaUltimaCarpeta))
            .ToList();
    }

    public Task<int> ContarAsync(CancellationToken ct = default) => Pendientes().CountAsync(ct);

    private IQueryable<Carpeta> Pendientes() =>
        db.Carpetas.AsNoTracking().Where(c =>
            !c.Anulada
            && c.Decision == Decision.Pendiente
            && c.PendienteReclasificacion
            && c.Estado == EstadoCarpeta.CambioDomicilio
            && c.SentidoCambioDomicilio != null);
}
