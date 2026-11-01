using Microsoft.EntityFrameworkCore;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>Read side of «Seguimiento» and «Pantallazos pendientes» over the circuit's shared DbContext.</summary>
public sealed class SeguimientoRepository(SglDbContext db) : ISeguimientoRepository
{
    public async Task<IReadOnlyList<SolicitadaDto>> ListarSolicitadasAsync(CancellationToken ct = default)
    {
        var filas = await db.Carpetas.AsNoTracking()
            .Where(c => !c.Anulada
                        && c.Estado == EstadoCarpeta.CambioDomicilioSolicitado
                        && c.SentidoCambioDomicilio == SentidoCambioDomicilio.Saliente)
            .Select(c => new
            {
                c.Id,
                Rut = c.Ciudadano.Rut.Value,
                c.Ciudadano.Nombre,
                c.Ciudadano.Apellido,
                c.Comuna,
                c.FechaCitacion,
            })
            .ToListAsync(ct);
        if (filas.Count == 0)
        {
            return [];
        }

        var ids = filas.Select(f => f.Id).ToList();
        var envios = (await (from l in db.EnviosCorreoComunaCarpetas.AsNoTracking()
                             join e in db.EnviosCorreoComuna.AsNoTracking() on l.EnvioId equals e.Id
                             where ids.Contains(l.CarpetaId)
                                   && (e.Tipo == TipoEnvioCorreoComuna.Pedido || e.Tipo == TipoEnvioCorreoComuna.Reenvio)
                                   && e.Estado == EstadoEnvioCorreoComuna.Enviado
                             select new { l.CarpetaId, e.Id, e.EnviadoEn, e.EsPrueba, e.PantallazoArchivo })
                .ToListAsync(ct))
            .ToLookup(e => e.CarpetaId);

        return filas
            .Select(f =>
            {
                var propios = envios[f.Id].OrderBy(e => e.EnviadoEn).ToList();
                var conPantallazo = propios.LastOrDefault(e => e.PantallazoArchivo is not null);
                return new SolicitadaDto(
                    f.Id,
                    Rut.TryParse(f.Rut, out var rut) ? rut!.Formatted : f.Rut,
                    $"{f.Nombre} {f.Apellido}".Trim(),
                    f.Comuna,
                    f.FechaCitacion,
                    propios.Count,
                    propios.Count == 0 ? null : propios[^1].EnviadoEn,
                    propios.Count > 0 && propios.All(e => e.EsPrueba),
                    conPantallazo?.Id,
                    conPantallazo?.EsPrueba ?? false);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<PantallazoPendienteDto>> ListarPantallazosPendientesAsync(CancellationToken ct = default)
    {
        var envios = await SinPantallazo()
            .OrderBy(e => e.EnviadoEn).ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.Tipo, e.Comuna, e.Destinatarios, e.Asunto, e.EnviadoEn, e.EsPrueba })
            .ToListAsync(ct);
        if (envios.Count == 0)
        {
            return [];
        }

        var ids = envios.Select(e => e.Id).ToList();
        var personas = (await (from l in db.EnviosCorreoComunaCarpetas.AsNoTracking()
                               join c in db.Carpetas.AsNoTracking() on l.CarpetaId equals c.Id
                               where ids.Contains(l.EnvioId)
                               select new { l.EnvioId, c.Id, Rut = c.Ciudadano.Rut.Value, c.Ciudadano.Nombre, c.Ciudadano.Apellido })
                .ToListAsync(ct))
            .ToLookup(p => p.EnvioId);

        return envios
            .Select(e => new PantallazoPendienteDto(
                e.Id, e.Tipo, e.Comuna, e.Destinatarios, e.Asunto, e.EnviadoEn, e.EsPrueba,
                personas[e.Id]
                    .OrderBy(p => p.Apellido).ThenBy(p => p.Nombre).ThenBy(p => p.Id)
                    .Select(p => new PersonaEnvioDto(p.Id, Rut.TryParse(p.Rut, out var rut) ? rut!.Formatted : p.Rut, $"{p.Nombre} {p.Apellido}".Trim()))
                    .ToList()))
            .ToList();
    }

    public Task<int> ContarPantallazosPendientesAsync(CancellationToken ct = default) => SinPantallazo().CountAsync(ct);

    public Task<EnvioCorreoComuna?> ObtenerEnvioAsync(Guid envioId, CancellationToken ct = default) =>
        db.EnviosCorreoComuna.SingleOrDefaultAsync(e => e.Id == envioId, ct);

    public Task<string?> ArchivoPantallazoAsync(Guid envioId, CancellationToken ct = default) =>
        db.EnviosCorreoComuna.AsNoTracking().Where(e => e.Id == envioId).Select(e => e.PantallazoArchivo).SingleOrDefaultAsync(ct);

    public async Task<Guid?> EnvioConPantallazoDeCarpetaAsync(Guid carpetaId, CancellationToken ct = default)
    {
        var filas = await (from l in db.EnviosCorreoComunaCarpetas.AsNoTracking()
                           join e in db.EnviosCorreoComuna.AsNoTracking() on l.EnvioId equals e.Id
                           where l.CarpetaId == carpetaId
                                 && (e.Tipo == TipoEnvioCorreoComuna.Pedido || e.Tipo == TipoEnvioCorreoComuna.Reenvio)
                                 && e.Estado == EstadoEnvioCorreoComuna.Enviado
                                 && !e.EsPrueba
                                 && e.PantallazoArchivo != null
                           select new { e.Id, e.EnviadoEn })
            .ToListAsync(ct);
        return filas.OrderByDescending(f => f.EnviadoEn).Select(f => (Guid?)f.Id).FirstOrDefault();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public void DescartarCambios() => db.ChangeTracker.Clear();

    /// <summary>Request mails that went out (really or as a test) and have no screenshot. A simulated send has no mail to capture.</summary>
    private IQueryable<EnvioCorreoComuna> SinPantallazo() =>
        db.EnviosCorreoComuna.AsNoTracking().Where(e =>
            e.Estado == EstadoEnvioCorreoComuna.Enviado
            && e.PantallazoArchivo == null
            && (e.Tipo == TipoEnvioCorreoComuna.Pedido || e.Tipo == TipoEnvioCorreoComuna.Reenvio)
            && (e.Nota == null || !e.Nota.StartsWith(EnvioCorreoComuna.PrefijoSimulado)));
}
