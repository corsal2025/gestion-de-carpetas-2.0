using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>Use cases for resolving duplicates from the Por clasificar tray: citizens with more than one open carpeta.</summary>
public sealed class DuplicadosService(IDuplicadoRepository repository, TimeProvider clock, UnidadDeTrabajoGate? gate = null)
{
    private readonly UnidadDeTrabajoGate _gate = gate ?? new UnidadDeTrabajoGate();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ListResult<DuplicadoGrupoDto>> ListarAsync(ListarDuplicadosQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var q = query.Normalized();
        var (rutFragment, nameFragment) = TextoBusqueda.Parse(q.Texto);
        var search = new DuplicadoSearch(rutFragment, nameFragment, q.Sede, q.Skip, q.Take, q.SinCarpetaPorClasificar);
        var (items, total) = await _gate.LeerAsync(() => repository.SearchAsync(search, ct), ct);
        return new ListResult<DuplicadoGrupoDto>(items, total);
    }

    public async Task<int> ContarPendientesAsync(CancellationToken ct = default)
    {
        var (_, total) = await _gate.LeerAsync(() => repository.SearchAsync(new DuplicadoSearch(null, null, null, 0, 0), ct), ct);
        return total;
    }

    public Task<IReadOnlyList<CarpetaDuplicadoDetalleDto>> ObtenerDetalleCarpetasAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        _gate.LeerAsync(() => repository.GetDetalleCarpetasAbiertasAsync(ciudadanoId, ct), ct);

    public async Task MarcarRevisadoAsync(Guid ciudadanoId, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var existente = await repository.GetRevisionAsync(ciudadanoId, ct);
            if (existente is null)
            {
                await repository.AddRevisionAsync(new RevisionDuplicado(ciudadanoId, autor, Now), ct);
            }
            else
            {
                existente.Renovar(autor, Now);
            }

            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
