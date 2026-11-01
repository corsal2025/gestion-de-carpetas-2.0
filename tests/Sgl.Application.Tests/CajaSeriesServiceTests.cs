using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

/// <summary>Tramo 7: cola de caja por serie, duplicados, sugerencia del siguiente número y cola heredada. Datos sintéticos.</summary>
public sealed class CajaSeriesServiceTests : IDisposable
{
    private static readonly DateTime Antes = new(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = new();
    private int _rut = 12000000;

    public void Dispose() => _db.Dispose();

    private static string RutValido(int cuerpo)
    {
        var suma = 0;
        var factor = 2;
        for (var n = cuerpo; n > 0; n /= 10)
        {
            suma += n % 10 * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }
        var dv = 11 - suma % 11;
        return $"{cuerpo}-{(dv == 11 ? "0" : dv == 10 ? "K" : dv.ToString())}";
    }

    /// <summary>Siembra una carpeta; <paramref name="cajaHeredada"/> entra por la importación (sin validar), como las cajas antiguas.</summary>
    private async Task<Guid> SembrarAsync(
        EstadoCarpeta estado, Decision decision = Decision.Otorgado, string? cajaHeredada = null, bool anular = false)
    {
        await using var ctx = _db.NewContext();
        var persona = new Ciudadano(Rut.Parse(RutValido(_rut++)), "Persona", "Ficticia");
        var c = Carpeta.CrearImportada(Guid.NewGuid(), persona, Sede.AvArgentina, new DateOnly(2026, 4, 1), null, null,
            estado, decision, null, null, cajaHeredada, null, Antes);
        if (anular) c.Anular("Duplicado", "setup", Antes);
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<string?> CajaDeAsync(Guid id) => (await _db.NewService().ObtenerAsync(id)).CajaArchivo;

    // B2: un rótulo antiguo equivalente («A 12») cuenta como la misma caja que «A-12».
    [Fact]
    public async Task Un_rotulo_antiguo_equivalente_cuenta_como_caja_duplicada()
    {
        await SembrarAsync(EstadoCarpeta.SubidaConaset, cajaHeredada: "A 12");
        var nueva = await SembrarAsync(EstadoCarpeta.SubidaConaset);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(nueva, "A-12", "ana")));

        Assert.Contains("Ya existe la caja A-12", ex.Message);
        Assert.Null(await CajaDeAsync(nueva));

        await _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(nueva, "A-12", "ana", AgregarAExistente: true));
        Assert.Equal("A-12", await CajaDeAsync(nueva));
    }

    [Fact]
    public async Task Editar_la_caja_desde_el_formulario_tambien_comprueba_duplicado_salvo_confirmacion()
    {
        await SembrarAsync(EstadoCarpeta.SubidaConaset, cajaHeredada: "A-12");
        var nueva = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        var d = await _db.NewService().ObtenerAsync(nueva);
        EditarCarpetaCommand Cmd(bool agregar) =>
            new(nueva, d.Sede, d.FechaCitacion, d.FechaSubida, d.IdoneidadMoral, "ana", Caja: "a 12", CambiarCaja: true, AgregarAExistenteCaja: agregar);

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().EditarAsync(Cmd(false)));
        Assert.Null(await CajaDeAsync(nueva));

        await _db.NewService().EditarAsync(Cmd(true));
        Assert.Equal("A-12", await CajaDeAsync(nueva));
    }

    // Criterio 4
    [Fact]
    public async Task La_cola_de_cada_serie_solo_tiene_carpetas_cerradas_sin_caja_o_en_cola_y_no_anuladas()
    {
        var normal = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        var normalDenegada = await SembrarAsync(EstadoCarpeta.PrimeraLicencia, Decision.Denegado);
        var normalEnCola = await SembrarAsync(EstadoCarpeta.CanjeLicExtranjera, cajaHeredada: Carpeta.ColaDeCaja);
        var cd = await SembrarAsync(EstadoCarpeta.CambioDomSubidoConaset);
        var cdCorreo = await SembrarAsync(EstadoCarpeta.CambioDomSubidoCorreo, cajaHeredada: Carpeta.ColaDeCaja);
        var puc = await SembrarAsync(EstadoCarpeta.SubidaConF8);

        // Fuera de toda cola
        await SembrarAsync(EstadoCarpeta.SubidaConaset, Decision.Pendiente);
        await SembrarAsync(EstadoCarpeta.CambioDomicilio, Decision.Pendiente);
        await SembrarAsync(EstadoCarpeta.SubidaConaset, cajaHeredada: "A-12");
        await SembrarAsync(EstadoCarpeta.SubidaConaset, anular: true);
        await SembrarAsync(EstadoCarpeta.SubidaConF8, anular: true);

        var svc = _db.NewService();
        var colaNormal = await svc.ListarColaCajaAsync(SerieCaja.Normal);
        var colaCd = await svc.ListarColaCajaAsync(SerieCaja.CambioDomicilio);
        var colaPuc = await svc.ListarColaCajaAsync(SerieCaja.PenultimaCarpeta);

        Assert.Equal(new[] { normal, normalDenegada, normalEnCola }.Order(), colaNormal.Select(c => c.Id).Order());
        Assert.Equal(new[] { cd, cdCorreo }.Order(), colaCd.Select(c => c.Id).Order());
        Assert.Equal([puc], colaPuc.Select(c => c.Id));

        var conteo = await svc.ContarColasCajaAsync();
        Assert.Equal(3, conteo.Normal);
        Assert.Equal(2, conteo.CambioDomicilio);
        Assert.Equal(1, conteo.PenultimaCarpeta);
    }

    [Fact]
    public async Task Asignar_a_una_caja_lo_saca_de_la_cola_y_rechaza_mezclar_series_en_el_lote()
    {
        var a = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        var b = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        var cd = await SembrarAsync(EstadoCarpeta.CambioDomSubidoConaset);

        await _db.NewService().AsignarCajaLoteAsync(new AsignarCajaLoteCommand([a, b], "a 12", "Funcionaria Prueba"));
        Assert.Equal("A-12", await CajaDeAsync(a));
        Assert.Equal("A-12", await CajaDeAsync(b));
        Assert.Empty(await _db.NewService().ListarColaCajaAsync(SerieCaja.Normal));

        var otra = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().AsignarCajaLoteAsync(new AsignarCajaLoteCommand([otra, cd], "B-1", "Funcionaria Prueba")));
        Assert.Contains("serie", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Todo o nada: ni siquiera la carpeta válida del lote quedó en la caja.
        Assert.Null(await CajaDeAsync(otra));
        Assert.Null(await CajaDeAsync(cd));
    }

    [Fact]
    public async Task Una_serie_equivocada_se_informa_antes_que_el_duplicado()
    {
        var normal = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        var cd = await SembrarAsync(EstadoCarpeta.CambioDomSubidoConaset);
        await _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(normal, "A-12", "Funcionaria Prueba"));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(cd, "A-12", "Funcionaria Prueba")));

        Assert.Contains("serie", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ya existe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Criterio 5
    [Fact]
    public async Task Un_rotulo_equivalente_a_uno_existente_se_rechaza_como_duplicado_salvo_agregar_a_existente()
    {
        var primera = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        var segunda = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        var tercera = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        await _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(primera, "A-12", "Funcionaria Prueba"));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(segunda, "a 12", "Funcionaria Prueba")));
        Assert.Contains("ya existe", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await CajaDeAsync(segunda));

        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService().AsignarCajaLoteAsync(new AsignarCajaLoteCommand([segunda, tercera], "A-12", "Funcionaria Prueba")));

        await _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(segunda, "a 12", "Funcionaria Prueba", AgregarAExistente: true));
        Assert.Equal("A-12", await CajaDeAsync(segunda));
        await _db.NewService().AsignarCajaLoteAsync(new AsignarCajaLoteCommand([tercera], "A-12", "Funcionaria Prueba", AgregarAExistente: true));
        Assert.Equal("A-12", await CajaDeAsync(tercera));
    }

    // Criterio 6
    [Fact]
    public async Task La_sugerencia_es_el_maximo_numero_de_la_letra_y_serie_mas_uno_y_nunca_se_asigna()
    {
        foreach (var (estado, rotulo) in new[]
        {
            (EstadoCarpeta.SubidaConaset, "A-3"), (EstadoCarpeta.SubidaConaset, "A-12"), (EstadoCarpeta.SubidaConaset, "B-40"),
            (EstadoCarpeta.CambioDomSubidoConaset, "A-5-CD"), (EstadoCarpeta.SubidaConF8, "A-7-PUC"),
        })
        {
            var id = await SembrarAsync(estado);
            await _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(id, rotulo, "Funcionaria Prueba"));
        }
        var pendiente = await SembrarAsync(EstadoCarpeta.SubidaConaset);

        var svc = _db.NewService();
        Assert.Equal("A-13", await svc.SugerirSiguienteCajaAsync(SerieCaja.Normal, "a"));
        Assert.Equal("B-41", await svc.SugerirSiguienteCajaAsync(SerieCaja.Normal, "B"));
        Assert.Equal("C-1", await svc.SugerirSiguienteCajaAsync(SerieCaja.Normal, "C"));
        Assert.Equal("A-6-CD", await svc.SugerirSiguienteCajaAsync(SerieCaja.CambioDomicilio, "A"));
        Assert.Equal("A-8-PUC", await svc.SugerirSiguienteCajaAsync(SerieCaja.PenultimaCarpeta, "A"));
        Assert.Equal("B-41", await svc.SugerirSiguienteCajaAsync(SerieCaja.Normal, null));
        Assert.Equal("A-6-CD", await svc.SugerirSiguienteCajaAsync(SerieCaja.CambioDomicilio, null));

        Assert.Null(await CajaDeAsync(pendiente));
        Assert.Single(await svc.ListarColaCajaAsync(SerieCaja.Normal));
    }

    [Fact]
    public async Task Sin_cajas_de_la_serie_la_sugerencia_empieza_en_uno()
    {
        var svc = _db.NewService();

        Assert.Equal("A-1", await svc.SugerirSiguienteCajaAsync(SerieCaja.Normal, null));
        Assert.Equal("A-1-CD", await svc.SugerirSiguienteCajaAsync(SerieCaja.CambioDomicilio, null));
        Assert.Equal("Z-1-PUC", await svc.SugerirSiguienteCajaAsync(SerieCaja.PenultimaCarpeta, "z"));
    }

    // Criterio 7
    [Fact]
    public async Task Las_carpetas_hoy_en_cola_de_caja_sin_decision_no_se_modifican_y_se_cuentan_aparte()
    {
        var heredada = await SembrarAsync(EstadoCarpeta.SubidaConaset, Decision.Pendiente, Carpeta.ColaDeCaja);
        var heredada2 = await SembrarAsync(EstadoCarpeta.CambioDomSubidoConaset, Decision.Pendiente, Carpeta.ColaDeCaja);
        await SembrarAsync(EstadoCarpeta.SubidaConaset, Decision.Pendiente, Carpeta.ColaDeCaja, anular: true);
        await SembrarAsync(EstadoCarpeta.SubidaConaset);

        var svc = _db.NewService();
        var conteo = await svc.ContarColasCajaAsync();

        Assert.Equal(2, conteo.SinDecisionEnCola);
        Assert.Equal(1, conteo.Normal);
        Assert.Equal(0, conteo.CambioDomicilio);
        Assert.DoesNotContain(await svc.ListarColaCajaAsync(SerieCaja.Normal), c => c.Id == heredada);
        Assert.Equal(Carpeta.ColaDeCaja, await CajaDeAsync(heredada));
        Assert.Equal(Carpeta.ColaDeCaja, await CajaDeAsync(heredada2));
    }

    [Fact]
    public async Task Las_cajas_antiguas_fuera_de_formato_siguen_visibles_y_se_pueden_reabrir()
    {
        var antigua = await SembrarAsync(EstadoCarpeta.SubidaConaset, Decision.Pendiente, "CAJA AA-48");

        var cajas = await _db.NewService().ObtenerCajasAsync();
        Assert.Contains(cajas, c => c.Caja == "CAJA AA-48" && c.TotalCarpetas == 1);
        Assert.Single(await _db.NewService().ObtenerCarpetasDeCajaAsync("CAJA AA-48"));

        await _db.NewService().ReabrirCajaAsync("CAJA AA-48", "Funcionaria Prueba");

        Assert.Equal(Carpeta.ColaDeCaja, await CajaDeAsync(antigua));
    }

    [Fact]
    public async Task Quitar_la_caja_sigue_permitido()
    {
        var id = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        await _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(id, "A-1", "Funcionaria Prueba"));

        await _db.NewService().AsignarCajaAsync(new AsignarCajaCommand(id, null, "Funcionaria Prueba"));

        Assert.Null(await CajaDeAsync(id));
    }
}
