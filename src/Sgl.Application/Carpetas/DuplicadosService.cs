using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>Use cases for the "Posibles Duplicados" queue: citizens with more than one open carpeta.</summary>
public sealed class DuplicadosService(IDuplicadoRepository repository, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ListResult<DuplicadoGrupoDto>> ListarAsync(ListarDuplicadosQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var q = query.Normalized();
        var (rutFragment, nameFragment) = TextoBusqueda.Parse(q.Texto);
        var search = new DuplicadoSearch(rutFragment, nameFragment, q.Sede, q.Skip, q.Take);
        var (items, total) = await repository.SearchAsync(search, ct);
        return new ListResult<DuplicadoGrupoDto>(items, total);
    }

    public async Task<int> ContarPendientesAsync(CancellationToken ct = default)
    {
        var (_, total) = await repository.SearchAsync(new DuplicadoSearch(null, null, null, 0, 0), ct);
        return total;
    }

    public Task<IReadOnlyList<CarpetaAbiertaDto>> ObtenerCarpetasAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        repository.GetCarpetasAbiertasAsync(ciudadanoId, ct);

    public Task<IReadOnlyList<CarpetaDuplicadoDetalleDto>> ObtenerDetalleCarpetasAsync(Guid ciudadanoId, CancellationToken ct = default) =>
        repository.GetDetalleCarpetasAbiertasAsync(ciudadanoId, ct);

    public async Task MarcarRevisadoAsync(Guid ciudadanoId, string autor, CancellationToken ct = default)
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
}
