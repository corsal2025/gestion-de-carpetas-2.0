using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

public sealed class ListarPorIdsTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();

    private async Task SeedAsync()
    {
        string[] ruts = ["11111111-1", "22222222-2", "33333333-3", "44444444-4", "55555555-5"];
        for (var i = 0; i < ruts.Length; i++)
        {
            await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(
                ruts[i], "N" + i, "Apellido" + (ruts.Length - i), Sede.AvArgentina, new DateOnly(2026, 5, 1 + i % 2), "ana"));
        }
    }

    [Fact]
    public async Task ListarIdsAsync_returns_the_same_order_as_paged_listing()
    {
        await SeedAsync();
        var paged = (await _db.NewService().ListarAsync(new ListarCarpetasQuery())).Items.Select(i => i.Id).ToList();

        var ids = await _db.NewService().ListarIdsAsync(new ListarCarpetasQuery());

        Assert.Equal(paged, ids);
    }

    [Fact]
    public async Task ListarAsync_by_ids_returns_the_requested_rows_in_the_given_order()
    {
        await SeedAsync();
        var ids = await _db.NewService().ListarIdsAsync(new ListarCarpetasQuery());
        var slice = new[] { ids[3], ids[1], ids[4] };

        var r = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Ids: slice));

        Assert.Equal(slice, r.Items.Select(i => i.Id).ToArray());
        Assert.Equal(3, r.Total);
        Assert.All(r.Items, i => Assert.NotNull(i.Indicadores));
    }
}
