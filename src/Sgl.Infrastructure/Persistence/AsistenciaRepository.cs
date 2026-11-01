using Microsoft.EntityFrameworkCore;
using Sgl.Application.Asistencias;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>Attendance record over the circuit's shared DbContext (one unit of work with the carpeta repository).</summary>
public sealed class AsistenciaRepository(SglDbContext db) : IAsistenciaRepository
{
    public Task<Asistencia?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Asistencias.SingleOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(Asistencia asistencia, CancellationToken ct = default) =>
        await db.Asistencias.AddAsync(asistencia, ct);

    public async Task<IReadOnlyList<Asistencia>> GetPorAgregarVigentesAsync(string rut, CancellationToken ct = default) =>
        await db.Asistencias.Where(a => a.Rut == rut && !a.Anulada && a.Tipo == TipoAsistencia.PorAgregar).ToListAsync(ct);

    public async Task<ContextoRutDto> GetContextoAsync(string rut, DateOnly hoy, CancellationToken ct = default)
    {
        var ciudadano = await db.Ciudadanos.AsNoTracking()
            .Where(c => c.Rut.Value == rut)
            .Select(c => new { c.Id, c.Nombre, c.Apellido })
            .FirstOrDefaultAsync(ct);
        var nombre = ciudadano is null ? null : $"{ciudadano.Nombre} {ciudadano.Apellido}".Trim();

        CitaHoyDto? cita = null;
        if (ciudadano is not null)
        {
            var c = await db.Carpetas.AsNoTracking()
                .Where(x => x.CiudadanoId == ciudadano.Id && x.FechaCitacion == hoy && !x.EsExtra)
                .OrderBy(x => x.HoraCita)
                .ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.HoraCita, x.Sede, x.TipoTramite, x.TramiteAgenda, x.Estado, x.Decision, x.CajaArchivo, Pedida = x.FechaPeticion != null })
                .FirstOrDefaultAsync(ct);
            if (c is not null)
            {
                cita = new CitaHoyDto(c.Id, c.HoraCita, c.Sede, c.TipoTramite ?? TextoTramite(c.TramiteAgenda),
                    Carpeta.ClasificarBandeja(c.Estado, c.Decision, c.CajaArchivo, c.Pedida));
            }
        }

        var filas = await db.Asistencias.AsNoTracking()
            .Where(a => a.Rut == rut && !a.Anulada && (a.Tipo == TipoAsistencia.PorAgregar || a.Fecha == hoy))
            .ToListAsync(ct);
        var marcada = filas.FirstOrDefault(a => a.Tipo != TipoAsistencia.PorAgregar);
        var pendiente = filas.Where(a => a.Tipo == TipoAsistencia.PorAgregar).OrderByDescending(a => a.RegistradaEn).FirstOrDefault();
        return new ContextoRutDto(
            nombre,
            ciudadano is not null,
            cita,
            marcada is null ? null : Dto(marcada, nombre),
            pendiente is null ? null : Dto(pendiente, nombre));
    }

    public async Task<ResumenAsistenciaDto> GetResumenAsync(DateOnly hoy, CancellationToken ct = default)
    {
        // Cancelled carpetas are out of the Carpetas set (soft-delete filter): "agendadas" never counts them.
        var agendadas = await db.Carpetas.AsNoTracking().CountAsync(c => c.FechaCitacion == hoy && !c.EsExtra, ct);

        var filas = await (
            from a in db.Asistencias.AsNoTracking()
            where !a.Anulada && (a.Tipo == TipoAsistencia.PorAgregar || a.Fecha == hoy)
            join c in db.Ciudadanos.AsNoTracking() on a.Rut equals c.Rut.Value into g
            from c in g.DefaultIfEmpty()
            orderby a.RegistradaEn descending, a.Id
            select new { Fila = a, Nombre = c == null ? null : c.Nombre + " " + c.Apellido })
            .ToListAsync(ct);

        var marcados = filas.Where(f => f.Fila.Tipo != TipoAsistencia.PorAgregar).Select(f => Dto(f.Fila, f.Nombre)).ToList();
        var porAgregar = filas.Where(f => f.Fila.Tipo == TipoAsistencia.PorAgregar).Select(f => Dto(f.Fila, f.Nombre)).ToList();
        return new ResumenAsistenciaDto(
            new ContadoresAsistenciaDto(
                agendadas,
                marcados.Count(m => m.Tipo == TipoAsistencia.Vino),
                marcados.Count(m => m.Tipo == TipoAsistencia.Extra)),
            marcados,
            porAgregar);
    }

    private static AsistenciaDto Dto(Asistencia a, string? nombre) =>
        new(a.Id, a.Tipo, Rut.TryParse(a.Rut, out var rut) ? rut!.Formatted : a.Rut, nombre, a.Sede, a.Fecha,
            HoraChile.ALocal(a.RegistradaEn), a.Autor, a.CarpetaId);

    private static string? TextoTramite(TramiteAgenda? tramite) => tramite switch
    {
        TramiteAgenda.Renovacion => "Renovación",
        TramiteAgenda.PrimeraVezExtension => "Primera vez, Extensión",
        TramiteAgenda.CambioDomicilio => "Cambio de domicilio",
        TramiteAgenda.Canje => "Canje",
        _ => null,
    };
}
