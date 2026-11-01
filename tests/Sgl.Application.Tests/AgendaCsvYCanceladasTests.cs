using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sgl.Domain;
using Sgl.Infrastructure.Carpetas;

namespace Sgl.Application.Tests;

/// <summary>E1: agenda en CSV (cp1252) y xlsx, clave RUT + Fecha Toma Cita y canceladas guardadas sin datos personales. Datos 100% sintéticos.</summary>
public sealed class AgendaCsvYCanceladasTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static readonly string[] Headers =
        ["Fecha Toma Cita", "Fecha Cita", "Hora Inicio", "RUT", "Nombre", "Email", "Telefono", "Tramite", "Ubicacion", "Estado", "Observaciones", "Motivo Cancelacion"];

    private sealed record Cita(
        string Toma, string RutTxt, string Nombre = "Maria Ñandú Peñaloza Soto", string Fecha = "30/12/2026", string Hora = "12:00",
        string Tramite = "Renovación", string Ubicacion = "Placilla de Peñuelas", string Estado = "Agendada", string Motivo = "",
        string Email = "maria.prueba@example.com", string Telefono = "+56 9 0000 0001");

    private const string Rut1 = "12345678-5";
    private const string Rut2 = "11111111-1";
    private const string Rut3 = "22222222-2";
    private const string RutMalo = "12345678-9";

    private static string[] Campos(Cita c) =>
        [c.Toma, c.Fecha, c.Hora, c.RutTxt, c.Nombre, c.Email, c.Telefono, c.Tramite, c.Ubicacion, c.Estado, "", c.Motivo];

    private static byte[] Csv(Encoding enc, bool conSep, params Cita[] filas)
    {
        static string Q(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var sb = new StringBuilder();
        if (conSep) sb.Append("sep=,\r\n");
        sb.Append(string.Join(',', Headers)).Append("\r\n");
        foreach (var f in filas) sb.Append(string.Join(',', Campos(f).Select(Q))).Append("\r\n");
        return enc.GetBytes(sb.ToString());
    }

    private static Encoding Cp1252() => CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    private static MemoryStream Xlsx(params Cita[] filas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("citas_20261030_155709");
        for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        var r = 2;
        foreach (var f in filas)
        {
            var campos = Campos(f);
            for (var i = 0; i < campos.Length; i++) ws.Cell(r, i + 1).Value = campos[i];
            r++;
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private sealed class LoggerFalso : ILogger<AgendaImportService>
    {
        public List<string> Lineas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lineas.Add(formatter(state, exception) + " " + exception);
    }

    private AgendaImportService Nuevo(ILogger<AgendaImportService>? logger = null) =>
        new(_db.NewContext(), _db.Clock, logger ?? new LoggerFalso());

    private static Cita[] Lote() =>
    [
        new("16/06/2026 11:03", Rut1),
        new("16/06/2026 11:20", Rut2, Tramite: "Primera vez, Extensión", Ubicacion: "Av. Argentina"),
        new("16/06/2026 12:00", Rut3, Estado: "Cancelada", Motivo: "No podré asistir"),
        new("16/06/2026 12:30", RutMalo),
    ];

    // Criterio 1
    [Fact]
    public async Task Csv_cp1252_con_sep_da_los_mismos_conteos_que_su_xlsx()
    {
        using var csv = new MemoryStream(Csv(Cp1252(), true, Lote()));
        var rCsv = await Nuevo().ImportarAsync(csv, "tester");

        using var otra = new TestDb();
        using var x = Xlsx(Lote());
        var rXlsx = await new AgendaImportService(otra.NewContext(), otra.Clock, new LoggerFalso()).ImportarAsync(x, "tester");

        Assert.Equal(rXlsx, rCsv with { }, new ResultadoComparer());
        Assert.Equal(4, rCsv.FilasLeidas);
        Assert.Equal(2, rCsv.Creadas);
        Assert.Equal(1, rCsv.CanceladasOmitidas);
        Assert.Equal([5], rCsv.FilasRutInvalido);
        using var ctx = _db.NewContext();
        var c = ctx.Carpetas.Single(k => k.Sede == Sede.Placilla);
        Assert.Equal(TramiteAgenda.Renovacion, c.TramiteAgenda);
        Assert.Contains(ctx.Ciudadanos.ToList(), z => z.NombreCompleto.Contains("ÑANDÚ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Csv_en_utf8_con_bom_sin_linea_sep_tambien_se_importa()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Csv(new UTF8Encoding(false), false, Lote())).ToArray();
        var res = await Nuevo().ImportarAsync(new MemoryStream(bytes), "tester");
        Assert.Equal(2, res.Creadas);
        using var ctx = _db.NewContext();
        Assert.Contains(ctx.Ciudadanos.ToList(), z => z.NombreCompleto.Contains("PEÑALOZA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Csv_con_campo_entrecomillado_con_coma_y_comillas_dobles_se_lee_completo()
    {
        var fila = new Cita("16/06/2026 11:03", Rut1, Nombre: "Maria \"La Prueba\", Soto Peña");
        var res = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, fila)), "tester");
        Assert.Equal(1, res.Creadas);
        using var ctx = _db.NewContext();
        Assert.Contains("\"LA PRUEBA\", SOTO PEÑA", ctx.Ciudadanos.Single().NombreCompleto, StringComparison.Ordinal);
    }

    // Criterio 2
    [Fact]
    public async Task Reconoce_el_formato_por_contenido_y_un_txt_con_encabezados_validos_se_importa()
    {
        var csv = Csv(Cp1252(), true, Lote());
        using var comoTxt = new MemoryStream(csv); // el nombre del archivo no participa
        Assert.True(AgendaImportService.EsFormatoAgenda(comoTxt));
        Assert.Equal(0, comoTxt.Position);
        Assert.False(AgendaTabla.EsXlsx(comoTxt));

        using var x = Xlsx(Lote());
        Assert.True(AgendaTabla.EsXlsx(x));
        Assert.True(AgendaImportService.EsFormatoAgenda(x));

        using var otro = new MemoryStream(Cp1252().GetBytes("sep=,\r\nA,B,C\r\n1,2,3\r\n"));
        Assert.False(AgendaImportService.EsFormatoAgenda(otro));

        var res = await Nuevo().ImportarAsync(comoTxt, "tester");
        Assert.Equal(2, res.Creadas);
    }

    // Criterio 3
    [Fact]
    public async Task Importar_A_y_luego_A_mas_B_crea_solo_las_filas_de_B()
    {
        var a = new[] { new Cita("16/06/2026 11:03", Rut1), new Cita("16/06/2026 11:20", Rut2) };
        var b = new Cita("17/06/2026 09:01", Rut3, Ubicacion: "Mercado Puerto");

        var r1 = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, a)), "tester");
        var r2 = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, [.. a, b])), "tester");

        Assert.Equal(2, r1.Creadas);
        Assert.Equal(1, r2.Creadas);
        Assert.Equal(2, r2.YaExistian);
        using var ctx = _db.NewContext();
        Assert.Equal(3, ctx.Carpetas.Count());
    }

    [Fact]
    public async Task La_clave_es_RUT_mas_Fecha_Toma_Cita_misma_persona_con_otra_toma_y_otra_cita_es_otra_fila()
    {
        var a = new Cita("16/06/2026 11:03", Rut1);
        var otraCita = new Cita("20/06/2026 10:00", Rut1, Fecha: "15/01/2027");
        await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, a)), "tester");

        var res = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, a, otraCita)), "tester");

        Assert.Equal(1, res.Creadas);
        using var ctx = _db.NewContext();
        Assert.Equal(2, ctx.Carpetas.Count());
        Assert.Equal(2, ctx.Carpetas.Select(c => c.FechaTomaCita).Distinct().Count());
    }

    [Fact]
    public async Task Carpetas_antiguas_sin_fecha_de_toma_no_se_duplican()
    {
        using (var ctx = _db.NewContext())
        {
            var c = new Ciudadano(Rut.Parse(Rut1), "ANTIGUA", "PRUEBA");
            ctx.Carpetas.Add(Carpeta.CrearDesdeAgenda(c, Sede.Placilla, new DateOnly(2026, 12, 30), null, null, "imp", _db.Clock.GetUtcNow().UtcDateTime));
            ctx.SaveChanges();
        }

        var res = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, new Cita("16/06/2026 11:03", Rut1))), "tester");

        Assert.Equal(0, res.Creadas);
        Assert.Equal(1, res.YaExistian);
        using var check = _db.NewContext();
        Assert.Single(check.Carpetas);
    }

    // M3: la clave antigua (sede + fecha de cita) solo vale para carpetas sin fecha de toma, tanto al buscar existentes como al anotar una cancelación.
    [Fact]
    public async Task Carpeta_antigua_sin_toma_que_llega_cancelada_con_toma_deja_el_evento_en_su_historial()
    {
        using (var ctx = _db.NewContext())
        {
            var c = new Ciudadano(Rut.Parse(Rut1), "ANTIGUA", "PRUEBA");
            ctx.Carpetas.Add(Carpeta.CrearDesdeAgenda(c, Sede.Placilla, new DateOnly(2026, 12, 30), null, null, "imp", _db.Clock.GetUtcNow().UtcDateTime));
            ctx.SaveChanges();
        }
        var cancelada = new Cita("16/06/2026 11:03", Rut1, Estado: "Cancelada", Motivo: "No podré asistir");

        var res = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, cancelada)), "tester");

        Assert.Equal(1, res.CanceladasDeCarpetaExistente);
        using var check = _db.NewContext();
        var carpeta = Assert.Single(check.Carpetas.Include(k => k.Historial).ToList());
        Assert.Contains(carpeta.Historial, h => h.Campo == "CitaCancelada");
    }

    [Fact]
    public async Task Misma_sede_y_fecha_con_otra_toma_es_otra_carpeta_y_la_antigua_sin_toma_sigue_deduplicando()
    {
        await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, new Cita("16/06/2026 11:03", Rut1))), "tester");

        var res = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, new Cita("17/06/2026 09:00", Rut1))), "tester");

        Assert.Equal(1, res.Creadas);
        using var ctx = _db.NewContext();
        Assert.Equal(2, ctx.Carpetas.Count());
    }

    // Criterio 4
    [Fact]
    public async Task Guarda_email_telefono_tramite_y_fecha_de_toma_y_ningun_log_ni_resultado_trae_datos_personales()
    {
        var logger = new LoggerFalso();
        var fila = new Cita("16/06/2026 11:03", Rut1, Email: "dato.privado@example.com", Telefono: "+56 9 1111 2222");

        var res = await Nuevo(logger).ImportarAsync(new MemoryStream(Csv(Cp1252(), true, fila)), "tester");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Nuevo(logger).ImportarAsync(new MemoryStream(Cp1252().GetBytes("sep=,\r\nX,Y\r\ndato.privado@example.com,Maria Ñandú\r\n")), "tester"));

        using var ctx = _db.NewContext();
        var c = ctx.Carpetas.Single();
        Assert.Equal("dato.privado@example.com", c.EmailContacto);
        Assert.Equal("+56 9 1111 2222", c.TelefonoContacto);
        Assert.Equal(TramiteAgenda.Renovacion, c.TramiteAgenda);
        Assert.Equal(new DateTime(2026, 6, 16, 11, 3, 0), c.FechaTomaCita);

        var serializado = JsonSerializer.Serialize(res) + res;
        var todo = serializado + string.Join('\n', logger.Lineas);
        foreach (var prohibido in new[] { "12345678", "dato.privado", "Ñandú", "Nandu", "1111 2222", "Maria" })
        {
            Assert.DoesNotContain(prohibido, todo, StringComparison.OrdinalIgnoreCase);
        }
        Assert.NotEmpty(logger.Lineas);
    }

    [Fact]
    public async Task Recorta_email_y_telefono_a_su_largo_maximo()
    {
        var fila = new Cita("16/06/2026 11:03", Rut1, Email: new string('a', 300) + "@example.com", Telefono: new string('9', 60));
        await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, fila)), "tester");
        using var ctx = _db.NewContext();
        var c = ctx.Carpetas.Single();
        Assert.Equal(Carpeta.MaxEmailContactoLength, c.EmailContacto!.Length);
        Assert.Equal(Carpeta.MaxTelefonoContactoLength, c.TelefonoContacto!.Length);
    }

    // Criterio 5
    [Fact]
    public async Task Cancelada_no_crea_carpeta_crea_CitaCancelada_sin_RUT_ni_nombre_y_reimportar_no_duplica()
    {
        var canc = new Cita("16/06/2026 12:00", Rut3, Estado: "Cancelada", Motivo: "Motivos personales", Hora: "10:30");
        var ok = new Cita("16/06/2026 11:03", Rut1);

        var r1 = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, ok, canc)), "tester");
        var r2 = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, ok, canc)), "tester");

        Assert.Equal(1, r1.Creadas);
        Assert.Equal(1, r1.CanceladasOmitidas);
        Assert.Equal(1, r1.CanceladasGuardadas);
        Assert.Equal(0, r2.CanceladasGuardadas);
        Assert.Equal(1, r2.CanceladasYaRegistradas);
        using var ctx = _db.NewContext();
        Assert.Single(ctx.Carpetas);
        var cita = Assert.Single(ctx.CitasCanceladas.ToList());
        Assert.Equal(new DateOnly(2026, 12, 30), cita.FechaCita);
        Assert.Equal(new DateTime(2026, 6, 16, 12, 0, 0), cita.FechaTomaCita);
        Assert.Equal(new TimeOnly(10, 30), cita.HoraCita);
        Assert.Equal(Sede.Placilla, cita.Sede);
        Assert.Equal("Renovación", cita.Tramite);
        Assert.Equal("Motivos personales", cita.Motivo);
        Assert.DoesNotContain(typeof(CitaCancelada).GetProperties(), p =>
            p.Name.Contains("Rut", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Nombre", StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains("Email", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Telefono", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ctx.Ciudadanos.ToList(), z => z.Rut.Value == Rut.Parse(Rut3).Value);
    }

    // Criterio 6
    [Fact]
    public async Task Rut_con_DV_invalido_va_a_FilasRutInvalido_y_se_salta_pero_una_cancelada_con_RUT_invalido_cuenta()
    {
        var malo = new Cita("16/06/2026 12:30", RutMalo);
        var cancelMalo = new Cita("16/06/2026 12:45", RutMalo, Estado: "Cancelada", Motivo: "Otro", Hora: "11:00");

        var res = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, malo, cancelMalo)), "tester");

        Assert.Equal([2], res.FilasRutInvalido);
        Assert.Equal(0, res.Creadas);
        Assert.Equal(1, res.CanceladasGuardadas);
        using var ctx = _db.NewContext();
        Assert.Empty(ctx.Carpetas);
        Assert.Empty(ctx.Ciudadanos);
        Assert.Single(ctx.CitasCanceladas);
    }

    // Criterio 7
    [Fact]
    public async Task Fila_que_ya_era_carpeta_y_llega_cancelada_se_registra_deja_evento_y_no_borra_la_carpeta()
    {
        var agendada = new Cita("16/06/2026 11:03", Rut1);
        await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, agendada)), "tester");

        var cancelada = agendada with { Estado = "Cancelada", Motivo = "No podré asistir" };
        var r2 = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, cancelada)), "tester");
        var r3 = await Nuevo().ImportarAsync(new MemoryStream(Csv(Cp1252(), true, cancelada)), "tester");

        Assert.Equal(1, r2.CanceladasGuardadas);
        Assert.Equal(1, r2.CanceladasDeCarpetaExistente);
        Assert.Equal(0, r3.CanceladasGuardadas);
        Assert.Equal(0, r3.CanceladasDeCarpetaExistente);
        using var ctx = _db.NewContext();
        var carpeta = ctx.Carpetas.Single();
        Assert.False(carpeta.Anulada);
        Assert.Equal(EstadoCarpeta.PorClasificar, carpeta.Estado);
        Assert.Single(ctx.CitasCanceladas);
        var eventos = ctx.Historial.Where(h => h.CarpetaId == carpeta.Id && h.Campo == "CitaCancelada").ToList();
        Assert.Single(eventos);
        Assert.DoesNotContain("No podré", eventos[0].Nuevo ?? "");
    }

    [Fact]
    public async Task Xlsx_con_fecha_de_toma_como_fecha_real_de_Excel_tambien_cuenta()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("citas");
        for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Cell(2, 1).Value = new DateTime(2026, 6, 16, 11, 3, 0);
        ws.Cell(2, 2).Value = new DateTime(2026, 12, 30);
        ws.Cell(2, 3).Value = TimeSpan.FromHours(12);
        ws.Cell(2, 4).Value = Rut1;
        ws.Cell(2, 5).Value = "Maria Prueba Soto";
        ws.Cell(2, 8).Value = "Renovación";
        ws.Cell(2, 9).Value = "Av. Argentina";
        ws.Cell(2, 10).Value = "Agendada";
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var res = await Nuevo().ImportarAsync(ms, "tester");

        Assert.Equal(1, res.Creadas);
        using var ctx = _db.NewContext();
        var c = ctx.Carpetas.Single();
        Assert.Equal(new DateTime(2026, 6, 16, 11, 3, 0), c.FechaTomaCita);
        Assert.Equal(new DateOnly(2026, 12, 30), c.FechaCitacion);
        Assert.Equal(new TimeOnly(12, 0), c.HoraCita);
    }

    private sealed class ResultadoComparer : IEqualityComparer<AgendaImportResult>
    {
        public bool Equals(AgendaImportResult? a, AgendaImportResult? b) =>
            a is not null && b is not null
            && a.FilasLeidas == b.FilasLeidas && a.Creadas == b.Creadas && a.YaExistian == b.YaExistian
            && a.CanceladasOmitidas == b.CanceladasOmitidas && a.CanceladasGuardadas == b.CanceladasGuardadas
            && a.CiudadanosCreados == b.CiudadanosCreados && a.FilasRutInvalido.SequenceEqual(b.FilasRutInvalido)
            && a.Errores.Count == b.Errores.Count && a.ConAlertaDuplicado == b.ConAlertaDuplicado;

        public int GetHashCode(AgendaImportResult obj) => obj.FilasLeidas;
    }
}
