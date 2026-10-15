using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Sgl.Domain;
using Sgl.Infrastructure.Carpetas;

namespace Sgl.Application.Tests;

public sealed class ExcelImportComunaTests
{
    private static MemoryStream Build(params (string Rut, string Comuna, string Estado)[] rows)
    {
        var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("AV ARGENTINA");
            ws.Cell(1, 1).Value = "CITACION";
            ws.Cell(1, 4).Value = "NOMBRE";
            ws.Cell(1, 5).Value = "APELLIDO";
            ws.Cell(1, 7).Value = "RUT";
            ws.Cell(1, 8).Value = "COMUNA";
            ws.Cell(1, 9).Value = "ESTADO";
            for (var i = 0; i < rows.Length; i++)
            {
                ws.Cell(i + 2, 1).Value = "2026-01-15";
                ws.Cell(i + 2, 4).Value = "Test";
                ws.Cell(i + 2, 5).Value = "Synthetic";
                ws.Cell(i + 2, 7).Value = rows[i].Rut;
                ws.Cell(i + 2, 8).Value = rows[i].Comuna;
                ws.Cell(i + 2, 9).Value = rows[i].Estado;
            }
            wb.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Unknown_comuna_on_existing_carpeta_still_applies_estado_and_is_counted_as_warning()
    {
        using var db = new TestDb();
        var svc = () => new ExcelImportService(db.NewContext(), db.Clock, NullLogger<ExcelImportService>.Instance);
        await svc().ImportarExcelAsync(Build(("11111111-1", "", "")), "tester");

        var r = await svc().ImportarExcelAsync(Build(("11111111-1", "ZZ COMUNA RARA", "SUBIDA CON F8")), "tester");

        using var check = db.NewContext();
        var c = Assert.Single(check.Carpetas.ToList());
        Assert.Equal(EstadoCarpeta.SubidaConF8, c.Estado);
        Assert.Null(c.Comuna);
        Assert.Equal(0, r.TotalErrores);
        Assert.Equal(1, r.ComunasNoReconocidas);
        Assert.Equal(1, r.CarpetasActualizadas);
    }

    [Fact]
    public async Task New_rows_store_the_official_comuna_or_null_when_unknown()
    {
        using var db = new TestDb();
        var svc = new ExcelImportService(db.NewContext(), db.Clock, NullLogger<ExcelImportService>.Instance);

        var r = await svc.ImportarExcelAsync(Build(("11111111-1", "vina del mar", ""), ("22222222-2", "ZZ COMUNA RARA", "")), "tester");

        using var check = db.NewContext();
        var comunas = check.Carpetas.Select(c => c.Comuna).ToList();
        Assert.Contains("VIÑA DEL MAR", comunas);
        Assert.Single(comunas, x => x == null);
        Assert.Equal(2, r.CarpetasCreadas);
        Assert.Equal(1, r.ComunasNoReconocidas);
    }
}
