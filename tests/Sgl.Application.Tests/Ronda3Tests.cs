using Sgl.Application.Carpetas;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class Ronda3Tests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static readonly DateTime Antes = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private (Guid A, Guid B) Seed()
    {
        using var ctx = _db.NewContext();
        var juan = new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ");
        var ana = new Ciudadano(Rut.Parse("11111111-1"), "ANA", "SOTO");
        var a = Carpeta.Registrar(juan, Sede.Placilla, new DateOnly(2026, 4, 2), "x", Antes, ClaseLicencia.B);
        var b = Carpeta.Registrar(ana, Sede.Placilla, new DateOnly(2026, 4, 3), "x", Antes, ClaseLicencia.B);
        ctx.Carpetas.AddRange(a, b);
        ctx.SaveChanges();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task Editar_with_caja_and_clases_is_saved_all_together()
    {
        var (a, _) = Seed();

        await _db.NewService().EditarAsync(new EditarCarpetaCommand(a, Sede.MercadoPuerto, new DateOnly(2026, 4, 2), null, "OBS", "javiera",
            Caja: "CAJA 9", CambiarCaja: true, Clases: ClaseLicencia.C));

        var det = await _db.NewService().ObtenerAsync(a);
        Assert.Equal(Sede.MercadoPuerto, det.Sede);
        Assert.Equal("CAJA 9", det.CajaArchivo);
        Assert.Equal(ClaseLicencia.C, det.Clases);
    }

    [Fact]
    public async Task Editar_is_all_or_nothing_when_one_part_is_invalid()
    {
        var (a, _) = Seed();

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().EditarAsync(new EditarCarpetaCommand(
            a, Sede.MercadoPuerto, new DateOnly(2026, 4, 2), null, "OBS", "javiera",
            Caja: "CAJA 9", CambiarCaja: true, Clases: (ClaseLicencia)(1 << 20))));

        var det = await _db.NewService().ObtenerAsync(a);
        Assert.Equal(Sede.Placilla, det.Sede);
        Assert.Null(det.CajaArchivo);
        Assert.Equal(ClaseLicencia.B, det.Clases);
    }

    [Fact]
    public async Task Concurrent_writes_on_one_circuit_service_are_serialized()
    {
        var (a, b) = Seed();
        using var ctx = _db.NewContext();
        var svc = new CarpetaService(new CarpetaRepository(ctx), _db.Clock);

        await Task.WhenAll(
            Enumerable.Range(0, 10).Select(i => svc.GuardarObservacionAsync(i % 2 == 0 ? a : b, $"OBS {i}", "javiera")));

        Assert.StartsWith("OBS", (await _db.NewService().ObtenerAsync(a)).IdoneidadMoral);
        Assert.StartsWith("OBS", (await _db.NewService().ObtenerAsync(b)).IdoneidadMoral);
    }

    [Fact]
    public async Task Coalesced_refresh_never_loses_a_request_arriving_at_the_end()
    {
        var ejecuciones = 0;
        var gate = new TaskCompletionSource();
        RefrescoCoalescido? r = null;
        r = new RefrescoCoalescido(async () =>
        {
            ejecuciones++;
            if (ejecuciones == 1) await gate.Task;
        });

        var primera = r.SolicitarAsync();
        var segunda = r.SolicitarAsync(); // arrives while the first runs → must run once more
        gate.SetResult();
        await Task.WhenAll(primera, segunda);
        await r.SolicitarAsync();

        Assert.Equal(3, ejecuciones);
    }
}
