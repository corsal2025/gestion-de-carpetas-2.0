using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Sgl.Domain;
using Sgl.Infrastructure.Carpetas;

namespace Sgl.Application.Tests;

public sealed class AgendaImportServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static readonly string[] Headers =
        ["Fecha Cita", "Hora Inicio", "RUT", "Nombre", "Email", "Telefono", "Tramite", "Ubicacion", "Estado", "Observaciones", "Motivo Cancelacion", "Clase de licencia"];

    private sealed record Fila(
        object Fecha, object Hora, string Rut, string Nombre, string Tramite, string Ubicacion,
        string Estado = "Agendada", string Motivo = "", string Clase = "");

    private static MemoryStream Agenda(params Fila[] filas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("citas_20261013_080000");
        for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        var r = 2;
        foreach (var f in filas)
        {
            ws.Cell(r, 1).Value = f.Fecha is DateTime d ? d : XLCellValue.FromObject(f.Fecha);
            ws.Cell(r, 2).Value = XLCellValue.FromObject(f.Hora);
            ws.Cell(r, 3).Value = f.Rut;
            ws.Cell(r, 4).Value = f.Nombre;
            ws.Cell(r, 5).Value = "persona@example.test";
            ws.Cell(r, 6).Value = "+56900000000";
            ws.Cell(r, 7).Value = f.Tramite;
            ws.Cell(r, 8).Value = f.Ubicacion;
            ws.Cell(r, 9).Value = f.Estado;
            ws.Cell(r, 11).Value = f.Motivo;
            ws.Cell(r, 12).Value = f.Clase;
            r++;
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private AgendaImportService NewService() =>
        new(_db.NewContext(), _db.Clock, NullLogger<AgendaImportService>.Instance);

    [Fact]
    public async Task Clase_de_agenda_es_solo_origen_y_no_clasifica_ni_sobrescribe()
    {
        using var input = Agenda(Ok("12345678-5") with { Clase = "B, C" });
        await NewService().ImportarAsync(input, "tester");
        using var ctx = _db.NewContext();
        var c = ctx.Carpetas.Single();
        Assert.Equal(ClaseLicencia.B | ClaseLicencia.C, c.ClasesAgenda);
        Assert.Equal(ClaseLicencia.Ninguna, c.Clases);
        Assert.Equal(EstadoCarpeta.PorClasificar, c.Estado);
        using var changed = Agenda(Ok("12345678-5") with { Clase = "A1" });
        var result = await NewService().ImportarAsync(changed, "tester");
        Assert.Contains(2, result.FilasConCambiosPorRevisar);
    }

    [Fact]
    public async Task Clase_invalida_genera_aviso_sin_perder_la_cita()
    {
        using var input = Agenda(Ok("12345678-5") with { Clase = "Clase desconocida" });
        var result = await NewService().ImportarAsync(input, "tester");
        Assert.Equal(1, result.Creadas);
        Assert.Contains(2, result.FilasConCambiosPorRevisar);
    }

    private static Fila Ok(string rut, string ubic = "Av. Argentina", string tramite = "Renovación", string hora = "09:30") =>
        new(new DateTime(2026, 10, 13), hora, rut, "Persona De Prueba Uno", tramite, ubic);

    [Fact]
    public async Task Different_appointment_hours_are_not_silently_discarded()
    {
        using var input = Agenda(Ok("12345678-5", hora: "09:30"), Ok("12345678-5", hora: "10:30"));
        var result = await NewService().ImportarAsync(input, "tester");
        Assert.Equal(2, result.Creadas);
        Assert.Equal(1, result.ConAlertaDuplicado);
        using var ctx = _db.NewContext();
        Assert.Equal(2, ctx.Carpetas.Count());
    }

    [Fact]
    public async Task Changed_hour_without_booking_identifier_is_reported_for_review()
    {
        using (var first = Agenda(Ok("12345678-5", hora: "09:30")))
            await NewService().ImportarAsync(first, "tester");
        using var second = Agenda(Ok("12345678-5", hora: "10:30"));
        var result = await NewService().ImportarAsync(second, "tester");
        Assert.Equal(0, result.Creadas);
        Assert.Contains(2, result.FilasSinImportarPorRevision);
        using var ctx = _db.NewContext();
        Assert.Single(ctx.Carpetas);
    }

    [Theory]
    [InlineData("Cambio de domicilio", TramiteAgenda.CambioDomicilio)]
    [InlineData("Canje licencia extranjera", TramiteAgenda.Canje)]
    public async Task Imports_other_recognized_appointment_types(string tramite, TramiteAgenda esperado)
    {
        using var input = Agenda(Ok("12345678-5", tramite: tramite));
        await NewService().ImportarAsync(input, "tester");
        using var ctx = _db.NewContext();
        Assert.Equal(esperado, ctx.Carpetas.Single().TramiteAgenda);
    }

    [Fact]
    public async Task Reimport_fills_missing_contact_without_overwriting_existing_values()
    {
        using (var ctx = _db.NewContext())
        {
            var persona = new Ciudadano(Rut.Parse("12345678-5"), "ORIGINAL", "PRUEBA");
            ctx.Carpetas.Add(Carpeta.CrearDesdeAgenda(persona, Sede.AvArgentina, new DateOnly(2026, 10, 13), new TimeOnly(9, 30), TramiteAgenda.Renovacion, "x", DateTime.UtcNow, email: "original@example.test"));
            ctx.SaveChanges();
        }
        using var input = Agenda(Ok("12345678-5"));
        var result = await NewService().ImportarAsync(input, "tester");
        using var check = _db.NewContext();
        var carpeta = check.Carpetas.Single();
        Assert.Equal("original@example.test", carpeta.EmailContacto);
        Assert.Equal("+56900000000", carpeta.TelefonoContacto);
        Assert.Contains(check.Set<HistorialCambio>(), h => h.CarpetaId == carpeta.Id && h.Campo == "TelefonoContacto");
        Assert.Equal(1, result.Actualizadas);
        Assert.Contains(2, result.FilasConCambiosPorRevisar);
    }

    [Fact]
    public void EsFormatoAgenda_detects_agenda_headers()
    {
        using var agenda = Agenda(Ok("12345678-5"));
        Assert.True(AgendaImportService.EsFormatoAgenda(agenda));
        Assert.Equal(0, agenda.Position);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("AV. ARGENTINA");
        ws.Cell(1, 1).Value = "FECHA CITACION";
        ws.Cell(1, 7).Value = "RUT";
        ws.Cell(1, 9).Value = "ESTADO";
        using var detalle = new MemoryStream();
        wb.SaveAs(detalle);
        detalle.Position = 0;
        Assert.False(AgendaImportService.EsFormatoAgenda(detalle));
    }

    [Fact]
    public async Task Imports_rows_as_PorClasificar_with_agenda_fields_and_sede_mapping()
    {
        using var s = Agenda(
            Ok("12345678-5", "Av. Argentina", "Renovación", "09:30"),
            Ok("11111111-1", "Placilla", "Primera vez, Extensión", "10:00"),
            Ok("22222222-2", "Merc. Puerto"),
            Ok("33333333-3", "Mercado Puerto"));

        var res = await NewService().ImportarAsync(s, "tester");

        Assert.Equal(4, res.FilasLeidas);
        Assert.Equal(4, res.Creadas);
        using var ctx = _db.NewContext();
        var carpetas = ctx.Carpetas.ToList();
        Assert.All(carpetas, c => Assert.Equal(EstadoCarpeta.PorClasificar, c.Estado));
        Assert.All(carpetas, c => Assert.Equal(new DateOnly(2026, 10, 13), c.FechaCitacion));
        Assert.Equal(2, carpetas.Count(c => c.Sede == Sede.MercadoPuerto));
        Assert.Single(carpetas, c => c.Sede == Sede.Placilla && c.TramiteAgenda == TramiteAgenda.PrimeraVezExtension && c.HoraCita == new TimeOnly(10, 0));
        Assert.Single(carpetas, c => c.Sede == Sede.AvArgentina && c.TramiteAgenda == TramiteAgenda.Renovacion && c.HoraCita == new TimeOnly(9, 30));
        Assert.Contains(ctx.Ciudadanos.ToList(), c => c.NombreBusqueda == "persona de prueba uno");
    }

    [Fact]
    public async Task Skips_cancelled_rows_and_reports_invalid_ruts_by_row_without_names()
    {
        using var s = Agenda(
            Ok("12345678-5"),
            Ok("11111111-1") with { Estado = "Cancelada" },
            Ok("22222222-2") with { Motivo = "No puede asistir" },
            Ok("12345678-9"));

        var res = await NewService().ImportarAsync(s, "tester");

        Assert.Equal(1, res.Creadas);
        Assert.Equal(2, res.CanceladasOmitidas);
        Assert.Equal([5], res.FilasRutInvalido);
        Assert.DoesNotContain(res.Errores, e => e.Contains("Persona", StringComparison.OrdinalIgnoreCase));
        using var ctx = _db.NewContext();
        Assert.Equal(1, ctx.Carpetas.Count());
    }

    [Fact]
    public async Task Is_idempotent_and_writes_import_log()
    {
        using (var s = Agenda(Ok("12345678-5"), Ok("11111111-1")))
            await NewService().ImportarAsync(s, "tester");

        using var again = Agenda(Ok("12345678-5"), Ok("11111111-1"), Ok("11111111-1"));
        var res = await NewService().ImportarAsync(again, "tester");

        Assert.Equal(0, res.Creadas);
        Assert.Equal(2, res.YaExistian);
        Assert.Contains(4, res.FilasSinImportarPorRevision);
        using var ctx = _db.NewContext();
        Assert.Equal(2, ctx.Carpetas.Count());
        Assert.Equal(2, ctx.ImportacionLogs.Count());
    }

    [Fact]
    public async Task Reuses_existing_ciudadano_without_overwriting_name()
    {
        using (var ctx = _db.NewContext())
        {
            ctx.Ciudadanos.Add(new Ciudadano(Rut.Parse("12345678-5"), "ORIGINAL", "NOMBRE"));
            ctx.SaveChanges();
        }
        using var s = Agenda(Ok("12.345.678-5"));

        var res = await NewService().ImportarAsync(s, "tester");

        Assert.Equal(1, res.Creadas);
        Assert.Equal(0, res.CiudadanosCreados);
        using var check = _db.NewContext();
        Assert.Equal("ORIGINAL", check.Ciudadanos.Single().Nombre);
    }

    [Fact]
    public async Task Reports_row_with_unknown_sede_or_missing_fecha()
    {
        using var s = Agenda(Ok("12345678-5", "Sede Desconocida"), Ok("11111111-1") with { Fecha = "" });

        var res = await NewService().ImportarAsync(s, "tester");

        Assert.Equal(0, res.Creadas);
        Assert.Equal(2, res.Errores.Count);
        Assert.Contains(res.Errores, e => e.StartsWith("Fila 2"));
        Assert.Contains(res.Errores, e => e.StartsWith("Fila 3"));
    }

    [Fact]
    public async Task Import_flags_row_when_rut_already_has_an_open_carpeta_but_still_creates_it()
    {
        using (var ctx = _db.NewContext())
        {
            var c = new Ciudadano(Rut.Parse("12345678-5"), "PERSONA", "PRUEBA");
            ctx.Carpetas.Add(Carpeta.Registrar(c, Sede.Placilla, new DateOnly(2026, 9, 1), "x", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), ClaseLicencia.B));
            ctx.SaveChanges();
        }
        using var s = Agenda(Ok("12345678-5"), Ok("11111111-1"));

        var res = await NewService().ImportarAsync(s, "tester");

        Assert.Equal(2, res.Creadas);
        Assert.Equal(1, res.ConAlertaDuplicado);
    }

    [Fact]
    public async Task Reimport_does_not_recreate_a_row_cancelled_by_reagendamiento()
    {
        using (var s = Agenda(Ok("12345678-5")))
        {
            await NewService().ImportarAsync(s, "tester");
        }
        using (var ctx = _db.NewContext())
        {
            var c = ctx.Carpetas.Single();
            c.Anular(MotivosAnulacion.Reagendamiento, "javiera", new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc));
            ctx.SaveChanges();
        }

        using var again = Agenda(Ok("12345678-5"));
        var res = await NewService().ImportarAsync(again, "tester");

        Assert.Equal(0, res.Creadas);
        Assert.Equal(1, res.YaExistian);
        Assert.Equal(1, res.YaExistianAnuladas);
    }
}
