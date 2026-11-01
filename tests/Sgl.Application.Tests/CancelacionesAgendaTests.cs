using Microsoft.EntityFrameworkCore;
using Sgl.Domain;
using Sgl.Infrastructure.Carpetas;

namespace Sgl.Application.Tests;

public sealed class CancelacionesAgendaTests
{
    [Fact]
    public async Task Pendiente_se_conserva_hasta_decision_explicita_y_restaurada_reaparece()
    {
        using var db = new TestDb();
        Guid id;
        using (var ctx = db.NewContext())
        {
            var c = Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("12345678-5"), "DEMO", "PRUEBA"), Sede.Placilla,
                new DateOnly(2026, 12, 30), null, TramiteAgenda.Renovacion, "test", db.Clock.GetUtcNow().UtcDateTime);
            c.RegistrarEvento("CitaCancelada", "ref", "Revisar", "test", db.Clock.GetUtcNow().UtcDateTime);
            ctx.Add(c); ctx.SaveChanges(); id = c.Id;
        }
        using var context = db.NewContext();
        var service = new CancelacionesAgendaService(context, new Sgl.Application.UnidadDeTrabajoGate(), db.Clock);
        Assert.Single(await service.ListarAsync());
        await service.ResolverAsync(id, false, "Cita confirmada por atención", "test");
        Assert.Empty(await service.ListarAsync());
        using var check = db.NewContext();
        Assert.False(check.Carpetas.Single().Anulada);
        Assert.Contains(check.Historial, h => h.Campo == "CitaCanceladaResuelta");
    }
    [Fact]
    public async Task Anular_y_restaurar_reabre_la_revision_de_cancelacion()
    {
        using var db = new TestDb();
        Guid id;
        using (var ctx = db.NewContext())
        {
            var c = Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("12345678-5"), "DEMO", "PRUEBA"), Sede.Placilla,
                new DateOnly(2026, 12, 30), null, TramiteAgenda.Renovacion, "test", db.Clock.GetUtcNow().UtcDateTime);
            c.RegistrarEvento("CitaCancelada", "ref", "Revisar", "test", db.Clock.GetUtcNow().UtcDateTime);
            ctx.Add(c); ctx.SaveChanges(); id = c.Id;
        }
        using var context = db.NewContext();
        var service = new CancelacionesAgendaService(context, new Sgl.Application.UnidadDeTrabajoGate(), db.Clock);
        await service.ResolverAsync(id, true, "Cita cancelada confirmada", "test");
        Assert.Empty(await service.ListarAsync());
        using (var ctx = db.NewContext())
        {
            ctx.Carpetas.IgnoreQueryFilters().Single().Restaurar("test", db.Clock.GetUtcNow().UtcDateTime);
            ctx.SaveChanges();
        }
        Assert.Single(await service.ListarAsync());
    }
}
