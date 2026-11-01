using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Application.Otorgamiento;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>Read side of «Otorgamiento» over the circuit's shared DbContext.</summary>
public sealed class OtorgamientoRepository(SglDbContext db) : IOtorgamientoRepository
{
    public async Task<ListResult<ParaOtorgarDto>> ListarAsync(FiltroParaOtorgar filtro, int limite, CancellationToken ct = default)
    {
        var query = db.Carpetas.AsNoTracking().Where(Carpeta.ParaOtorgarExpresion);
        if (filtro.Sede is { } sede) query = query.Where(c => c.Sede == sede);
        if (filtro.SubidaDesde is { } desde) query = query.Where(c => c.FechaSubida >= desde);
        if (filtro.SubidaHasta is { } hasta) query = query.Where(c => c.FechaSubida <= hasta);

        var total = await query.CountAsync(ct);
        var filas = await query
            .OrderBy(c => c.FechaSubida == null).ThenBy(c => c.FechaSubida).ThenBy(c => c.Ciudadano.Apellido).ThenBy(c => c.Id)
            .Take(limite)
            .Select(c => new
            {
                c.Id,
                Rut = c.Ciudadano.Rut.Value,
                c.Ciudadano.Nombre,
                c.Ciudadano.Apellido,
                c.Sede,
                c.FechaCitacion,
                c.FechaSubida,
                c.Estado,
                c.TipoTramite,
                c.Resolucion,
            })
            .ToListAsync(ct);

        var items = filas
            .Select(r => new ParaOtorgarDto(
                r.Id,
                Rut.TryParse(r.Rut, out var rut) ? rut!.Formatted : r.Rut,
                $"{r.Nombre} {r.Apellido}".Trim(),
                r.Sede,
                r.FechaCitacion,
                r.FechaSubida,
                r.Estado,
                r.TipoTramite,
                r.Resolucion ?? Carpeta.ResolucionDeEstado(r.Estado)))
            .ToList();
        return new ListResult<ParaOtorgarDto>(items, total);
    }

    public Task<int> ContarAsync(CancellationToken ct = default) =>
        db.Carpetas.AsNoTracking().CountAsync(Carpeta.ParaOtorgarExpresion, ct);
}
