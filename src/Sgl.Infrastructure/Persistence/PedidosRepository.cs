using Microsoft.EntityFrameworkCore;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>Read side of «Pedimos carpetas» over the circuit's shared DbContext.</summary>
public sealed class PedidosRepository(SglDbContext db) : IPedidosRepository
{
    public async Task<IReadOnlyList<PorPedirDto>> ListarPorPedirAsync(DateOnly hoy, CancellationToken ct = default)
    {
        var filas = await PorPedir()
            .OrderBy(c => c.FechaEstadoActual).ThenBy(c => c.Ciudadano.Apellido).ThenBy(c => c.Ciudadano.Nombre).ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                Rut = c.Ciudadano.Rut.Value,
                c.Ciudadano.Nombre,
                c.Ciudadano.Apellido,
                c.Comuna,
                c.Sede,
                c.FechaCitacion,
                c.FechaEstadoActual,
                c.RevisadaEn,
                c.FechaUltimaCarpeta,
            })
            .ToListAsync(ct);

        var fallidas = await CarpetasConUltimoPedidoFallidoAsync(filas.Select(f => f.Id).ToList(), ct);
        return filas
            .Select(f =>
            {
                var clasificada = HoraChile.Fecha(f.FechaEstadoActual);
                return new PorPedirDto(
                    f.Id,
                    Rut.TryParse(f.Rut, out var rut) ? rut!.Formatted : f.Rut,
                    $"{f.Nombre} {f.Apellido}".Trim(),
                    f.Comuna,
                    f.Sede,
                    f.FechaCitacion,
                    clasificada,
                    clasificada < hoy,
                    f.RevisadaEn is not null,
                    fallidas.Contains(f.Id),
                    f.FechaUltimaCarpeta);
            })
            .ToList();
    }

    public Task<int> ContarPorPedirAsync(CancellationToken ct = default) => PorPedir().CountAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> ComunasPedidasAsync(IReadOnlyCollection<Guid> carpetaIds, CancellationToken ct = default)
    {
        var ids = carpetaIds.ToList();
        var filas = await (from l in db.EnviosCorreoComunaCarpetas.AsNoTracking()
                           join e in db.EnviosCorreoComuna.AsNoTracking() on l.EnvioId equals e.Id
                           where ids.Contains(l.CarpetaId) && e.Tipo == TipoEnvioCorreoComuna.Pedido && e.Estado == EstadoEnvioCorreoComuna.Enviado
                           select new { l.CarpetaId, e.Comuna, e.EnviadoEn })
            .ToListAsync(ct);
        return filas
            .GroupBy(f => f.CarpetaId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.EnviadoEn).First().Comuna);
    }

    private IQueryable<Carpeta> PorPedir() =>
        db.Carpetas.AsNoTracking().Where(c =>
            !c.Anulada
            && c.Decision == Decision.Pendiente
            && !c.PendienteReclasificacion
            && c.Estado == EstadoCarpeta.CambioDomicilio
            && c.SentidoCambioDomicilio == SentidoCambioDomicilio.Saliente);

    /// <summary>Carpetas whose most recent request mail failed (a later success moves the carpeta out of «Por pedir» anyway).</summary>
    private async Task<HashSet<Guid>> CarpetasConUltimoPedidoFallidoAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var lista = ids.ToList();
        var filas = await (from l in db.EnviosCorreoComunaCarpetas.AsNoTracking()
                           join e in db.EnviosCorreoComuna.AsNoTracking() on l.EnvioId equals e.Id
                           where lista.Contains(l.CarpetaId) && e.Tipo == TipoEnvioCorreoComuna.Pedido
                           select new { l.CarpetaId, e.Estado, e.EnviadoEn })
            .ToListAsync(ct);
        return filas
            .GroupBy(f => f.CarpetaId)
            .Where(g => g.OrderByDescending(f => f.EnviadoEn).First().Estado == EstadoEnvioCorreoComuna.Fallido)
            .Select(g => g.Key)
            .ToHashSet();
    }
}
