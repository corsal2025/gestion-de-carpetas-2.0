using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sgl.Application.Carpetas;
using Sgl.Domain;
using Sgl.Infrastructure.Carpetas;

namespace Sgl.Application.Tests;

/// <summary>
/// E12 transversal: las carpetas digitales no van a caja, reimportar tras corregir un RUT no duplica, el importador del DETALLE
/// lee «SUBIO» y solo completa fechas vacías, y la asistencia sigue al RUT corregido. Datos 100% sintéticos.
/// </summary>
public sealed class TransversalDigitalYReimportacionTests : IDisposable
{
    private static readonly DateTime Antes = new(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);
    private const string RutOriginal = "12345678-5";
    private const string RutCorregido = "11111111-1";

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

    private async Task<Guid> SembrarAsync(EstadoCarpeta estado, string? fechaUltima, Decision decision = Decision.Otorgado)
    {
        await using var ctx = _db.NewContext();
        var persona = new Ciudadano(Rut.Parse(RutValido(_rut++)), "Persona", "Ficticia");
        var c = Carpeta.CrearImportada(Guid.NewGuid(), persona, Sede.AvArgentina, new DateOnly(2026, 4, 1), null, fechaUltima,
            estado, decision, null, null, null, null, Antes);
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    // ---- Criterio 2: colas de caja ----

    [Fact]
    public async Task Una_carpeta_otorgada_con_ultima_carpeta_digital_no_entra_a_ninguna_cola_de_caja()
    {
        var normalFisica = await SembrarAsync(EstadoCarpeta.SubidaConaset, "2026-07-31");
        var normalDigital = await SembrarAsync(EstadoCarpeta.SubidaConaset, "2026-08-15");
        var normalDigitalLimite = await SembrarAsync(EstadoCarpeta.SubidaConaset, "2026-08-01");
        var cdDigital = await SembrarAsync(EstadoCarpeta.CambioDomSubidoConaset, "2026-09-01");
        var cdFisica = await SembrarAsync(EstadoCarpeta.CambioDomSubidoConaset, "2025-01-10");
        var pucDigital = await SembrarAsync(EstadoCarpeta.SubidaConF8, "2026-10-01");
        var pucFisica = await SembrarAsync(EstadoCarpeta.SubidaConF8, "2024-05-05");
        // Sin fecha o con texto antiguo no se tocan: siguen en la cola.
        var primeraSinFecha = await SembrarAsync(EstadoCarpeta.PrimeraLicencia, null, Decision.Denegado);
        var textoLibre = await SembrarAsync(EstadoCarpeta.SubidaConaset, "no recuerda");
        var textoDiaPrimero = await SembrarAsync(EstadoCarpeta.SubidaConaset, "31-12-2023");

        var svc = _db.NewService();
        var colaNormal = (await svc.ListarColaCajaAsync(SerieCaja.Normal)).Select(c => c.Id).Order().ToList();
        var colaCd = (await svc.ListarColaCajaAsync(SerieCaja.CambioDomicilio)).Select(c => c.Id).Order().ToList();
        var colaPuc = (await svc.ListarColaCajaAsync(SerieCaja.PenultimaCarpeta)).Select(c => c.Id).Order().ToList();

        Assert.Equal(new[] { normalFisica, primeraSinFecha, textoLibre, textoDiaPrimero }.Order(), colaNormal);
        Assert.Equal([cdFisica], colaCd);
        Assert.Equal([pucFisica], colaPuc);
        Assert.DoesNotContain(normalDigital, colaNormal);
        Assert.DoesNotContain(normalDigitalLimite, colaNormal);
        Assert.DoesNotContain(cdDigital, colaCd);
        Assert.DoesNotContain(pucDigital, colaPuc);

        var conteo = await svc.ContarColasCajaAsync();
        Assert.Equal(4, conteo.Normal);
        Assert.Equal(1, conteo.CambioDomicilio);
        Assert.Equal(1, conteo.PenultimaCarpeta);
    }

    // ---- Criterio 3: reimportar tras corregir el RUT ----

    private static readonly string[] HeadersAgenda =
        ["Fecha Toma Cita", "Fecha Cita", "Hora Inicio", "RUT", "Nombre", "Email", "Telefono", "Tramite", "Ubicacion", "Estado", "Observaciones", "Motivo Cancelacion"];

    private static MemoryStream Agenda(params (string Toma, string Rut)[] filas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("citas_20261030_155709");
        for (var i = 0; i < HeadersAgenda.Length; i++) ws.Cell(1, i + 1).Value = HeadersAgenda[i];
        var r = 2;
        foreach (var (toma, rut) in filas)
        {
            string[] campos = [toma, "30/12/2026", "12:00", rut, "Maria Prueba Soto", "maria.prueba@example.com", "+56900000001",
                "Renovación", "Av. Argentina", "Agendada", "", ""];
            for (var i = 0; i < campos.Length; i++) ws.Cell(r, i + 1).Value = campos[i];
            r++;
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private AgendaImportService Agendador() =>
        new(_db.NewContext(), _db.Clock, NullLogger<AgendaImportService>.Instance);

    [Fact]
    public async Task Importar_corregir_el_rut_y_reimportar_el_mismo_listado_no_crea_nada_nuevo()
    {
        var r1 = await Agendador().ImportarAsync(Agenda(("16/06/2026 11:03", RutOriginal)), "tester");
        Assert.Equal(1, r1.Creadas);
        Guid id;
        await using (var ctx = _db.NewContext()) id = ctx.Carpetas.Single().Id;

        await _db.NewService().ModificarRutAsync(id, RutCorregido, "operador");
        var r2 = await Agendador().ImportarAsync(Agenda(("16/06/2026 11:03", RutOriginal)), "tester");

        Assert.Equal(0, r2.Creadas);
        Assert.Equal(1, r2.YaExistian);
        Assert.Equal(0, r2.CiudadanosCreados);
        await using var check = _db.NewContext();
        Assert.Single(check.Carpetas);
        var ciudadano = Assert.Single(check.Ciudadanos);
        Assert.Equal(RutCorregido, ciudadano.Rut.Value);
    }

    [Fact]
    public async Task Tras_corregir_el_rut_otra_cita_con_el_rut_original_si_es_una_carpeta_nueva()
    {
        await Agendador().ImportarAsync(Agenda(("16/06/2026 11:03", RutOriginal)), "tester");
        Guid id;
        await using (var ctx = _db.NewContext()) id = ctx.Carpetas.Single().Id;
        await _db.NewService().ModificarRutAsync(id, RutCorregido, "operador");

        var r2 = await Agendador().ImportarAsync(Agenda(("16/06/2026 11:03", RutOriginal), ("17/06/2026 09:00", RutOriginal)), "tester");

        Assert.Equal(1, r2.Creadas);
        Assert.Equal(1, r2.YaExistian);
        await using var check = _db.NewContext();
        Assert.Equal(2, check.Carpetas.Count());
    }

    // ---- Criterio 4: importador del DETALLE ----

    private static readonly string[] HeadersDetalle =
        ["FECHA CITACION", "FECHA CUANDO SE SUBIO LA CARPETA", "ULTIMA CARPETA", "NOMBRE", "APELLIDO", "RUT", "ESTADO"];

    private static MemoryStream Detalle(string[] headers, params string?[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("AV. ARGENTINA");
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                ws.Cell(r + 2, c + 1).Value = rows[r][c];
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private Task ImportarDetalleAsync(MemoryStream s) =>
        new ExcelImportService(_db.NewContext(), _db.Clock, NullLogger<ExcelImportService>.Instance).ImportarExcelAsync(s, "tester");

    [Fact]
    public async Task Detalle_con_encabezado_SUBIO_da_FechaSubida_a_la_carpeta_nueva()
    {
        using var s = Detalle(HeadersDetalle, ["2026-01-15", "2026-01-20", "2020-05-01", "JUAN", "PEREZ", RutOriginal, "SUBIDA A CONASET"]);

        await ImportarDetalleAsync(s);

        await using var ctx = _db.NewContext();
        Assert.Equal(new DateOnly(2026, 1, 20), ctx.Carpetas.Single().FechaSubida);
    }

    [Fact]
    public async Task Detalle_completa_la_fecha_de_subida_de_una_carpeta_existente_sin_fecha()
    {
        string[] sinSubida = ["FECHA CITACION", "ULTIMA CARPETA", "NOMBRE", "APELLIDO", "RUT", "ESTADO"];
        using (var primero = Detalle(sinSubida, ["2026-01-15", "2020-05-01", "JUAN", "PEREZ", RutOriginal, "SUBIDA A CONASET"]))
            await ImportarDetalleAsync(primero);
        await using (var ctx = _db.NewContext()) Assert.Null(ctx.Carpetas.Single().FechaSubida);

        using var segundo = Detalle(HeadersDetalle, ["2026-01-15", "2026-01-20", "2020-05-01", "JUAN", "PEREZ", RutOriginal, "SUBIDA A CONASET"]);
        await ImportarDetalleAsync(segundo);

        await using var check = _db.NewContext();
        var c = check.Carpetas.Include(x => x.Historial).Single();
        Assert.Equal(new DateOnly(2026, 1, 20), c.FechaSubida);
        Assert.Contains(c.Historial, h => h.Campo == "FechaSubida" && h.Anterior is null && h.Nuevo == "2026-01-20");
    }

    [Fact]
    public async Task Detalle_no_pisa_una_fecha_de_subida_ya_registrada()
    {
        using (var primero = Detalle(HeadersDetalle, ["2026-01-15", "2026-01-20", "2020-05-01", "JUAN", "PEREZ", RutOriginal, "SUBIDA A CONASET"]))
            await ImportarDetalleAsync(primero);

        using var segundo = Detalle(HeadersDetalle, ["2026-01-15", "2026-02-03", "2020-05-01", "JUAN", "PEREZ", RutOriginal, "SUBIDA A CONASET"]);
        await ImportarDetalleAsync(segundo);

        await using var check = _db.NewContext();
        Assert.Equal(new DateOnly(2026, 1, 20), check.Carpetas.Single().FechaSubida);
    }

    [Fact]
    public async Task Detalle_acepta_tambien_el_encabezado_antiguo_SUBIDA()
    {
        using var s = Detalle(["FECHA CITACION", "FECHA SUBIDA", "NOMBRE", "APELLIDO", "RUT", "ESTADO"],
            ["2026-01-15", "2026-01-22", "JUAN", "PEREZ", RutOriginal, "SUBIDA A CONASET"]);

        await ImportarDetalleAsync(s);

        await using var ctx = _db.NewContext();
        Assert.Equal(new DateOnly(2026, 1, 22), ctx.Carpetas.Single().FechaSubida);
    }

    // Documenta el salto de la columna H (atención): no existe AtendidaEn y crear Asistencias inflaría los «vinieron» de la asistencia.
    [Fact]
    public async Task La_columna_de_atencion_del_detalle_no_se_importa_ni_crea_asistencias()
    {
        using var s = Detalle(["FECHA CITACION", "NOMBRE", "APELLIDO", "RUT", "ESTADO", "X", "Y", "ATENCIÓN"],
            ["2026-01-15", "JUAN", "PEREZ", RutOriginal, "SUBIDA A CONASET", "a", "b", "SI, EN AV. ARGENTINA"]);

        var res = await new ExcelImportService(_db.NewContext(), _db.Clock, NullLogger<ExcelImportService>.Instance).ImportarExcelAsync(s, "tester");

        await using var ctx = _db.NewContext();
        Assert.Equal(1, res.CarpetasCreadas);
        Assert.Empty(ctx.Asistencias);
        Assert.Null(ctx.Carpetas.Single().Comuna);
    }

    // ---- Edición del RUT y la asistencia ----

    [Fact]
    public async Task Corregir_el_rut_de_una_carpeta_mantiene_ligada_su_asistencia_del_dia()
    {
        var id = await SembrarAsync(EstadoCarpeta.PorClasificar, null, Decision.Pendiente);
        string rutAnterior;
        await using (var ctx = _db.NewContext())
        {
            var carpeta = await ctx.Carpetas.Include(c => c.Ciudadano).SingleAsync(c => c.Id == id);
            rutAnterior = carpeta.Ciudadano.Rut.Value;
            ctx.Asistencias.Add(Asistencia.Vino(id, carpeta.Ciudadano.Rut, Sede.AvArgentina, new DateOnly(2026, 5, 4), "ana", Antes));
            await ctx.SaveChangesAsync();
        }

        await _db.NewService().ModificarRutAsync(id, RutCorregido, "operador");

        await using var check = _db.NewContext();
        var asistencia = Assert.Single(check.Asistencias);
        Assert.Equal(RutCorregido, asistencia.Rut);
        Assert.NotEqual(rutAnterior, asistencia.Rut);
    }
}
