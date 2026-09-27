using System.Text.RegularExpressions;
using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>Use cases for carpetas: RegistrarCarpeta, CambiarEstado, EditarCarpeta, ListarCarpetas, ObtenerCarpeta.</summary>
public sealed partial class CarpetaService(ICarpetaRepository repository, TimeProvider clock)
{
    [GeneratedRegex(@"^[0-9.\-kK]+$")]
    private static partial Regex RutLikeText();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Guid> RegistrarAsync(RegistrarCarpetaCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var rut = Rut.Parse(cmd.Rut);
        var ciudadano = await repository.FindCiudadanoAsync(rut, ct)
                        ?? new Ciudadano(rut, cmd.Nombre, cmd.Apellido);

        var carpeta = Carpeta.Registrar(ciudadano, cmd.Sede, cmd.FechaCitacion, cmd.Autor, Now);
        await repository.AddAsync(carpeta, ct);
        await repository.SaveChangesAsync(ct);
        return carpeta.Id;
    }

    public async Task CambiarEstadoAsync(CambiarEstadoCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var carpeta = await LoadAsync(cmd.CarpetaId, ct);
        carpeta.CambiarEstado(cmd.NuevoEstado, cmd.Autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task EditarAsync(EditarCarpetaCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var carpeta = await LoadAsync(cmd.CarpetaId, ct);
        carpeta.Editar(cmd.Sede, cmd.FechaCitacion, cmd.FechaSubida, cmd.IdoneidadMoral, cmd.Autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task<CarpetaDetalleDto> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var c = await LoadAsync(id, ct);
        var historial = c.Historial
            .OrderByDescending(h => h.Fecha)
            .ThenByDescending(h => h.Id)
            .Select(h => new HistorialDto(h.Fecha, h.Usuario, h.Campo, h.Anterior, h.Nuevo))
            .ToList();

        return new CarpetaDetalleDto(
            c.Id,
            c.Ciudadano.Rut.Formatted,
            c.Ciudadano.Nombre,
            c.Ciudadano.Apellido,
            c.Sede,
            c.FechaCitacion,
            c.FechaSubida,
            c.Estado,
            c.Decision,
            c.IdoneidadMoral,
            EstadoTransitions.NextStates(c.Estado),
            historial);
    }

    public async Task<PagedResult<CarpetaResumenDto>> ListarAsync(ListarCarpetasQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var q = query.Normalized();

        string? rutFragment = null;
        string? nameFragment = null;
        if (q.Texto is { } texto)
        {
            if (RutLikeText().IsMatch(texto))
            {
                rutFragment = texto.Replace(".", string.Empty).ToUpperInvariant();
            }
            else
            {
                nameFragment = Ciudadano.NormalizeForSearch(texto);
            }
        }

        var search = new CarpetaSearch(rutFragment, nameFragment, q.Sede, q.Estado, (q.Page - 1) * q.PageSize, q.PageSize);
        var (items, total) = await repository.SearchAsync(search, ct);
        return new PagedResult<CarpetaResumenDto>(items, total, q.Page, q.PageSize);
    }

    private async Task<Carpeta> LoadAsync(Guid id, CancellationToken ct) =>
        await repository.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");
}
