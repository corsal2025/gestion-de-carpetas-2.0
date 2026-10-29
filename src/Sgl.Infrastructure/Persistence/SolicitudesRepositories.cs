using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sgl.Application.Solicitudes;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

/// <summary>Key/value row of the Configuracion table (editable settings stored in the database).</summary>
public sealed class ConfiguracionItem
{
    public const int MaxClaveLength = 100;
    public const int MaxValorLength = 500;

    public string Clave { get; set; } = string.Empty;

    public string Valor { get; set; } = string.Empty;
}

public sealed class SolicitudF8Repository(SglDbContext db) : ISolicitudF8Repository
{
    public Task<SolicitudF8?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.SolicitudesF8.SingleOrDefaultAsync(s => s.Id == id, ct);

    public Task<SolicitudF8?> GetAbiertaPorCarpetaAsync(Guid carpetaId, CancellationToken ct = default) =>
        db.SolicitudesF8.SingleOrDefaultAsync(s => s.CarpetaId == carpetaId && s.Estado == EstadoSolicitudF8.Solicitado, ct);

    public async Task AddAsync(SolicitudF8 solicitud, CancellationToken ct = default) =>
        await db.SolicitudesF8.AddAsync(solicitud, ct);

    public async Task<(IReadOnlyList<SolicitudF8Dto> Items, int Total)> ListarAsync(FiltroSolicitudesF8 filtro, CancellationToken ct = default)
    {
        // The inner join goes through the Carpetas soft-delete filter: requests of cancelled carpetas disappear.
        var query = from s in db.SolicitudesF8.AsNoTracking()
                    join c in db.Carpetas.AsNoTracking() on s.CarpetaId equals c.Id
                    select new { S = s, C = c };

        if (filtro.Estado is { } estado)
        {
            query = query.Where(x => x.S.Estado == estado);
        }
        if (filtro.RutFragment is { } rut)
        {
            query = query.Where(x => x.C.Ciudadano.Rut.Value.Contains(rut));
        }
        if (filtro.NameFragment is { } nombre)
        {
            query = query.Where(x => x.C.Ciudadano.NombreBusqueda.Contains(nombre));
        }

        var total = await query.CountAsync(ct);
        // The open queue is worked oldest first; resolved history shows the newest first.
        var ordenada = filtro.Estado == EstadoSolicitudF8.Solicitado
            ? query.OrderBy(x => x.S.SolicitadoEn).ThenBy(x => x.S.Id)
            : query.OrderByDescending(x => x.S.SolicitadoEn).ThenBy(x => x.S.Id);

        var filas = await ordenada
            .Skip(filtro.Skip)
            .Take(filtro.Take)
            .Select(x => new
            {
                x.S.Id,
                x.S.CarpetaId,
                Rut = x.C.Ciudadano.Rut.Value,
                x.C.Ciudadano.Nombre,
                x.C.Ciudadano.Apellido,
                x.C.Sede,
                x.C.Comuna,
                EstadoCarpeta = x.C.Estado,
                x.S.EstadoOrigen,
                x.S.SolicitadoPor,
                x.S.SolicitadoEn,
                x.S.Estado,
                x.S.ResueltoPor,
                x.S.ResueltoEn,
                x.S.Observacion,
                x.C.CodigoF8,
                x.C.FechaPenultimaCarpeta,
            })
            .ToListAsync(ct);

        return (filas.Select(r => new SolicitudF8Dto(
            r.Id,
            r.CarpetaId,
            SolicitudesFormato.FormatearRut(r.Rut),
            SolicitudesFormato.Nombre(r.Nombre, r.Apellido),
            r.Sede,
            r.Comuna,
            r.EstadoCarpeta,
            r.EstadoOrigen,
            r.SolicitadoPor,
            r.SolicitadoEn,
            r.Estado,
            r.ResueltoPor,
            r.ResueltoEn,
            r.Observacion)
        {
            CodigoF8 = r.CodigoF8,
            FechaPenultimaCarpeta = r.FechaPenultimaCarpeta,
        }).ToList(), total);
    }

    public async Task<IReadOnlyDictionary<EstadoSolicitudF8, int>> ContarPorEstadoAsync(CancellationToken ct = default)
    {
        var filas = await (from s in db.SolicitudesF8.AsNoTracking()
                           join c in db.Carpetas.AsNoTracking() on s.CarpetaId equals c.Id
                           group s by s.Estado into g
                           select new { Estado = g.Key, Total = g.Count() })
            .ToListAsync(ct);
        return filas.ToDictionary(f => f.Estado, f => f.Total);
    }
}

public sealed class CertificadoRepository(SglDbContext db) : ICertificadoRepository
{
    public Task<SolicitudCertificado?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.SolicitudesCertificado.SingleOrDefaultAsync(s => s.Id == id, ct);

    public Task<SolicitudCertificado?> GetAbiertoAsync(Guid carpetaId, TipoCertificado tipo, CancellationToken ct = default) =>
        db.SolicitudesCertificado.SingleOrDefaultAsync(
            s => s.CarpetaId == carpetaId && s.Tipo == tipo && s.Estado == EstadoCertificado.PorFirmar, ct);

    public async Task AddAsync(SolicitudCertificado certificado, CancellationToken ct = default) =>
        await db.SolicitudesCertificado.AddAsync(certificado, ct);

    public async Task<(IReadOnlyList<SolicitudCertificadoDto> Items, int Total)> ListarAsync(FiltroCertificados filtro, CancellationToken ct = default)
    {
        var query = from s in db.SolicitudesCertificado.AsNoTracking()
                    join c in db.Carpetas.AsNoTracking() on s.CarpetaId equals c.Id
                    select new { S = s, C = c };

        if (filtro.Id is { } id)
        {
            query = query.Where(x => x.S.Id == id);
        }
        if (filtro.Estado is { } estado)
        {
            query = query.Where(x => x.S.Estado == estado);
        }
        if (filtro.Tipo is { } tipo)
        {
            query = query.Where(x => x.S.Tipo == tipo);
        }
        if (filtro.RutFragment is { } rut)
        {
            query = query.Where(x => x.C.Ciudadano.Rut.Value.Contains(rut));
        }
        if (filtro.NameFragment is { } nombre)
        {
            query = query.Where(x => x.C.Ciudadano.NombreBusqueda.Contains(nombre));
        }

        var total = await query.CountAsync(ct);
        var ordenada = filtro.Estado == EstadoCertificado.PorFirmar
            ? query.OrderBy(x => x.S.SolicitadoEn).ThenBy(x => x.S.Id)
            : query.OrderByDescending(x => x.S.SolicitadoEn).ThenBy(x => x.S.Id);

        var filas = await ordenada
            .Skip(filtro.Skip)
            .Take(filtro.Take)
            .Select(x => new
            {
                x.S.Id,
                x.S.CarpetaId,
                Rut = x.C.Ciudadano.Rut.Value,
                x.C.Ciudadano.Nombre,
                x.C.Ciudadano.Apellido,
                x.C.Sede,
                x.C.Comuna,
                EstadoCarpeta = x.C.Estado,
                x.S.Tipo,
                x.S.Origen,
                x.S.SolicitudF8Id,
                x.S.SolicitadoPor,
                x.S.SolicitadoEn,
                x.S.Estado,
                x.S.FirmadoPor,
                x.S.FirmadoEn,
                x.S.NumeroOficio,
                x.S.ParametrosJson,
                x.S.EnviadoADirectorEn,
                x.S.Observacion,
            })
            .ToListAsync(ct);

        return (filas.Select(r => new SolicitudCertificadoDto(
            r.Id,
            r.CarpetaId,
            SolicitudesFormato.FormatearRut(r.Rut),
            SolicitudesFormato.Nombre(r.Nombre, r.Apellido),
            r.Sede,
            r.Comuna,
            r.EstadoCarpeta,
            r.Tipo,
            r.Origen,
            r.SolicitudF8Id,
            r.SolicitadoPor,
            r.SolicitadoEn,
            r.Estado,
            r.FirmadoPor,
            r.FirmadoEn,
            r.NumeroOficio,
            JsonSerializer.Deserialize<Dictionary<string, string>>(r.ParametrosJson) ?? [],
            r.EnviadoADirectorEn,
            r.Observacion)).ToList(), total);
    }

    public async Task<IReadOnlyDictionary<EstadoCertificado, int>> ContarPorEstadoAsync(TipoCertificado? tipo, CancellationToken ct = default)
    {
        var query = from s in db.SolicitudesCertificado.AsNoTracking()
                    join c in db.Carpetas.AsNoTracking() on s.CarpetaId equals c.Id
                    select s;
        if (tipo is { } t)
        {
            query = query.Where(s => s.Tipo == t);
        }

        var filas = await query.GroupBy(s => s.Estado).Select(g => new { Estado = g.Key, Total = g.Count() }).ToListAsync(ct);
        return filas.ToDictionary(f => f.Estado, f => f.Total);
    }
}

/// <summary>Signers stored in the Configuracion key/value table; missing keys read as empty.</summary>
public sealed class FirmantesRepository(SglDbContext db) : IFirmantesRepository
{
    private const string DirectorNombre = "Firmantes.DirectorNombre";
    private const string DirectorCargo = "Firmantes.DirectorCargo";
    private const string DirectorCorreo = "Firmantes.DirectorCorreo";
    private const string SecretariaNombre = "Firmantes.SecretariaNombre";
    private const string SecretariaCargo = "Firmantes.SecretariaCargo";
    private const string ModificadoPor = "Firmantes.ModificadoPor";
    private const string ModificadoEn = "Firmantes.ModificadoEn";

    public async Task<Firmantes> ObtenerAsync(CancellationToken ct = default)
    {
        var items = await db.Configuracion.AsNoTracking()
            .Where(i => i.Clave.StartsWith("Firmantes."))
            .ToDictionaryAsync(i => i.Clave, i => i.Valor, ct);
        return new Firmantes(
            items.GetValueOrDefault(DirectorNombre, string.Empty),
            items.GetValueOrDefault(DirectorCargo, string.Empty),
            items.GetValueOrDefault(DirectorCorreo, string.Empty),
            items.GetValueOrDefault(SecretariaNombre, string.Empty),
            items.GetValueOrDefault(SecretariaCargo, string.Empty));
    }

    public async Task GuardarAsync(Firmantes firmantes, string modificadoPor, DateTime modificadoEnUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(firmantes);
        var valores = new Dictionary<string, string>
        {
            [DirectorNombre] = firmantes.DirectorNombre,
            [DirectorCargo] = firmantes.DirectorCargo,
            [DirectorCorreo] = firmantes.DirectorCorreo,
            [SecretariaNombre] = firmantes.SecretariaNombre,
            [SecretariaCargo] = firmantes.SecretariaCargo,
            [ModificadoPor] = modificadoPor,
            [ModificadoEn] = modificadoEnUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

        var existentes = await db.Configuracion.Where(i => i.Clave.StartsWith("Firmantes.")).ToDictionaryAsync(i => i.Clave, ct);
        foreach (var (clave, valor) in valores)
        {
            if (existentes.TryGetValue(clave, out var item))
            {
                item.Valor = valor;
            }
            else
            {
                db.Configuracion.Add(new ConfiguracionItem { Clave = clave, Valor = valor });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

internal static class SolicitudesFormato
{
    public static string FormatearRut(string valor) => Sgl.Domain.Rut.TryParse(valor, out var rut) ? rut!.Formatted : valor;

    public static string Nombre(string nombre, string apellido) => $"{nombre} {apellido}".Trim().ToUpperInvariant();
}
