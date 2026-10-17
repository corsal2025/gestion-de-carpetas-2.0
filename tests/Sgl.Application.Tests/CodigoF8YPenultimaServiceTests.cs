using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class CodigoF8YPenultimaServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private Task<Guid> CrearAsync(EstadoCarpeta estado = EstadoCarpeta.NoExisteCarpeta) =>
        SolicitudesTestSupport.CrearCarpetaAsync(_db, estado);

    private async Task<CarpetaResumenDto> ResumenAsync(Guid id) =>
        (await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.SinCarpetaF8, Take: 50)))
            .Items.Single(i => i.Id == id);

    [Fact]
    public async Task Code_is_persisted_normalized_audited_and_projected_in_the_listing()
    {
        var id = await CrearAsync();

        await _db.NewService().GuardarCodigoF8Async(id, "  7701 ", "ana");

        var resumen = await ResumenAsync(id);
        Assert.Equal("7701", resumen.CodigoF8);
        var detalle = await SolicitudesTestSupport.DetalleAsync(_db, id);
        Assert.Contains(detalle.Historial, h => h.Campo == nameof(Carpeta.CodigoF8) && h.Nuevo == "7701" && h.Usuario == "ana");
    }

    [Fact]
    public async Task A_code_with_letters_is_rejected_and_not_saved()
    {
        var id = await CrearAsync();

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().GuardarCodigoF8Async(id, "F8-1", "ana"));

        Assert.Null((await ResumenAsync(id)).CodigoF8);
    }

    [Fact]
    public async Task Penultimate_date_is_persisted_audited_and_projected_in_the_listing()
    {
        var id = await CrearAsync();

        await _db.NewService().GuardarFechaPenultimaCarpetaAsync(id, new DateOnly(2019, 5, 2), "ana");

        var resumen = await ResumenAsync(id);
        Assert.Equal(new DateOnly(2019, 5, 2), resumen.FechaPenultimaCarpeta);
        var detalle = await SolicitudesTestSupport.DetalleAsync(_db, id);
        Assert.Contains(detalle.Historial, h => h.Campo == nameof(Carpeta.FechaPenultimaCarpeta) && h.Nuevo == "2019-05-02");
    }

    [Fact]
    public async Task The_F8_module_listing_shows_both_values_of_the_carpeta()
    {
        var id = await CrearAsync(EstadoCarpeta.SeEncuentraEnArchivos);
        await _db.NewService().GuardarCodigoF8Async(id, "5501", "ana");
        await _db.NewService().GuardarFechaPenultimaCarpetaAsync(id, new DateOnly(2019, 5, 2), "ana");
        await _db.NewF8Service().SolicitarF8Async(id, "ana");

        var fila = Assert.Single((await _db.NewF8Service().ListarAsync(new Sgl.Application.Solicitudes.ListarSolicitudesF8Query(null, null))).Items);

        Assert.Equal("5501", fila.CodigoF8);
        Assert.Equal(new DateOnly(2019, 5, 2), fila.FechaPenultimaCarpeta);
    }

    [Fact]
    public async Task The_search_mark_can_be_set_in_NoExiste_and_is_cleared_when_it_leaves_the_set()
    {
        var id = await CrearAsync();

        await _db.NewService().MarcarPendienteBusquedaAsync(id, true, "ana");
        Assert.True((await ResumenAsync(id)).PendienteBusqueda);

        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.CrearCertificado, "ana");
        var detalle = await SolicitudesTestSupport.DetalleAsync(_db, id);
        Assert.Contains(detalle.Historial, h => h.Campo == nameof(Carpeta.PendienteBusqueda) && h.Nuevo == "No");
    }

    [Fact]
    public async Task Both_values_can_be_cleared()
    {
        var id = await CrearAsync();
        await _db.NewService().GuardarCodigoF8Async(id, "11", "ana");
        await _db.NewService().GuardarFechaPenultimaCarpetaAsync(id, new DateOnly(2019, 5, 2), "ana");

        await _db.NewService().GuardarCodigoF8Async(id, "", "ana");
        await _db.NewService().GuardarFechaPenultimaCarpetaAsync(id, null, "ana");

        var resumen = await ResumenAsync(id);
        Assert.Null(resumen.CodigoF8);
        Assert.Null(resumen.FechaPenultimaCarpeta);
    }

    [Fact]
    public async Task An_author_is_required_and_nothing_is_saved_without_one()
    {
        var id = await CrearAsync();

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().GuardarCodigoF8Async(id, "11", " "));
        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().GuardarFechaPenultimaCarpetaAsync(id, new DateOnly(2019, 5, 2), ""));

        var resumen = await ResumenAsync(id);
        Assert.Null(resumen.CodigoF8);
        Assert.Null(resumen.FechaPenultimaCarpeta);
    }

    [Fact]
    public async Task A_future_date_is_rejected_and_not_saved()
    {
        var id = await CrearAsync();
        var manana = HoraChile.Fecha(_db.Clock.GetUtcNow().UtcDateTime).AddDays(1);

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().GuardarFechaPenultimaCarpetaAsync(id, manana, "ana"));

        Assert.Null((await ResumenAsync(id)).FechaPenultimaCarpeta);
    }

    [Fact]
    public async Task An_annulled_carpeta_cannot_be_edited()
    {
        var id = await CrearAsync();
        await _db.NewService().AnularAsync(id, "duplicada", "ana");

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().GuardarCodigoF8Async(id, "11", "ana"));
        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().GuardarFechaPenultimaCarpetaAsync(id, new DateOnly(2019, 5, 2), "ana"));
    }

    [Fact]
    public async Task The_values_can_be_filled_while_the_carpeta_is_in_another_estado()
    {
        var id = await CrearAsync(EstadoCarpeta.Revision);

        await _db.NewService().GuardarCodigoF8Async(id, "41", "ana");

        var detalle = await SolicitudesTestSupport.DetalleAsync(_db, id);
        Assert.Contains(detalle.Historial, h => h.Campo == nameof(Carpeta.CodigoF8) && h.Nuevo == "41");
        Assert.Equal(EstadoCarpeta.Revision, detalle.Estado);
    }

    [Fact]
    public void The_migration_adds_both_nullable_columns_on_an_empty_database_with_no_pending_model_changes()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var ctx = new SglDbContext(new DbContextOptionsBuilder<SglDbContext>().UseSqlite(connection).Options);

        ctx.Database.Migrate();

        Assert.False(ctx.Database.HasPendingModelChanges());
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, \"notnull\" FROM pragma_table_info('Carpetas') WHERE name IN ('CodigoF8','FechaPenultimaCarpeta') ORDER BY name";
        using var r = cmd.ExecuteReader();
        var cols = new List<(string, long)>();
        while (r.Read())
        {
            cols.Add((r.GetString(0), r.GetInt64(1)));
        }
        Assert.Equal([("CodigoF8", 0L), ("FechaPenultimaCarpeta", 0L)], cols);
    }
}
