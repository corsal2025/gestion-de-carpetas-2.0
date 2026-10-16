using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Application.Integridad;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>R12 end to end over a real (in-memory SQLite) database: runs, history, forced violations.</summary>
public sealed class IntegridadServicioTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private IntegridadService NewService()
    {
        var ctx = _db.NewContext();
        return new IntegridadService(new IntegridadRepository(ctx, new PanelQuery(ctx)), _db.Clock);
    }

    private async Task<Guid> RegistrarAsync(string rut, ClaseLicencia clases = ClaseLicencia.B, string apellido = "PEREZ") =>
        await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(
            rut, "JUAN", apellido, Sede.AvArgentina, new DateOnly(2026, 5, 1), "ana", clases));

    [Fact]
    public async Task Healthy_database_records_a_clean_run_with_counts_only()
    {
        await RegistrarAsync("11111111-1");
        await RegistrarAsync("22222222-2", ClaseLicencia.C);

        var r = await NewService().EjecutarAsync(OrigenIntegridad.Inicio);

        Assert.Equal(0, r.Corrida.Problemas);
        Assert.False(r.Corrida.TieneProblemas);
        Assert.Empty(r.Problemas);
        Assert.Equal("R1=0;R2=0;R3=0;R4=0;R5=0", r.Corrida.Detalle);
        var ultima = await NewService().UltimaAsync();
        Assert.Equal(OrigenIntegridad.Inicio, ultima!.Origen);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, ultima.Fecha);
    }

    [Fact]
    public async Task Cancelled_carpetas_never_count_as_a_problem_nor_appear_in_trays()
    {
        var id = await RegistrarAsync("11111111-1");
        await _db.NewService().AnularAsync(id, MotivosAnulacion.Duplicado, "ana");

        var r = await NewService().EjecutarAsync(OrigenIntegridad.Manual);

        Assert.Equal(0, r.Corrida.Problemas);
    }

    [Fact]
    public async Task A_forced_impossible_date_is_reported_with_its_carpeta_but_without_personal_data_in_the_run()
    {
        var id = await RegistrarAsync("11111111-1");
        using (var ctx = _db.NewContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE Carpetas SET FechaSubida = '2026-04-01' WHERE Id = {0}", id.ToString());
        }

        var r = await NewService().EjecutarAsync(OrigenIntegridad.Manual);

        var p = Assert.Single(r.Problemas);
        Assert.Equal(ReglaIntegridad.FechasImposibles, p.Regla);
        Assert.Equal(id, p.CarpetaId);
        Assert.NotNull(p.Rut);
        Assert.Equal(1, r.PorRegla[ReglaIntegridad.FechasImposibles]);
        Assert.Equal(1, r.Corrida.Problemas);
        Assert.DoesNotContain("11111111", r.Corrida.Detalle);
    }

    [Fact]
    public async Task A_forced_same_class_duplicate_is_found_and_clears_once_one_is_cancelled()
    {
        var a = await RegistrarAsync("11111111-1");
        await RegistrarAsync("22222222-2", ClaseLicencia.B, "SOTO");
        var b = await RegistrarAsync("33333333-3", ClaseLicencia.C, "DIAZ");
        using (var ctx = _db.NewContext())
        {
            // Forced violation: b's citizen becomes a's citizen while both are open with a shared class.
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE Carpetas SET CiudadanoId = (SELECT CiudadanoId FROM Carpetas WHERE Id = {0}), ClasesLicencia = 32 WHERE Id = {1}",
                a.ToString(), b.ToString());
        }

        var con = await NewService().EjecutarAsync(OrigenIntegridad.Manual);
        Assert.Equal(2, con.PorRegla[ReglaIntegridad.DuplicadoMismaClase]);

        await _db.NewService().AnularAsync(b, MotivosAnulacion.Duplicado, "ana");
        var sin = await NewService().EjecutarAsync(OrigenIntegridad.Manual);
        Assert.Equal(0, sin.Corrida.Problemas);
    }

    [Fact]
    public async Task History_lists_the_latest_runs_newest_first()
    {
        await NewService().EjecutarAsync(OrigenIntegridad.Inicio);
        _db.AdvanceClock(TimeSpan.FromHours(1));
        await NewService().EjecutarAsync(OrigenIntegridad.Importacion);

        var h = await NewService().HistorialAsync();

        Assert.Equal([OrigenIntegridad.Importacion, OrigenIntegridad.Inicio], h.Select(x => x.Origen).ToArray());
    }

    [Fact]
    public async Task A_failing_check_is_recorded_as_failed_never_as_all_clear()
    {
        var ctx = _db.NewContext();
        var repo = new FallaAlLeer(new IntegridadRepository(ctx, new PanelQuery(ctx)));
        var servicio = new IntegridadService(repo, _db.Clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EjecutarAsync(OrigenIntegridad.Diaria));

        var ultima = await NewService().UltimaAsync();
        Assert.True(ultima!.Fallo);
        Assert.True(ultima.TieneProblemas);
        Assert.Contains("InvalidOperationException", ultima.Detalle);
    }

    [Fact]
    public async Task Requests_after_an_import_are_coalesced_and_the_wait_times_out_to_the_daily_run()
    {
        var buzon = new IntegridadSolicitudes();
        buzon.Solicitar(OrigenIntegridad.Importacion);
        buzon.Solicitar(OrigenIntegridad.Importacion);

        var primera = await buzon.EsperarAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        var segunda = await buzon.EsperarAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.Equal(OrigenIntegridad.Importacion, primera);
        Assert.Null(segunda);
    }

    private sealed class FallaAlLeer(IIntegridadRepository inner) : IIntegridadRepository
    {
        public Task<EntradaIntegridad> ObtenerEntradaAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");

        public Task GuardarCorridaAsync(Sgl.Domain.IntegridadCorrida corrida, CancellationToken ct = default) =>
            inner.GuardarCorridaAsync(corrida, ct);

        public Task<Sgl.Domain.IntegridadCorrida?> UltimaCorridaAsync(CancellationToken ct = default) => inner.UltimaCorridaAsync(ct);

        public Task<IReadOnlyList<Sgl.Domain.IntegridadCorrida>> UltimasCorridasAsync(int cantidad, CancellationToken ct = default) =>
            inner.UltimasCorridasAsync(cantidad, ct);

        public Task<IReadOnlyDictionary<Guid, ProblemaIntegridadDto>> IdentificarAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            inner.IdentificarAsync(ids, ct);
    }
}
