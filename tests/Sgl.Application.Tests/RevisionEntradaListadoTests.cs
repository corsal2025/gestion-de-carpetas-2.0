using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Application.Panel;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>ENTRADA / REVISIÓN split over the real SQLite pipeline: listing, per-tray counters and home panel agree.</summary>
public sealed class RevisionEntradaListadoTests : IDisposable
{
    private static readonly DateOnly Hoy = new(2026, 5, 4); // TestDb clock (Chile date)
    private static readonly DateTime Ayer = new(2026, 5, 3, 15, 0, 0, DateTimeKind.Utc);
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private Guid Agenda(string rut, string nombre, TramiteAgenda? tramite = TramiteAgenda.Renovacion, DateOnly? cita = null)
    {
        using var ctx = _db.NewContext();
        var ciudadano = ctx.Ciudadanos.AsEnumerable().FirstOrDefault(c => c.Rut.Value == rut) ?? new Ciudadano(Rut.Parse(rut), nombre, "PRUEBA");
        var c = Carpeta.CrearDesdeAgenda(ciudadano, Sede.AvArgentina, cita ?? Hoy, null, tramite, "imp", Ayer);
        ctx.Carpetas.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    private void ForzarRut(Guid carpetaId, string rut)
    {
        using var ctx = _db.NewContext();
        var ciudadanoId = ctx.Carpetas.Where(c => c.Id == carpetaId).Select(c => c.CiudadanoId).Single();
        ctx.Database.ExecuteSqlRaw("UPDATE Ciudadanos SET Rut = {0} WHERE Id = {1}", rut, ciudadanoId);
    }

    private async Task<IReadOnlyList<Guid>> IdsDe(Bandeja b) =>
        (await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: b, Take: 100))).Items.Select(i => i.Id).ToList();

    private async Task<(Guid Limpia, Guid Dup1, Guid Dup2, Guid RutMalo, Guid SinRut, Guid NoIdent, Guid Futura, Guid Citada)> SembrarAsync()
    {
        var limpia = Agenda("12345678-5", "JUAN");
        var dup1 = Agenda("11111111-1", "ANA");
        var dup2 = Agenda("11111111-1", "ANA", cita: Hoy.AddDays(-3));
        var rutMalo = Agenda("22222222-2", "PEDRO");
        ForzarRut(rutMalo, "22222222-3");
        var sinRut = Agenda("33333333-3", "LUIS");
        ForzarRut(sinRut, "");
        var noIdent = Agenda("44444444-4", "EVA", tramite: null);
        var futura = Agenda("55555555-5", "SOL", cita: Hoy.AddDays(2));
        var citada = await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand("66666666-6", "RAUL", "PRUEBA", Sede.Placilla, Hoy, "ana"));
        return (limpia, dup1, dup2, rutMalo, sinRut, noIdent, futura, citada);
    }

    [Fact]
    public async Task Each_carpeta_is_listed_in_exactly_one_Entrada_tray()
    {
        var s = await SembrarAsync();

        Assert.Equal([s.Limpia], await IdsDe(Bandeja.PorClasificar));
        Assert.Equal(new[] { s.Dup1, s.Dup2 }.Order(), (await IdsDe(Bandeja.RevisionDuplicadas)).Order());
        Assert.Equal([s.RutMalo], await IdsDe(Bandeja.RevisionRutInvalido));
        Assert.Equal([s.SinRut], await IdsDe(Bandeja.RevisionSinRut));
        Assert.Equal([s.NoIdent], await IdsDe(Bandeja.RevisionNoIdentificada));
        Assert.Equal([s.Futura], await IdsDe(Bandeja.RevisionNoEvaluadas));
        Assert.Equal([s.Citada], await IdsDe(Bandeja.ParaRevision));
    }

    [Fact]
    public async Task Counters_split_Entrada_and_keep_the_total()
    {
        await SembrarAsync();

        var conteos = await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery());

        Assert.Equal(1, conteos[Bandeja.PorClasificar]);
        Assert.Equal(2, conteos[Bandeja.RevisionDuplicadas]);
        Assert.Equal(1, conteos[Bandeja.RevisionRutInvalido]);
        Assert.Equal(1, conteos[Bandeja.RevisionSinRut]);
        Assert.Equal(1, conteos[Bandeja.RevisionNoIdentificada]);
        Assert.Equal(1, conteos[Bandeja.RevisionNoEvaluadas]);
        Assert.Equal(1, conteos[Bandeja.ParaRevision]);
        Assert.Equal(8, conteos.Values.Sum());
    }

    [Fact]
    public async Task Home_panel_counts_match_the_listing_and_integrity_stays_clean()
    {
        await SembrarAsync();
        var conteos = await _db.NewService().ContarPorBandejaAsync(new ListarCarpetasQuery());

        var panel = await new PanelService(new PanelQuery(_db.NewContext(), _db.Clock), new DuplicadoRepository(_db.NewContext()), _db.Clock, new BandejasOptions()).ObtenerAsync();

        foreach (var b in RevisionEntrada.Submodulos.Append(Bandeja.PorClasificar))
        {
            Assert.Equal(conteos[b], panel.Modulos.Single(m => m.Bandeja == b).Conteo);
        }
        Assert.Equal(1, panel.Meta.Pendientes);
        Assert.Equal(1, await new PanelQuery(_db.NewContext(), _db.Clock).PendientesPorClasificarAsync());
    }

    [Fact]
    public async Task Fixed_duplicate_leaves_Revision_and_returns_to_Entrantes()
    {
        var s = await SembrarAsync();

        await _db.NewService().AnularAsync(s.Dup2, MotivosAnulacion.Duplicado, "javiera");

        Assert.Empty(await IdsDe(Bandeja.RevisionDuplicadas));
        Assert.Contains(s.Dup1, await IdsDe(Bandeja.PorClasificar));
    }

    [Fact]
    public async Task Mantener_ambas_review_clears_the_duplicate_problem()
    {
        var s = await SembrarAsync();
        Guid ciudadano;
        using (var ctx = _db.NewContext()) ciudadano = ctx.Carpetas.Where(c => c.Id == s.Dup1).Select(c => c.CiudadanoId).Single();

        await _db.NewDuplicadosService().MarcarRevisadoAsync(ciudadano, "javiera");

        Assert.Empty(await IdsDe(Bandeja.RevisionDuplicadas));
        Assert.Equal(2, (await IdsDe(Bandeja.PorClasificar)).Count(id => id == s.Dup1 || id == s.Dup2));
    }

    [Fact]
    public async Task Duplicate_rows_carry_the_red_alert_also_for_citadas()
    {
        var citada = await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand("11111111-1", "ANA", "PRUEBA", Sede.Placilla, Hoy, "ana"));
        var agenda = Agenda("11111111-1", "ANA");

        var filas = (await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.RevisionDuplicadas, Take: 100))).Items;

        Assert.Equal(new[] { agenda, citada }.Order(), filas.Select(f => f.Id).Order());
        Assert.All(filas, f => Assert.NotNull(f.AlertaDuplicado));
    }
}
