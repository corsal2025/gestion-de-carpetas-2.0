using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class LegacyComunaRepairTests
{
    private static async Task<Guid> SeedAsync(TestDb db, string rut, string estado, string? legacy)
    {
        var r = await db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(rut, "N", "A" + rut[..3], Sede.AvArgentina, new DateOnly(2026, 5, 1), "ana"));
        using var ctx = db.NewContext();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE Carpetas SET Estado = {0}, FechaUltimaCarpeta = {1} WHERE Id = {2}", estado, legacy ?? (object)DBNull.Value, r.ToString());
        return r;
    }

    [Fact]
    public async Task Moves_comuna_text_out_of_FechaUltimaCarpeta_and_is_idempotent()
    {
        using var db = new TestDb();
        var moved = await SeedAsync(db, "11111111-1", "CambioDomicilio", "quilpue");
        var unknown = await SeedAsync(db, "22222222-2", "CambioDomicilio", "ZZ NO EXISTE");
        var date = await SeedAsync(db, "33333333-3", "CambioDomicilio", "2024-03-05");
        var otherState = await SeedAsync(db, "44444444-4", "PrimeraLicencia", "VIÑA DEL MAR");

        var first = await LegacyComunaRepair.RepararAsync(db.NewContext());
        var second = await LegacyComunaRepair.RepararAsync(db.NewContext());

        using var ctx = db.NewContext();
        var c = await ctx.Carpetas.IgnoreQueryFilters().ToDictionaryAsync(x => x.Id);
        Assert.Equal("QUILPUÉ", c[moved].Comuna);
        Assert.Null(c[moved].FechaUltimaCarpeta);
        Assert.Equal("ZZ NO EXISTE", c[unknown].FechaUltimaCarpeta);
        Assert.Equal("2024-03-05", c[date].FechaUltimaCarpeta);
        Assert.Equal("VIÑA DEL MAR", c[otherState].FechaUltimaCarpeta);
        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task RepararYRegistrarAsync_logs_one_summary_row_with_counts_only()
    {
        using var db = new TestDb();
        await SeedAsync(db, "11111111-1", "CambioDomicilio", "quilpue");
        await SeedAsync(db, "22222222-2", "CambioDomicilio", "VIÑA DEL MAR");
        await SeedAsync(db, "33333333-3", "CambioDomicilio", "ZZ NO EXISTE");

        var reparadas = await LegacyComunaRepair.RepararYRegistrarAsync(db.NewContext(), db.Clock.GetUtcNow().UtcDateTime);

        using var ctx = db.NewContext();
        var log = await ctx.ImportacionLogs.SingleAsync();
        Assert.Equal(2, reparadas);
        Assert.Equal(LegacyComunaRepair.UsuarioRegistro, log.Usuario);
        Assert.Equal(3, log.FilasLeidas);
        Assert.Equal(0, log.CarpetasCreadas);
        Assert.Equal(2, log.CarpetasActualizadas);
        Assert.Equal(1, log.Errores);
        Assert.Equal(db.Clock.GetUtcNow().UtcDateTime, log.Fecha);
    }

    [Fact]
    public async Task RepararYRegistrarAsync_writes_nothing_when_there_is_nothing_to_repair()
    {
        using var db = new TestDb();
        await SeedAsync(db, "11111111-1", "CambioDomicilio", "2024-03-05");

        var reparadas = await LegacyComunaRepair.RepararYRegistrarAsync(db.NewContext(), db.Clock.GetUtcNow().UtcDateTime);

        using var ctx = db.NewContext();
        Assert.Equal(0, reparadas);
        Assert.False(await ctx.ImportacionLogs.AnyAsync());
    }
}
