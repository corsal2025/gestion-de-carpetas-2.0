using Microsoft.EntityFrameworkCore;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>Read side of «Nos piden carpetas» over the circuit's shared DbContext.</summary>
public sealed class EntrantesRepository(SglDbContext db) : IEntrantesRepository
{
    private static readonly EstadoCarpeta[] EstadosCerrados =
    [
        EstadoCarpeta.CambioDomSubidoConaset,
        EstadoCarpeta.SubidaConF8,
        EstadoCarpeta.SubidaConOficio,
        EstadoCarpeta.CambioDomSinRegistro,
        EstadoCarpeta.CambioDomCarpetaDigital,
    ];

    private static readonly TipoEnvioCorreoComuna[] TiposDeRespuesta =
    [
        TipoEnvioCorreoComuna.SinRegistro,
        TipoEnvioCorreoComuna.Digital,
        TipoEnvioCorreoComuna.ConfirmacionSubida,
    ];

    private sealed record Fila(
        Guid Id, string Rut, string Nombre, string Apellido, string? Comuna, Sede Sede, DateOnly FechaCitacion, string? FechaUltimaCarpeta,
        EstadoCarpeta Estado, bool PendienteBusqueda, bool CarpetaEncontrada, DateOnly? FechaSubida, DateOnly? FechaCierre, string? IdoneidadMoral);

    public async Task<IReadOnlyList<EntranteFila>> ListarAbiertasAsync(CancellationToken ct = default) =>
        await ConvertirAsync(
            await Proyectar(Entrantes().Where(c => c.Estado == EstadoCarpeta.CambioDomicilio && c.Decision == Decision.Pendiente)
                .OrderBy(c => c.FechaCitacion).ThenBy(c => c.Ciudadano.Apellido).ThenBy(c => c.Ciudadano.Nombre).ThenBy(c => c.Id)).ToListAsync(ct),
            ct);

    public async Task<IReadOnlyList<EntranteFila>> ListarCerradasAsync(int max, CancellationToken ct = default) =>
        await ConvertirAsync(
            await Proyectar(Entrantes().Where(c => EstadosCerrados.Contains(c.Estado))
                .OrderByDescending(c => c.FechaEstadoActual).ThenBy(c => c.Id).Take(Math.Max(1, max))).ToListAsync(ct),
            ct);

    public Task<int> ContarCerradasAsync(CancellationToken ct = default) =>
        Entrantes().CountAsync(c => EstadosCerrados.Contains(c.Estado), ct);

    public async Task<EntranteFila?> ObtenerAsync(Guid id, CancellationToken ct = default) =>
        (await ConvertirAsync(await Proyectar(Entrantes().Where(c => c.Id == id)).ToListAsync(ct), ct)).SingleOrDefault();

    public async Task<IReadOnlyList<DescartadoDto>> ListarDescartadosAsync(CancellationToken ct = default) =>
        await db.CorreosDescartados.AsNoTracking()
            .OrderByDescending(d => d.DescartadoEn).ThenByDescending(d => d.Id)
            .Select(d => new DescartadoDto(d.Id, d.Asunto, d.Remitente, d.RecibidoEn, d.Motivo, d.DescartadoEn))
            .ToListAsync(ct);

    public Task<int> ContarDescartadosAsync(CancellationToken ct = default) => db.CorreosDescartados.CountAsync(ct);

    public async Task<bool> QuitarDescartadoAsync(long id, CancellationToken ct = default) =>
        await db.CorreosDescartados.Where(d => d.Id == id).ExecuteDeleteAsync(ct) > 0;

    private IQueryable<Carpeta> Entrantes() =>
        db.Carpetas.AsNoTracking().Where(c => !c.Anulada && c.SentidoCambioDomicilio == SentidoCambioDomicilio.Entrante && !c.PendienteReclasificacion);

    private static IQueryable<Fila> Proyectar(IQueryable<Carpeta> consulta) =>
        consulta.Select(c => new Fila(
            c.Id, c.Ciudadano.Rut.Value, c.Ciudadano.Nombre, c.Ciudadano.Apellido, c.Comuna, c.Sede, c.FechaCitacion,
            c.FechaUltimaCarpeta, c.Estado, c.PendienteBusqueda, c.CarpetaEncontrada, c.FechaSubida, c.FechaCierre, c.IdoneidadMoral));

    private async Task<IReadOnlyList<EntranteFila>> ConvertirAsync(IReadOnlyList<Fila> filas, CancellationToken ct)
    {
        if (filas.Count == 0)
        {
            return [];
        }

        var ids = filas.Select(f => f.Id).ToList();
        var envios = await (from l in db.EnviosCorreoComunaCarpetas.AsNoTracking()
                            join e in db.EnviosCorreoComuna.AsNoTracking() on l.EnvioId equals e.Id
                            where ids.Contains(l.CarpetaId) && TiposDeRespuesta.Contains(e.Tipo) && e.Estado == EstadoEnvioCorreoComuna.Enviado
                            select new { l.CarpetaId, e.Tipo, e.EnviadoEn, e.EsPrueba })
            .ToListAsync(ct);
        var ultimo = envios.GroupBy(e => e.CarpetaId).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.EnviadoEn).First());

        return filas.Select(f =>
        {
            ultimo.TryGetValue(f.Id, out var correo);
            return new EntranteFila(
                f.Id,
                Rut.TryParse(f.Rut, out var rut) ? rut!.Formatted : f.Rut,
                $"{f.Nombre} {f.Apellido}".Trim(),
                f.Comuna,
                f.Sede,
                f.FechaCitacion,
                f.FechaUltimaCarpeta,
                f.Estado,
                f.PendienteBusqueda,
                f.CarpetaEncontrada,
                f.FechaSubida,
                f.FechaCierre,
                f.IdoneidadMoral == Carpeta.ObservacionRevisarNombreCorreo,
                correo?.Tipo,
                correo?.EnviadoEn,
                correo?.EsPrueba ?? false);
        }).ToList();
    }
}
