using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>
/// A Blazor circuit keeps one service/DbContext for many operations: a failed operation must never leave
/// tracked changes that a later, unrelated save would persist.
/// </summary>
public sealed class UnidadDeTrabajoTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static readonly DateTime Antes = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private CarpetaService ServicioDelCircuito(SglDbContext ctx) => new(new CarpetaRepository(ctx), _db.Clock);

    private (Guid Existente, Guid Nueva, Guid Otra) Seed()
    {
        using var ctx = _db.NewContext();
        var juan = new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ");
        var ana = new Ciudadano(Rut.Parse("11111111-1"), "ANA", "SOTO");
        var existente = Carpeta.CrearDesdeAgenda(juan, Sede.Placilla, new DateOnly(2026, 4, 2), null, null, "imp", Antes);
        existente.Clasificar(EstadoCarpeta.SeEncuentraEnArchivos, ClaseLicencia.B, null, null, "javiera", Antes);
        var nueva = Carpeta.CrearDesdeAgenda(juan, Sede.AvArgentina, new DateOnly(2026, 5, 4), null, null, "imp", Antes);
        var otra = Carpeta.CrearDesdeAgenda(ana, Sede.AvArgentina, new DateOnly(2026, 5, 4), null, null, "imp", Antes);
        ctx.Carpetas.AddRange(existente, nueva, otra);
        ctx.SaveChanges();
        return (existente.Id, nueva.Id, otra.Id);
    }

    [Fact]
    public async Task Failed_batch_classification_leaves_nothing_for_a_later_save()
    {
        var (existente, nueva, otra) = Seed();
        using var ctx = _db.NewContext();
        var svc = ServicioDelCircuito(ctx);

        // Row 1 (otra) is valid, row 2 (existente) is not "Por clasificar" → whole batch rejected.
        await Assert.ThrowsAsync<DomainException>(() => svc.ClasificarAsync(
            new ClasificarCommand([otra, existente], EstadoCarpeta.SubidaConaset, ClaseLicencia.A2, null, null, "javiera")));
        await svc.GuardarObservacionAsync(nueva, "OBS", "javiera");

        var det = await _db.NewService().ObtenerAsync(otra);
        Assert.Equal(EstadoCarpeta.PorClasificar, det.Estado);
        Assert.Equal(ClaseLicencia.Ninguna, det.Clases);
    }

    [Fact]
    public async Task Double_reagendar_records_a_single_history_line_and_fails_cleanly()
    {
        var (existente, nueva, otra) = Seed();
        using var ctx = _db.NewContext();
        var svc = ServicioDelCircuito(ctx);

        await svc.ReagendarAsync(nueva, existente, "javiera");
        await Assert.ThrowsAsync<DomainException>(() => svc.ReagendarAsync(nueva, existente, "javiera"));
        await svc.GuardarObservacionAsync(otra, "OBS", "javiera");

        var det = await _db.NewService().ObtenerAsync(existente);
        Assert.Single(det.Historial, h => h.Campo == "Reagendamiento");
        Assert.Equal(new DateOnly(2026, 5, 4), det.FechaCitacion);
    }

    [Fact]
    public async Task Cancelled_carpetas_reject_every_mutation_except_restore()
    {
        var (existente, nueva, _) = Seed();
        await _db.NewService().AnularAsync(nueva, MotivosAnulacion.Duplicado, "javiera");
        var svc = _db.NewService();

        await Assert.ThrowsAsync<DomainException>(() => svc.ClasificarAsync(
            new ClasificarCommand([nueva], EstadoCarpeta.SubidaConaset, ClaseLicencia.A2, null, null, "javiera")));
        await Assert.ThrowsAsync<DomainException>(() => svc.AsignarCajaAsync(new AsignarCajaCommand(nueva, "CAJA 1", "javiera")));
        await Assert.ThrowsAsync<DomainException>(() => svc.AsignarClasesAsync(nueva, ClaseLicencia.C, "javiera"));
        await Assert.ThrowsAsync<DomainException>(() => svc.GuardarObservacionAsync(nueva, "x", "javiera"));
        await Assert.ThrowsAsync<DomainException>(() => svc.CambiarDecisionAsync(nueva, Decision.Otorgado, "javiera"));
        await Assert.ThrowsAsync<DomainException>(() => svc.ForzarEstadoAsync(nueva, EstadoCarpeta.SubidaConF8, "javiera"));
        await Assert.ThrowsAsync<DomainException>(() => svc.ReagendarAsync(nueva, existente, "javiera"));

        await _db.NewService().RestaurarAsync(nueva, "raul");
        Assert.False((await _db.NewService().ObtenerAsync(nueva)).Anulada);
    }

    [Fact]
    public async Task Failed_single_operation_is_discarded_before_the_next_one()
    {
        var (_, nueva, otra) = Seed();
        using var ctx = _db.NewContext();
        var svc = ServicioDelCircuito(ctx);

        // Observation too long → rejected; then an unrelated valid save.
        await Assert.ThrowsAsync<DomainException>(() => svc.GuardarObservacionAsync(nueva, new string('x', 500), "javiera"));
        await svc.AsignarClasesAsync(otra, ClaseLicencia.B, "javiera");

        Assert.Null((await _db.NewService().ObtenerAsync(nueva)).IdoneidadMoral);
        Assert.Equal(ClaseLicencia.B, (await _db.NewService().ObtenerAsync(otra)).Clases);
    }

    [Fact]
    public async Task Global_stats_are_grouped_in_sql_and_exclude_cancelled()
    {
        var (existente, nueva, otra) = Seed();
        using (var ctx = _db.NewContext())
        {
            var c = new Ciudadano(Rut.Parse("22222222-2"), "PEDRO", "ROJAS");
            ctx.Carpetas.Add(Carpeta.CrearImportada(Guid.Empty, c, Sede.Placilla, new DateOnly(2025, 1, 1), null, null,
                EstadoCarpeta.Otorgado, Decision.Otorgado, "ALERTADA", "SUBIDA A CONASET", null, null, Antes));
            ctx.SaveChanges();
        }
        await _db.NewService().AnularAsync(otra, MotivosAnulacion.Duplicado, "javiera");

        var e = await _db.NewService().ObtenerEstadisticasAsync();

        Assert.Equal(3, e.TotalCarpetas);
        Assert.Equal(1, e.TotalOtorgadas);
        Assert.Equal(1, e.TotalAlertadas);
        var placilla = e.Sedes.Single(s => s.Sede == Sede.Placilla);
        Assert.Equal(2, placilla.Total);
        Assert.Equal(1, placilla.SubidasConaset);
        Assert.Equal(1, e.Sedes.Single(s => s.Sede == Sede.AvArgentina).Total);
        _ = (existente, nueva);
    }
}
