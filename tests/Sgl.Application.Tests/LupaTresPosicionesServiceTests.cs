using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

/// <summary>Tramo 5: lupa de 3 posiciones, «Subida a Conaset» y listado de búsqueda limitado a las marcadas en búsqueda.</summary>
public sealed class LupaTresPosicionesServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<Guid> EnArchivosAsync(string rut = "12345678-5", string nombre = "JUAN", string apellido = "PEREZ", string fecha = "2019-02-02")
    {
        Guid id;
        await using (var ctx = _db.NewContext())
        {
            var persona = new Ciudadano(Rut.Parse(rut), nombre, apellido);
            var c = Carpeta.CrearDesdeAgenda(persona, Sede.AvArgentina, new DateOnly(2026, 5, 4), new TimeOnly(9, 0), TramiteAgenda.Renovacion, "imp",
                new DateTime(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc));
            ctx.Carpetas.Add(c);
            await ctx.SaveChangesAsync();
            id = c.Id;
        }
        await _db.NewService().ClasificarAsync(new ClasificarCommand([id], null, ClaseLicencia.B, fecha, null, "Funcionaria Prueba"));
        return id;
    }

    private async Task<CarpetaResumenDto> ResumenAsync(Guid id) =>
        (await _db.NewService().ListarAsync(new ListarCarpetasQuery { Ids = [id] })).Items.Single();

    [Fact]
    public async Task AvanzarLupa_cicla_las_tres_posiciones_y_queda_en_el_historial()
    {
        var id = await EnArchivosAsync();

        await _db.NewService().AvanzarLupaAsync(id, "ana");
        var r = await ResumenAsync(id);
        Assert.True(r.PendienteBusqueda);
        Assert.False(r.CarpetaEncontrada);

        await _db.NewService().AvanzarLupaAsync(id, "luis");
        r = await ResumenAsync(id);
        Assert.False(r.PendienteBusqueda);
        Assert.True(r.CarpetaEncontrada);

        await _db.NewService().AvanzarLupaAsync(id, "marta");
        r = await ResumenAsync(id);
        Assert.False(r.PendienteBusqueda);
        Assert.False(r.CarpetaEncontrada);

        var detalle = await _db.NewService().ObtenerAsync(id);
        Assert.Contains(detalle.Historial, h => h.Campo == "CarpetaEncontrada" && h.Nuevo == "Si" && h.Usuario == "luis");
        Assert.Contains(detalle.Historial, h => h.Campo == "CarpetaEncontrada" && h.Nuevo == "No" && h.Usuario == "marta");
    }

    [Fact]
    public async Task AvanzarLupa_fuera_de_los_estados_permitidos_lanza_y_no_guarda()
    {
        var id = await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand("12.345.678-5", "Juan", "Pérez", Sede.AvArgentina, new DateOnly(2026, 5, 4), "ana"));

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().AvanzarLupaAsync(id, "ana"));

        var r = await ResumenAsync(id);
        Assert.False(r.PendienteBusqueda);
        Assert.False(r.CarpetaEncontrada);
    }

    [Fact]
    public async Task SubidaConaset_pasa_a_SubidaConaset_estampa_fecha_de_hoy_limpia_la_lupa_y_se_puede_deshacer()
    {
        var id = await EnArchivosAsync();
        await _db.NewService().AvanzarLupaAsync(id, "ana");
        await _db.NewService().AvanzarLupaAsync(id, "ana"); // encontrada

        var punto = await _db.NewService().MarcarSubidaConasetAsync(id, "Funcionaria Prueba");

        var r = await ResumenAsync(id);
        Assert.Equal(EstadoCarpeta.SubidaConaset, r.Estado);
        Assert.Equal(DateOnly.FromDateTime(_db.Clock.GetUtcNow().UtcDateTime.AddHours(-4)), r.FechaSubida); // 10:00 UTC = 06:00 Chile (UTC-4)
        Assert.False(r.PendienteBusqueda);
        Assert.False(r.CarpetaEncontrada);

        await _db.NewService().DeshacerEstadoAsync(id, punto, EstadoCarpeta.SubidaConaset, "Funcionaria Prueba");

        r = await ResumenAsync(id);
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, r.Estado);
        Assert.Null(r.FechaSubida);
        Assert.True(r.CarpetaEncontrada);
    }

    [Fact]
    public async Task SubidaConaset_desde_otro_estado_lanza_y_no_guarda()
    {
        var id = await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand("12.345.678-5", "Juan", "Pérez", Sede.AvArgentina, new DateOnly(2026, 5, 4), "ana"));

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().MarcarSubidaConasetAsync(id, "Funcionaria Prueba"));

        Assert.Equal(EstadoCarpeta.Citada, (await ResumenAsync(id)).Estado);
    }

    [Fact]
    public async Task Listado_solo_en_busqueda_excluye_sin_marca_y_encontradas_y_imprimir_no_cambia_la_marca()
    {
        var enBusqueda = await EnArchivosAsync("11111111-1", "ANA", "ZAMORA", "2023-06-01");
        var encontrada = await EnArchivosAsync("22222222-2", "LUIS", "ARAYA", "2019-02-02");
        var sinMarca = await EnArchivosAsync("33333333-3", "MARTA", "LIRA", "2021-03-03");
        await _db.NewService().AvanzarLupaAsync(enBusqueda, "ana");
        await _db.NewService().AvanzarLupaAsync(encontrada, "ana");
        await _db.NewService().AvanzarLupaAsync(encontrada, "ana");

        var todas = await _db.NewService().ObtenerListadoBusquedaAsync(Bandeja.SeEncuentraEnArchivos, new ListarCarpetasQuery());
        Assert.Equal(3, todas.Total);

        var soloBusqueda = await _db.NewService().ObtenerListadoBusquedaAsync(
            Bandeja.SeEncuentraEnArchivos, new ListarCarpetasQuery(), soloEnBusqueda: true);
        Assert.Equal([enBusqueda], soloBusqueda.Items.Select(i => i.Id).ToArray());
        Assert.Equal(1, soloBusqueda.Total);

        await _db.NewService().MarcarImpresasAsync(soloBusqueda.Items.Select(i => i.Id).ToList(), "Funcionaria Prueba");

        Assert.True((await ResumenAsync(enBusqueda)).PendienteBusqueda);
        Assert.True((await ResumenAsync(encontrada)).CarpetaEncontrada);
        Assert.False((await ResumenAsync(sinMarca)).PendienteBusqueda);
        Assert.NotNull((await ResumenAsync(enBusqueda)).FechaImpresion);
    }
}
