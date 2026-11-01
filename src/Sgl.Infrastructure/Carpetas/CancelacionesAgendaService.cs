using Microsoft.EntityFrameworkCore;
using Sgl.Application;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Infrastructure.Carpetas;

public sealed record CancelacionAgendaPendiente(Guid Id, string Nombre, string Rut, Sede Sede, DateOnly FechaCita, EstadoCarpeta Estado);

/// <summary>Explicit review of agenda cancellations where existing work must be preserved.</summary>
public sealed class CancelacionesAgendaService(SglDbContext db, UnidadDeTrabajoGate gate, TimeProvider clock)
{
    private IQueryable<Carpeta> Pendientes() => db.Carpetas.Where(c => db.Historial.Any(h => h.CarpetaId == c.Id && h.Campo == "CitaCancelada"
        && !db.Historial.Any(r => r.CarpetaId == c.Id && r.Campo == "CitaCanceladaResuelta" && r.Id > h.Id
            && !db.Historial.Any(a => a.CarpetaId == c.Id && a.Campo == "Anulacion" && a.Nuevo == "Restaurada" && a.Id > r.Id))));

    public Task<int> ContarAsync(CancellationToken ct = default) => gate.LeerAsync(() => Pendientes().CountAsync(ct), ct);

    public Task<List<CancelacionAgendaPendiente>> ListarAsync(CancellationToken ct = default) => gate.LeerAsync(() =>
        Pendientes().AsNoTracking().OrderBy(c => c.FechaCitacion).ThenBy(c => c.Id)
        .Select(c => new CancelacionAgendaPendiente(c.Id, c.Ciudadano.Nombre + " " + c.Ciudadano.Apellido,
            c.Ciudadano.Rut.Value, c.Sede, c.FechaCitacion, c.Estado)).ToListAsync(ct), ct);

    public async Task ResolverAsync(Guid id, bool anular, string motivo, string autor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > MotivosAnulacion.MaxLength)
            throw new DomainException("Indique un motivo de revisión de hasta 100 caracteres.");
        await gate.WaitAsync(ct);
        try
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var carpeta = await Pendientes().Include(c => c.Historial).SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new DomainException("La cancelación ya fue revisada o la carpeta ya no está activa. Actualice la lista.");
            var now = clock.GetUtcNow().UtcDateTime;
            carpeta.RegistrarEvento("CitaCanceladaResuelta", null, $"{(anular ? "Anulada" : "Conservada activa")}: {motivo.Trim()}", autor, now);
            if (anular)
            {
                var razon = $"Cancelación agenda: {motivo.Trim()}";
                carpeta.Anular(razon[..Math.Min(MotivosAnulacion.MaxLength, razon.Length)], autor, now);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch { db.ChangeTracker.Clear(); throw; }
        finally { gate.Release(); }
    }
}
