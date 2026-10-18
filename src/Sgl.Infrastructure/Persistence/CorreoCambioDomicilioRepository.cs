using Microsoft.EntityFrameworkCore;
using Sgl.Application.SincronizacionCorreo;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>Idempotency records of the mail sync. Shares the scoped DbContext (one unit of work with carpetas).</summary>
public sealed class CorreoCambioDomicilioRepository(SglDbContext db) : ICorreoCambioDomicilioRepository
{
    public Task<bool> ExisteAsync(string messageId, Rut rut, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rut);
        var id = Clave(messageId);
        var valor = rut.Value;
        return db.CorreosCambioDomicilio.AnyAsync(c => c.MessageId == id && c.Rut == valor, ct);
    }

    public async Task<IReadOnlyList<Guid>> CarpetasDeMensajeAsync(string messageId, CancellationToken ct = default)
    {
        var id = Clave(messageId);
        return await db.CorreosCambioDomicilio
            .Where(c => c.MessageId == id)
            .Select(c => c.CarpetaId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> CarpetasEnCambioDomicilioAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        await db.Carpetas
            .Where(c => c.CiudadanoId == ciudadanoId
                && c.Estado == EstadoCarpeta.CambioDomicilio
                && c.Decision == Decision.Pendiente)
            .OrderByDescending(c => c.FechaRegistro)
            .Select(c => c.Id)
            .ToListAsync(ct);

    public async Task AddAsync(CorreoCambioDomicilio registro, CancellationToken ct = default) =>
        await db.CorreosCambioDomicilio.AddAsync(registro, ct);

    private static string Clave(string messageId)
    {
        var id = (messageId ?? string.Empty).Trim();
        return id.Length > CorreoCambioDomicilio.MaxMessageIdLength ? id[..CorreoCambioDomicilio.MaxMessageIdLength] : id;
    }
}
