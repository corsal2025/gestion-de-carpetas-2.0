using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sgl.Domain;
using Sgl.Infrastructure.Carpetas;

namespace Sgl.Application.Tests;

/// <summary>H-04 / H-10 / H-11 / H-13: filas dudosas se reportan y se saltan; el Excel no pisa avances de la app.</summary>
public sealed class ExcelImportRobustezTests
{
    private const string RutValido = "11111111-1";

    private static MemoryStream Libro(params (string? Citacion, string? Subida, string Rut, string? Estado, string? Decision)[] filas)
    {
        var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("AV ARGENTINA");
            ws.Cell(1, 1).SetValue("CITACION");
            ws.Cell(1, 2).SetValue("SUBIDA");
            ws.Cell(1, 4).SetValue("NOMBRE");
            ws.Cell(1, 5).SetValue("APELLIDO");
            ws.Cell(1, 7).SetValue("RUT");
            ws.Cell(1, 9).SetValue("ESTADO");
            ws.Cell(1, 10).SetValue("DECISION");
            for (var i = 0; i < filas.Length; i++)
            {
                var r = i + 2;
                if (filas[i].Citacion is not null) ws.Cell(r, 1).SetValue(filas[i].Citacion);
                if (filas[i].Subida is not null) ws.Cell(r, 2).SetValue(filas[i].Subida);
                ws.Cell(r, 4).SetValue("Test");
                ws.Cell(r, 5).SetValue("Synthetic");
                ws.Cell(r, 7).SetValue(filas[i].Rut);
                if (filas[i].Estado is not null) ws.Cell(r, 9).SetValue(filas[i].Estado);
                if (filas[i].Decision is not null) ws.Cell(r, 10).SetValue(filas[i].Decision);
            }

            wb.SaveAs(ms);
        }

        ms.Position = 0;
        return ms;
    }

    private static ExcelImportService Servicio(TestDb db) =>
        new(db.NewContext(), db.Clock, NullLogger<ExcelImportService>.Instance);

    [Fact]
    public async Task FilaSinCitacionValida_SeReportaYSeSalta_NoUsaHoy()
    {
        using var db = new TestDb();
        using var libro = Libro((null, null, RutValido, null, null), ("no es fecha", null, "22222222-2", null, null));

        var r = await Servicio(db).ImportarExcelAsync(libro, "tester");

        Assert.Equal(0, r.CarpetasCreadas);
        Assert.Equal(2, r.TotalErrores);
        Assert.Contains(r.Errores, e => e.Contains("citación", StringComparison.OrdinalIgnoreCase));
        using var check = db.NewContext();
        Assert.Equal(0, check.Carpetas.Count());
        Assert.Equal(0, check.Ciudadanos.Count());
    }

    [Theory]
    [InlineData("1990-01-15")]
    [InlineData("2099-01-15")]
    public async Task CitacionFueraDeRango_SeReportaYSeSalta(string citacion)
    {
        using var db = new TestDb();
        using var libro = Libro((citacion, null, RutValido, null, null));

        var r = await Servicio(db).ImportarExcelAsync(libro, "tester");

        Assert.Equal(0, r.CarpetasCreadas);
        Assert.Equal(1, r.TotalErrores);
    }

    [Fact]
    public async Task RutConDigitoVerificadorMalo_SeReportaSinMostrarElRut()
    {
        using var db = new TestDb();
        using var libro = Libro(("2026-01-15", null, "11111111-9", null, null), ("2026-01-15", null, RutValido, null, null));

        var r = await Servicio(db).ImportarExcelAsync(libro, "tester");

        Assert.Equal(1, r.CarpetasCreadas);
        Assert.Equal(1, r.TotalErrores);
        Assert.Single(r.Errores);
        Assert.DoesNotContain("11111111", r.Errores[0]);
        Assert.Contains("dígito verificador", r.Errores[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubidaAnteriorALaCitacion_ImportaYAvisa()
    {
        using var db = new TestDb();
        using var libro = Libro(("2026-02-10", "2026-02-01", RutValido, null, null));

        var r = await Servicio(db).ImportarExcelAsync(libro, "tester");

        Assert.Equal(1, r.CarpetasCreadas);
        Assert.Equal(0, r.TotalErrores);
        Assert.Single(r.Avisos);
    }

    [Fact]
    public async Task ReimportarConEstadoYDecisionVacios_NoRetrocedeNiBorraNiFalla()
    {
        using var db = new TestDb();
        using (var primero = Libro(("2026-01-15", null, RutValido, "CAMBIO DE DOMICILIO", "OTORGADO")))
        {
            await Servicio(db).ImportarExcelAsync(primero, "tester");
        }

        using var vacio = Libro(("2026-01-15", null, RutValido, null, null));
        var r = await Servicio(db).ImportarExcelAsync(vacio, "tester");

        Assert.Equal(1, r.CarpetasActualizadas);
        Assert.Empty(r.Errores);
        using var check = db.NewContext();
        var c = await check.Carpetas.SingleAsync();
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.Equal(Decision.Otorgado, c.Decision);
    }

    // M1: la carga histórica de la planilla maestra está exenta de IPermisos, pero deja rastro de su origen
    // y no inventa FechaOtorgamiento (la planilla no trae una fecha de otorgamiento).
    [Fact]
    public async Task CargaHistoricaConDecisionEnCarpetaExistente_DejaOrigenYNoInventaFechaDeOtorgamiento()
    {
        using var db = new TestDb();
        using (var primero = Libro(("2026-01-15", null, RutValido, null, null)))
        {
            await Servicio(db).ImportarExcelAsync(primero, "tester");
        }

        using var conDecision = Libro(("2026-01-15", "2026-02-01", RutValido, null, "OTORGADO"));
        var r = await Servicio(db).ImportarExcelAsync(conDecision, "tester");

        Assert.Empty(r.Errores);
        using var check = db.NewContext();
        var c = await check.Carpetas.Include(x => x.Historial).SingleAsync();
        Assert.Equal(Decision.Otorgado, c.Decision);
        Assert.Null(c.FechaOtorgamiento);
        Assert.Contains(c.Historial, h => h.Campo == "Origen" && h.Nuevo == "Importación Excel (carga histórica)");
    }

    [Fact]
    public async Task ReimportarNoRetrocedeElEstado_PeroSiAvanzaCuandoElExcelEstaMasAdelante()
    {
        using var db = new TestDb();
        using (var primero = Libro(("2026-01-15", null, RutValido, "CAMBIO DE DOMICILIO", null)))
        {
            await Servicio(db).ImportarExcelAsync(primero, "tester");
        }

        using (var atrasado = Libro(("2026-01-15", null, RutValido, null, null)))
        {
            await Servicio(db).ImportarExcelAsync(atrasado, "tester");
        }

        using (var check = db.NewContext())
        {
            Assert.Equal(EstadoCarpeta.CambioDomicilio, (await check.Carpetas.SingleAsync()).Estado);
        }

        using (var avance = Libro(("2026-01-15", null, RutValido, "CAMBIO DOM. SUBIDO A CONASET", null)))
        {
            var r = await Servicio(db).ImportarExcelAsync(avance, "tester");
            Assert.Empty(r.Errores);
        }

        using var final = db.NewContext();
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, (await final.Carpetas.SingleAsync()).Estado);
    }

    [Fact]
    public async Task ReimportarConEstadoVacio_NoSacaUnaCarpetaDePorClasificar()
    {
        using var db = new TestDb();
        using (var primero = Libro(("2026-01-15", null, RutValido, "CAMBIO DE DOMICILIO", null)))
        {
            await Servicio(db).ImportarExcelAsync(primero, "tester");
        }

        using (var ctx = db.NewContext())
        {
            var c = await ctx.Carpetas.SingleAsync();
            c.ForzarEstado(EstadoCarpeta.PorClasificar, "tester", db.Clock.GetUtcNow().UtcDateTime);
            await ctx.SaveChangesAsync();
        }

        using var vacio = Libro(("2026-01-15", null, RutValido, null, null));
        var r = await Servicio(db).ImportarExcelAsync(vacio, "tester");

        Assert.Empty(r.Errores);
        Assert.Equal(0, r.EstadosNoAplicados);
        using var check = db.NewContext();
        Assert.Equal(EstadoCarpeta.PorClasificar, (await check.Carpetas.SingleAsync()).Estado);
    }

    [Fact]
    public async Task ReimportarConEstadoAtrasado_CuentaElEstadoNoAplicado()
    {
        using var db = new TestDb();
        using (var primero = Libro(("2026-01-15", null, RutValido, "SUBIDA A CONASET", null)))
        {
            await Servicio(db).ImportarExcelAsync(primero, "tester");
        }

        using var atrasado = Libro(("2026-01-15", null, RutValido, "CAMBIO DE DOMICILIO", null));
        var r = await Servicio(db).ImportarExcelAsync(atrasado, "tester");

        Assert.Equal(1, r.EstadosNoAplicados);
        using var check = db.NewContext();
        Assert.Equal(EstadoCarpeta.SubidaConaset, (await check.Carpetas.SingleAsync()).Estado);
    }

    [Fact]
    public async Task Avisos_TienenTopeYConservanElTotal()
    {
        using var db = new TestDb();
        var filas = Enumerable.Range(1, 30)
            .Select(i => ((string?)"2026-02-10", (string?)"2026-02-01", $"{10000000 + i}-{DigitoVerificador(10000000 + i)}", (string?)null, (string?)null))
            .ToArray();
        using var libro = Libro(filas);

        var r = await Servicio(db).ImportarExcelAsync(libro, "tester");

        Assert.Equal(30, r.AvisosTotal);
        Assert.Equal(20, r.Avisos.Count);
    }

    private static char DigitoVerificador(int cuerpo)
    {
        var suma = 0;
        var factor = 2;
        for (var n = cuerpo; n > 0; n /= 10)
        {
            suma += n % 10 * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }

        var dv = 11 - suma % 11;
        return dv == 11 ? '0' : dv == 10 ? 'K' : (char)('0' + dv);
    }

    [Fact]
    public async Task ArchivoQueNoEsXlsx_SeRechazaConMensajeClaro()
    {
        using var db = new TestDb();
        using var basura = new MemoryStream("esto no es un zip"u8.ToArray());

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Servicio(db).ImportarExcelAsync(basura, "tester"));

        Assert.Contains("xlsx", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Auditoría: lo importado dentro de la familia de cambio de domicilio es saliente (visible en «Por pedir»), y «subido con correo» no se fuerza.
    [Fact]
    public async Task Importar_cambio_de_domicilio_deja_la_carpeta_saliente()
    {
        using var db = new TestDb();
        using var libro = Libro(("2026-01-15", null, RutValido, "CAMBIO DE DOMICILIO", null));

        var r = await Servicio(db).ImportarExcelAsync(libro, "tester");

        Assert.Empty(r.Errores);
        using var check = db.NewContext();
        var c = await check.Carpetas.SingleAsync();
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.Equal(SentidoCambioDomicilio.Saliente, c.SentidoCambioDomicilio);
    }

    [Fact]
    public async Task Reimportar_una_carpeta_existente_hacia_cambio_de_domicilio_la_deja_saliente()
    {
        using var db = new TestDb();
        using (var primero = Libro(("2026-01-15", null, RutValido, null, null)))
        {
            await Servicio(db).ImportarExcelAsync(primero, "tester");
        }

        using var avance = Libro(("2026-01-15", null, RutValido, "CAMBIO DE DOMICILIO", null));
        var r = await Servicio(db).ImportarExcelAsync(avance, "tester");

        Assert.Empty(r.Errores);
        using var check = db.NewContext();
        var c = await check.Carpetas.SingleAsync();
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.Equal(SentidoCambioDomicilio.Saliente, c.SentidoCambioDomicilio);
    }

    [Fact]
    public async Task Reimportar_no_lleva_a_subido_con_correo_si_la_carpeta_no_esta_solicitada()
    {
        using var db = new TestDb();
        using (var primero = Libro(("2026-01-15", null, RutValido, null, null)))
        {
            await Servicio(db).ImportarExcelAsync(primero, "tester");
        }

        using var salto = Libro(("2026-01-15", null, RutValido, "CAMBIO DOM. SUBIDO CON CORREO", null));
        var r = await Servicio(db).ImportarExcelAsync(salto, "tester");

        Assert.Empty(r.Errores);
        Assert.Equal(1, r.EstadosNoAplicados);
        using var check = db.NewContext();
        var c = await check.Carpetas.SingleAsync();
        Assert.Equal(EstadoCarpeta.Citada, c.Estado);
        Assert.Null(c.SentidoCambioDomicilio);
    }
}
