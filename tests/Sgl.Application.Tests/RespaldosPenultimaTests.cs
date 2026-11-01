using Sgl.Application;
using Sgl.Domain;
using Sgl.Infrastructure.Carpetas;
using Microsoft.EntityFrameworkCore;

namespace Sgl.Application.Tests;

public sealed class RespaldosPenultimaTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();

    private RespaldosPenultimaService Servicio() => new(_db.NewContext(), _db.Clock, new UnidadDeTrabajoGate());

    [Fact]
    public async Task Solo_las_agregadas_expresamente_se_muestran_y_la_subida_no_mueve_estado()
    {
        var c = Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("12345678-5"), "Persona", "Prueba"), Sede.AvArgentina,
            new DateOnly(2026, 5, 1), null, TramiteAgenda.Renovacion, "ana", DateTime.UtcNow);
        using (var db = _db.NewContext()) { db.Carpetas.Add(c); await db.SaveChangesAsync(); }
        Assert.Empty(await Servicio().ListarPendientesAsync());
        await Servicio().AgregarAsync(c.Id, "ana");
        Assert.Single(await Servicio().ListarPendientesAsync());
        await Servicio().MarcarSubidosAsync([c.Id], "ana");
        Assert.Empty(await Servicio().ListarPendientesAsync());
        using var check = _db.NewContext();
        var actual = check.Carpetas.Single();
        Assert.Equal(EstadoCarpeta.PorClasificar, actual.Estado);
        Assert.NotNull(actual.FechaRespaldoSubido);
        Assert.Contains(check.Historial, h => h.CarpetaId == c.Id && h.Campo == "RespaldoPenultima" && h.Nuevo == "Subido");
    }

    [Fact]
    public async Task Lote_con_una_carpeta_sin_pendiente_no_marca_ninguna()
    {
        var a = Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("12345678-5"), "Uno", "Prueba"), Sede.AvArgentina, new DateOnly(2026, 5, 1), null, null, "ana", DateTime.UtcNow);
        var b = Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("11111111-1"), "Dos", "Prueba"), Sede.AvArgentina, new DateOnly(2026, 5, 1), null, null, "ana", DateTime.UtcNow);
        using (var db = _db.NewContext()) { db.Carpetas.AddRange(a, b); await db.SaveChangesAsync(); }
        await Servicio().AgregarAsync(a.Id, "ana");
        await Assert.ThrowsAsync<DomainException>(() => Servicio().MarcarSubidosAsync([a.Id, b.Id], "ana"));
        Assert.Single(await Servicio().ListarPendientesAsync());
    }

    [Fact]
    public async Task Se_puede_cancelar_un_respaldo_sin_marcar_una_subida_falsa()
    {
        var c = Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("12345678-5"), "Persona", "Prueba"), Sede.AvArgentina, new DateOnly(2026, 5, 1), null, null, "ana", DateTime.UtcNow);
        using (var db = _db.NewContext()) { db.Carpetas.Add(c); await db.SaveChangesAsync(); }
        await Servicio().AgregarAsync(c.Id, "ana");
        await Servicio().CancelarAsync(c.Id, "ana");
        Assert.Empty(await Servicio().ListarPendientesAsync());
        using var check = _db.NewContext();
        Assert.Null(check.Carpetas.Single().FechaRespaldoSubido);
        Assert.Contains(check.Historial, h => h.Campo == "RespaldoPenultima" && h.Nuevo == "Cancelado");
    }

    [Fact]
    public async Task Dos_contextos_no_pueden_resolver_el_mismo_pendiente()
    {
        var c = Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("12345678-5"), "Persona", "Prueba"), Sede.AvArgentina, new DateOnly(2026, 5, 1), null, null, "ana", DateTime.UtcNow);
        using (var db = _db.NewContext()) { db.Carpetas.Add(c); await db.SaveChangesAsync(); }
        await Servicio().AgregarAsync(c.Id, "ana");
        using var primero = _db.NewContext();
        using var segundo = _db.NewContext();
        var a = await primero.Carpetas.SingleAsync();
        var b = await segundo.Carpetas.SingleAsync();
        a.MarcarRespaldoPenultimaSubido("ana", DateTime.UtcNow);
        b.CancelarRespaldoPenultima("ana", DateTime.UtcNow);
        await primero.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<ConcurrenciaException>(() => segundo.SaveChangesAsync());
        Assert.Contains("actualice", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Anular_oculta_el_pendiente_y_restaurar_lo_recupera()
    {
        var c = Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("12345678-5"), "Persona", "Prueba"), Sede.AvArgentina, new DateOnly(2026, 5, 1), null, null, "ana", DateTime.UtcNow);
        using (var db = _db.NewContext()) { db.Carpetas.Add(c); await db.SaveChangesAsync(); }
        await Servicio().AgregarAsync(c.Id, "ana");
        using (var db = _db.NewContext()) { var carpeta = db.Carpetas.Single(); carpeta.Anular("Duplicado", "ana", DateTime.UtcNow); await db.SaveChangesAsync(); }
        Assert.Empty(await Servicio().ListarPendientesAsync());
        using (var db = _db.NewContext()) { var carpeta = db.Carpetas.IgnoreQueryFilters().Single(); carpeta.Restaurar("ana", DateTime.UtcNow); await db.SaveChangesAsync(); }
        Assert.Single(await Servicio().ListarPendientesAsync());
    }
}
