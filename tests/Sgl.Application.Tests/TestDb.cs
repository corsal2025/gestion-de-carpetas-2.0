using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sgl.Application.Asistencias;
using Sgl.Application.Carpetas;
using Sgl.Application.Solicitudes;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>Shared in-memory SQLite database; each context/service opens over the same connection.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public FixedTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero));

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    public SglDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SglDbContext>().UseSqlite(_connection).Options);

    /// <summary>Service over a fresh context so assertions never read stale tracked entities.</summary>
    public CarpetaService NewService(LicenciasOptions? options = null)
    {
        var ctx = NewContext();
        return new(new CarpetaRepository(ctx), Clock, options, solicitudesF8: new SolicitudF8Repository(ctx), certificados: new CertificadoRepository(ctx));
    }

    /// <summary>F8 service over one context shared by every repository (one unit of work, like a scoped circuit).</summary>
    public SolicitudesF8Service NewF8Service(INotificadorCertificado? notificador = null)
    {
        var ctx = NewContext();
        return new(new CarpetaRepository(ctx), new SolicitudF8Repository(ctx), new CertificadoRepository(ctx), new FirmantesRepository(ctx), Clock, notificador);
    }

    public CertificadosService NewCertificadosService(INotificadorCertificado? notificador = null)
    {
        var ctx = NewContext();
        return new(new CarpetaRepository(ctx), new CertificadoRepository(ctx), new FirmantesRepository(ctx), Clock, notificador);
    }

    /// <summary>Attendance service over one context shared with the carpeta service and one gate (like a scoped circuit).</summary>
    public AsistenciaService NewAsistenciaService()
    {
        var ctx = NewContext();
        var gate = new UnidadDeTrabajoGate();
        return new(new CarpetaRepository(ctx), new AsistenciaRepository(ctx), NewServiceCompartido(ctx, gate), Clock, gate);
    }

    /// <summary>Otorgamiento service over one context and one gate, with the given permission (like a scoped circuit).</summary>
    public Sgl.Application.Otorgamiento.OtorgamientoService NewOtorgamientoService(Sgl.Application.Seguridad.IPermisos permisos)
    {
        var ctx = NewContext();
        return new(new CarpetaRepository(ctx), new OtorgamientoRepository(ctx), permisos, Clock, new UnidadDeTrabajoGate());
    }

    public FirmantesService NewFirmantesService() => new(new FirmantesRepository(NewContext()), Clock);

    public DuplicadosService NewDuplicadosService() => new(new DuplicadoRepository(NewContext()), Clock);

    public void AdvanceClock(TimeSpan delta) => Clock.Advance(delta);

    /// <summary>Service over the given context, sharing the given gate with the other services of the same circuit.</summary>
    public CarpetaService NewServiceCompartido(SglDbContext ctx, UnidadDeTrabajoGate gate) =>
        new(new CarpetaRepository(ctx), Clock, solicitudesF8: new SolicitudF8Repository(ctx), certificados: new CertificadoRepository(ctx), gate: gate);

    public void Dispose() => _connection.Dispose();
}

public static class CarpetaServiceTestExtensions
{
    /// <summary>Sets the license classes through the detail edit (the only production path that changes them).</summary>
    public static async Task AsignarClasesAsync(this CarpetaService svc, Guid id, Sgl.Domain.ClaseLicencia clases, string autor)
    {
        var d = await svc.ObtenerAsync(id);
        await svc.EditarAsync(new EditarCarpetaCommand(id, d.Sede, d.FechaCitacion, d.FechaSubida, d.IdoneidadMoral, autor, Clases: clases));
    }
}

public static class EstadoFinalTestExtensions
{
    /// <summary>
    /// Leaves the carpeta in a legacy final state (Otorgado / Denegado) as the historical Excel load does: a final decision is
    /// no longer reachable through CambiarEstado / ForzarEstado, only through Otorgamiento or that load.
    /// </summary>
    public static async Task DejarEnEstadoFinalAsync(this TestDb db, Guid id, EstadoCarpeta final)
    {
        await using var ctx = db.NewContext();
        var carpeta = await ctx.Carpetas.SingleAsync(c => c.Id == id);
        carpeta.AplicarCargaHistorica(final, EstadoTransitions.DecisionFor(final), null, "setup", DateTime.UtcNow);
        await ctx.SaveChangesAsync();
    }
}

public static class CajaTestExtensions
{
    /// <summary>Gives the carpeta a final decision (an assignment to a box requires one) without going through Otorgamiento.</summary>
#pragma warning disable CS0618 // CambiarDecisionAsync es obsoleta para la interfaz; las pruebas de cajas solo necesitan la decisión final.
    public static Task DecidirAsync(this CarpetaService svc, Guid id) =>
        svc.CambiarDecisionAsync(id, Sgl.Domain.Decision.Otorgado, "setup");
#pragma warning restore CS0618
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
