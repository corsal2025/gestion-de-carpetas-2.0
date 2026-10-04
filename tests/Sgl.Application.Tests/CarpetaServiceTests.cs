using Microsoft.EntityFrameworkCore;
using Sgl.Application;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

public sealed class CarpetaServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static RegistrarCarpetaCommand Cmd(
        string rut = "12.345.678-5",
        string nombre = "Juan",
        string apellido = "Pérez",
        Sede sede = Sede.AvArgentina) =>
        new(rut, nombre, apellido, sede, new DateOnly(2026, 5, 4), "ana");

    [Fact]
    public async Task Registrar_persists_carpeta_in_Citada_with_audit_entry()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd());

        await using var ctx = _db.NewContext();
        var carpeta = await ctx.Carpetas.Include(c => c.Ciudadano).SingleAsync(c => c.Id == id);
        Assert.Equal(EstadoCarpeta.Citada, carpeta.Estado);
        Assert.Equal("12345678-5", carpeta.Ciudadano.Rut.Value);
        var audit = await ctx.Historial.Where(h => h.CarpetaId == id).ToListAsync();
        var entry = Assert.Single(audit);
        Assert.Equal("Registro", entry.Campo);
        Assert.Equal("ana", entry.Usuario);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, entry.Fecha);
    }

    [Fact]
    public async Task Registrar_with_invalid_rut_throws_and_persists_nothing()
    {
        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().RegistrarAsync(Cmd(rut: "12345678-9")));

        await using var ctx = _db.NewContext();
        Assert.Equal(0, await ctx.Carpetas.CountAsync());
    }

    [Fact]
    public async Task Registrar_reuses_existing_ciudadano_with_same_rut()
    {
        await _db.NewService().RegistrarAsync(Cmd(rut: "12345678-5"));
        await _db.NewService().RegistrarAsync(Cmd(rut: "12.345.678-5", sede: Sede.Placilla));

        await using var ctx = _db.NewContext();
        Assert.Equal(1, await ctx.Ciudadanos.CountAsync());
        Assert.Equal(2, await ctx.Carpetas.CountAsync());
    }

    [Fact]
    public async Task CambiarEstado_persists_new_state_and_writes_audit()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd());

        await _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.Revision, "pedro"));

        await using var ctx = _db.NewContext();
        Assert.Equal(EstadoCarpeta.Revision, (await ctx.Carpetas.SingleAsync(c => c.Id == id)).Estado);
        Assert.True(await ctx.Historial.AnyAsync(h =>
            h.CarpetaId == id && h.Campo == "Estado" && h.Anterior == "Citada" && h.Nuevo == "Revision" && h.Usuario == "pedro"));
    }

    [Fact]
    public async Task CambiarEstado_invalid_transition_throws_and_persists_nothing()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd());

        await Assert.ThrowsAsync<InvalidTransitionException>(() =>
            _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.Otorgado, "pedro")));

        await using var ctx = _db.NewContext();
        Assert.Equal(EstadoCarpeta.Citada, (await ctx.Carpetas.SingleAsync(c => c.Id == id)).Estado);
        Assert.Equal(1, await ctx.Historial.CountAsync(h => h.CarpetaId == id));
    }

    [Fact]
    public async Task CambiarEstado_unknown_carpeta_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(Guid.NewGuid(), EstadoCarpeta.Revision, "pedro")));
    }

    [Fact]
    public async Task Reaching_Otorgado_persists_decision()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd());
        foreach (var estado in new[] { EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen, EstadoCarpeta.Otorgado })
        {
            await _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, estado, "pedro"));
        }

        var detalle = await _db.NewService().ObtenerAsync(id);

        Assert.Equal(Decision.Otorgado, detalle.Decision);
        Assert.Empty(detalle.SiguientesEstados);
    }

    [Fact]
    public async Task Obtener_returns_next_states_and_history_newest_first()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd());
        await _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.Revision, "pedro"));

        var detalle = await _db.NewService().ObtenerAsync(id);

        Assert.Equal("12.345.678-5", detalle.Rut);
        Assert.Equal([EstadoCarpeta.Alertada, EstadoCarpeta.SubidaConaset], detalle.SiguientesEstados);
        Assert.Equal(2, detalle.Historial.Count);
        Assert.Equal("Estado", detalle.Historial[0].Campo);
    }

    [Fact]
    public async Task Obtener_unknown_carpeta_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _db.NewService().ObtenerAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Editar_persists_changes_and_audits_each_field()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd());

        await _db.NewService().EditarAsync(new EditarCarpetaCommand(
            id, Sede.MercadoPuerto, new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 10), "Favorable", "pedro"));

        var detalle = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(Sede.MercadoPuerto, detalle.Sede);
        Assert.Equal(new DateOnly(2026, 5, 10), detalle.FechaSubida);
        Assert.Equal("Favorable", detalle.IdoneidadMoral);
        Assert.Equal(4, detalle.Historial.Count);
    }

    [Fact]
    public async Task Listar_filters_by_sede_and_estado()
    {
        var a = await _db.NewService().RegistrarAsync(Cmd(rut: "11111111-1", sede: Sede.AvArgentina));
        await _db.NewService().RegistrarAsync(Cmd(rut: "7654321-6", sede: Sede.Placilla));
        await _db.NewService().RegistrarAsync(Cmd(rut: "10000013-K", sede: Sede.AvArgentina));
        await _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(a, EstadoCarpeta.Revision, "pedro"));

        var porSede = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Sede: Sede.AvArgentina));
        var porEstado = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Estado: EstadoCarpeta.Revision));

        Assert.Equal(2, porSede.Total);
        Assert.All(porSede.Items, i => Assert.Equal(Sede.AvArgentina, i.Sede));
        Assert.Equal(a, Assert.Single(porEstado.Items).Id);
    }

    [Theory]
    [InlineData("7.654.321-6")]
    [InlineData("7654321")]
    [InlineData("gonzález")]
    [InlineData("MARÍA")]
    public async Task Listar_searches_by_rut_or_name(string texto)
    {
        await _db.NewService().RegistrarAsync(Cmd(rut: "7654321-6", nombre: "María", apellido: "González"));
        await _db.NewService().RegistrarAsync(Cmd(rut: "11111111-1", nombre: "Juan", apellido: "Pérez"));

        var result = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Texto: texto));

        Assert.Equal("7.654.321-6", Assert.Single(result.Items).Rut);
    }

    [Fact]
    public async Task Listar_paginates_with_total_count()
    {
        string[] ruts = ["11111111-1", "7654321-6", "10000013-K", "12345678-5", "22222222-2"];
        foreach (var rut in ruts)
        {
            await _db.NewService().RegistrarAsync(Cmd(rut: rut));
        }

        var page2 = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Page: 2, PageSize: 2));
        var page3 = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Page: 3, PageSize: 2));

        Assert.Equal(5, page2.Total);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);
        Assert.Equal(3, page2.TotalPages);
    }

    [Fact]
    public async Task Listar_clamps_invalid_paging_values()
    {
        await _db.NewService().RegistrarAsync(Cmd());

        var result = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Page: 0, PageSize: 10_000));

        Assert.Equal(1, result.Page);
        Assert.Equal(ListarCarpetasQuery.MaxPageSize, result.PageSize);
    }

    [Fact]
    public async Task ForzarEstado_changes_state_directly()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd());
        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.SubidaConaset, "test_user");

        var c = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(EstadoCarpeta.SubidaConaset, c.Estado);
    }

    [Fact]
    public async Task ModificarRut_updates_rut_and_writes_audit()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd(rut: "12.345.678-5"));
        await _db.NewService().ModificarRutAsync(id, "11.111.111-1", "operador");

        var c = await _db.NewService().ObtenerAsync(id);
        Assert.Equal("11.111.111-1", c.Rut);

        var audit = c.Historial.FirstOrDefault(h => h.Campo == "RUT");
        Assert.NotNull(audit);
        Assert.Equal("12.345.678-5", audit.Anterior);
        Assert.Equal("11.111.111-1", audit.Nuevo);
    }

    [Fact]
    public async Task EditarAsync_with_new_rut_updates_rut_correctly()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd(rut: "12.345.678-5"));
        await _db.NewService().EditarAsync(new EditarCarpetaCommand(
            id, Sede.Placilla, new DateOnly(2026, 6, 1), null, null, "operador", "11.111.111-1"));

        var c = await _db.NewService().ObtenerAsync(id);
        Assert.Equal("11.111.111-1", c.Rut);
        Assert.Equal(Sede.Placilla, c.Sede);
    }
}
