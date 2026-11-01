using Microsoft.EntityFrameworkCore;
using Sgl.Application;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Infrastructure.Carpetas;

public sealed record RespaldoPendienteDto(Guid Id, string Rut, string Nombre, Sede Sede, DateOnly? FechaPenultima, DateTime AgregadoEn);

/// <summary>Lista independiente de respaldos opcionales; nunca modifica el flujo del F8.</summary>
public sealed class RespaldosPenultimaService(SglDbContext db, TimeProvider clock, UnidadDeTrabajoGate gate)
{
    public Task<int> ContarPendientesAsync(CancellationToken ct = default) =>
        gate.LeerAsync(() => db.Carpetas.AsNoTracking().CountAsync(c => c.FechaRespaldoSolicitado != null && c.FechaRespaldoSubido == null, ct), ct);

    public async Task<IReadOnlyList<RespaldoPendienteDto>> ListarPendientesAsync(CancellationToken ct = default) =>
        await gate.LeerAsync(async () => await db.Carpetas.AsNoTracking()
            .Where(c => c.FechaRespaldoSolicitado != null && c.FechaRespaldoSubido == null)
            .OrderBy(c => c.FechaRespaldoSolicitado)
            .Select(c => new RespaldoPendienteDto(c.Id, c.Ciudadano.Rut.Value, c.Ciudadano.Nombre + " " + c.Ciudadano.Apellido,
                c.Sede, c.FechaPenultimaCarpeta, c.FechaRespaldoSolicitado!.Value)).ToListAsync(ct), ct);

    public async Task AgregarAsync(Guid id, string autor, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            db.ChangeTracker.Clear();
            var carpeta = await db.Carpetas.SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new DomainException("No se encontró la carpeta activa.");
            carpeta.AgregarRespaldoPenultima(autor, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
        }
        catch { db.ChangeTracker.Clear(); throw; }
        finally { gate.Release(); }
    }

    public async Task CancelarAsync(Guid id, string autor, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            db.ChangeTracker.Clear();
            var carpeta = await db.Carpetas.SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new DomainException("No se encontró la carpeta activa.");
            carpeta.CancelarRespaldoPenultima(autor, clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
        }
        catch { db.ChangeTracker.Clear(); throw; }
        finally { gate.Release(); }
    }

    public async Task MarcarSubidosAsync(IReadOnlyCollection<Guid> ids, string autor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var seleccion = ids.Distinct().ToArray();
        if (seleccion.Length == 0) throw new DomainException("Seleccione al menos un respaldo.");
        await gate.WaitAsync(ct);
        try
        {
            db.ChangeTracker.Clear();
            var carpetas = await db.Carpetas.Where(c => seleccion.Contains(c.Id)).ToListAsync(ct);
            if (carpetas.Count != seleccion.Length) throw new DomainException("Una carpeta ya no está activa o no existe.");
            foreach (var carpeta in carpetas) carpeta.ValidarRespaldoPendiente();
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var carpeta in carpetas) carpeta.MarcarRespaldoPenultimaSubido(autor, now);
            await db.SaveChangesAsync(ct);
        }
        catch { db.ChangeTracker.Clear(); throw; }
        finally { gate.Release(); }
    }
}
