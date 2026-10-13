using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class EtapaBackfillTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task BlanquearAsync(params Guid[] ids)
    {
        using var ctx = _db.NewContext();
        foreach (var id in ids)
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE Carpetas SET FechaEstadoActual = '0001-01-01 00:00:00', FechaCierre = NULL WHERE Id = {0}", id.ToString());
        }
    }

    private async Task<Guid> RegistrarAsync(string rut) =>
        await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(rut, "N", "A", Sede.AvArgentina, new DateOnly(2026, 5, 1), "ana"));

    [Fact]
    public async Task Fills_stage_date_from_the_last_state_change_and_closing_date_from_the_audit()
    {
        var id = await RegistrarAsync("11111111-1");
        _db.AdvanceClock(TimeSpan.FromDays(2));
        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.SubidaConaset, "ana");
        var cambio = _db.Clock.GetUtcNow().UtcDateTime;
        await BlanquearAsync(id);

        var n = await EtapaBackfill.EjecutarAsync(_db.NewContext(), DateTime.UtcNow);

        var det = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(1, n);
        Assert.Equal(cambio, det.FechaEstadoActual);
        Assert.Equal(HoraChile.Fecha(cambio), det.FechaCierre);
    }

    [Fact]
    public async Task Uses_the_first_audit_entry_when_the_state_never_changed_and_invents_no_closing_date()
    {
        var id = await RegistrarAsync("11111111-1");
        var alta = _db.Clock.GetUtcNow().UtcDateTime;
        _db.AdvanceClock(TimeSpan.FromDays(5));
        await BlanquearAsync(id);

        await EtapaBackfill.EjecutarAsync(_db.NewContext(), DateTime.UtcNow);

        var det = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(alta, det.FechaEstadoActual);
        Assert.Null(det.FechaCierre);
    }

    [Fact]
    public async Task Is_idempotent_and_reports_pending_rows()
    {
        var id = await RegistrarAsync("11111111-1");
        await BlanquearAsync(id);
        Assert.Equal(1, await EtapaBackfill.PendientesAsync(_db.NewContext()));

        var primera = await EtapaBackfill.EjecutarAsync(_db.NewContext(), DateTime.UtcNow);
        var segunda = await EtapaBackfill.EjecutarAsync(_db.NewContext(), DateTime.UtcNow);

        Assert.Equal(1, primera);
        Assert.Equal(0, segunda);
        Assert.Equal(0, await EtapaBackfill.PendientesAsync(_db.NewContext()));
    }
}
