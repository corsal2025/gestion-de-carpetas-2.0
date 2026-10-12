using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Application.Estadisticas;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class AnulacionYReagendamientoTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static readonly DateTime Antes = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Juan has an open carpeta from 02-04 (Placilla, Se encuentra en archivos) and a new agenda row on 04-05.</summary>
    private (Guid Existente, Guid Nueva, Guid Otra) Seed(ClaseLicencia clasesExistente = ClaseLicencia.B)
    {
        using var ctx = _db.NewContext();
        var juan = new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ");
        var ana = new Ciudadano(Rut.Parse("11111111-1"), "ANA", "SOTO");
        var existente = Carpeta.CrearDesdeAgenda(juan, Sede.Placilla, new DateOnly(2026, 4, 2), null, TramiteAgenda.Renovacion, "imp", Antes);
        existente.Clasificar(EstadoCarpeta.SeEncuentraEnArchivos, clasesExistente, null, null, "javiera", Antes);
        var nueva = Carpeta.CrearDesdeAgenda(juan, Sede.AvArgentina, new DateOnly(2026, 5, 4), new TimeOnly(9, 0), TramiteAgenda.Renovacion, "imp", Antes);
        var otra = Carpeta.CrearDesdeAgenda(ana, Sede.AvArgentina, new DateOnly(2026, 5, 4), new TimeOnly(9, 30), TramiteAgenda.Renovacion, "imp", Antes);
        ctx.Carpetas.AddRange(existente, nueva, otra);
        ctx.SaveChanges();
        return (existente.Id, nueva.Id, otra.Id);
    }

    [Fact]
    public async Task Clasificar_without_clase_is_rejected()
    {
        var (_, _, otra) = Seed();

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().ClasificarAsync(
            new ClasificarCommand([otra], EstadoCarpeta.SubidaConaset, ClaseLicencia.Ninguna, null, null, "javiera")));

        Assert.Equal(EstadoCarpeta.PorClasificar, (await _db.NewService().ObtenerAsync(otra)).Estado);
    }

    [Fact]
    public async Task Clasificar_blocks_when_same_rut_and_clase_is_already_open()
    {
        var (_, nueva, _) = Seed(ClaseLicencia.B);

        var ex = await Assert.ThrowsAsync<CarpetaDuplicadaException>(() => _db.NewService().ClasificarAsync(
            new ClasificarCommand([nueva], EstadoCarpeta.SubidaConaset, ClaseLicencia.B, null, null, "javiera")));

        Assert.True(ex.Bloqueante);
        Assert.Equal(EstadoCarpeta.PorClasificar, (await _db.NewService().ObtenerAsync(nueva)).Estado);
    }

    [Fact]
    public async Task Clasificar_with_different_clase_creates_a_new_open_carpeta()
    {
        var (_, nueva, _) = Seed(ClaseLicencia.B);

        await _db.NewService().ClasificarAsync(
            new ClasificarCommand([nueva], EstadoCarpeta.SubidaConaset, ClaseLicencia.A2, null, null, "javiera"));

        var det = await _db.NewService().ObtenerAsync(nueva);
        Assert.Equal(EstadoCarpeta.SubidaConaset, det.Estado);
        Assert.Equal(ClaseLicencia.A2, det.Clases);
    }

    [Fact]
    public async Task Clasificar_batch_blocks_two_rows_of_same_rut_with_same_clase()
    {
        using (var ctx = _db.NewContext())
        {
            var pedro = new Ciudadano(Rut.Parse("22222222-2"), "PEDRO", "ROJAS");
            ctx.Carpetas.AddRange(
                Carpeta.CrearDesdeAgenda(pedro, Sede.AvArgentina, new DateOnly(2026, 5, 4), null, null, "imp", Antes),
                Carpeta.CrearDesdeAgenda(pedro, Sede.AvArgentina, new DateOnly(2026, 5, 5), null, null, "imp", Antes));
            ctx.SaveChanges();
        }
        var ids = (await _db.NewService().ListarAsync(new ListarCarpetasQuery(Texto: "22222222", Bandeja: Bandeja.PorClasificar)))
            .Items.Select(i => i.Id).ToList();

        await Assert.ThrowsAsync<CarpetaDuplicadaException>(() => _db.NewService().ClasificarAsync(
            new ClasificarCommand(ids, EstadoCarpeta.SubidaConaset, ClaseLicencia.B, null, null, "javiera")));
    }

    [Fact]
    public async Task Listar_PorClasificar_flags_row_whose_rut_has_an_open_carpeta()
    {
        var (existente, nueva, otra) = Seed();

        var res = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.PorClasificar, Take: 50));

        var alerta = res.Items.Single(i => i.Id == nueva).AlertaDuplicado;
        Assert.NotNull(alerta);
        Assert.Equal(existente, alerta!.CarpetaId);
        Assert.Equal(new DateOnly(2026, 4, 2), alerta.FechaCitacion);
        Assert.Equal(Sede.Placilla, alerta.Sede);
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, alerta.Estado);
        Assert.Null(res.Items.Single(i => i.Id == otra).AlertaDuplicado);
    }

    [Fact]
    public async Task Listar_does_not_flag_when_the_other_carpeta_is_archived_closed_or_cancelled()
    {
        var (existente, nueva, _) = Seed();
        await _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(existente, "CAJA 7", "javiera"));

        var res = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.PorClasificar, Take: 50));

        Assert.Null(res.Items.Single(i => i.Id == nueva).AlertaDuplicado);
    }

    [Fact]
    public async Task Reagendar_updates_existing_date_and_sede_and_cancels_the_new_row()
    {
        var (existente, nueva, _) = Seed();

        await _db.NewService().ReagendarAsync(origenId: nueva, destinoId: existente, "javiera");

        var det = await _db.NewService().ObtenerAsync(existente);
        Assert.Equal(new DateOnly(2026, 5, 4), det.FechaCitacion);
        Assert.Equal(Sede.AvArgentina, det.Sede);
        Assert.Contains(det.Historial, h => h.Campo == "Reagendamiento" && h.Nuevo == "Reagendada desde 02-04 a 04-05");
        var anulada = await _db.NewService().ObtenerAsync(nueva);
        Assert.True(anulada.Anulada);
        Assert.Equal(MotivosAnulacion.Reagendamiento, anulada.MotivoAnulacion);
        var conteos = await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery());
        Assert.Equal(1, conteos[Bandeja.PorClasificar]);
    }

    [Fact]
    public async Task Reagendar_rejects_carpetas_of_different_citizens()
    {
        var (existente, _, otra) = Seed();

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().ReagendarAsync(otra, existente, "javiera"));
    }

    [Fact]
    public async Task Anuladas_disappear_from_lists_counts_duplicates_and_stats_and_can_be_restored()
    {
        var (existente, nueva, _) = Seed();
        Assert.Equal(1, await _db.NewDuplicadosService().ContarPendientesAsync());

        await _db.NewService().AnularAsync(nueva, MotivosAnulacion.Duplicado, "javiera");

        var svc = _db.NewService();
        var conteos = await svc.ContarPorBandejaAsync(new ListarCarpetasQuery());
        Assert.Equal(1, conteos[Bandeja.PorClasificar]);
        Assert.Equal(2, conteos.Values.Sum());
        Assert.Equal(2, (await svc.ListarAsync(new ListarCarpetasQuery())).Total);
        Assert.Equal(0, await _db.NewDuplicadosService().ContarPendientesAsync());
        var stats = await new EstadisticasQuery(_db.NewContext()).ObtenerAsync(new FiltroEstadisticas(null, null, null, 30), new DateOnly(2026, 5, 4));
        Assert.Equal(2, stats.Actuales.EstadoPorSede.Sum(e => e.Cantidad));

        var anuladas = await _db.NewService().ListarAnuladasAsync();
        var a = Assert.Single(anuladas);
        Assert.Equal(nueva, a.Id);
        Assert.Equal(MotivosAnulacion.Duplicado, a.Motivo);
        Assert.Equal("javiera", a.AnuladaPor);

        await _db.NewService().RestaurarAsync(nueva, "raul");

        Assert.Empty(await _db.NewService().ListarAnuladasAsync());
        Assert.Equal(3, (await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery())).Values.Sum());
        Assert.Equal(EstadoCarpeta.PorClasificar, (await _db.NewService().ObtenerAsync(nueva)).Estado);
        _ = existente;
    }

    [Fact]
    public async Task Anular_is_never_a_physical_delete()
    {
        var (_, nueva, _) = Seed();

        await _db.NewService().AnularAsync(nueva, MotivosAnulacion.Duplicado, "javiera");

        using var ctx = _db.NewContext();
        Assert.Equal(3, ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas").Single());
    }
}
