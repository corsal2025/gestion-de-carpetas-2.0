using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Sgl.Infrastructure.Carpetas;

namespace Sgl.Application.Tests;

/// <summary>DETALLE column mapping: never invent data from unmapped columns.</summary>
public sealed class ExcelImportMapeoTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

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

    private async Task Importar(MemoryStream s) =>
        await new ExcelImportService(_db.NewContext(), _db.Clock, NullLogger<ExcelImportService>.Instance).ImportarExcelAsync(s, "tester");

    [Fact]
    public async Task Subida_state_without_date_keeps_FechaSubida_null()
    {
        using var s = Detalle(["CITACION", "NOMBRE", "APELLIDO", "RUT", "ESTADO"],
            ["2026-01-15", "JUAN", "PEREZ", "12345678-5", "SUBIDA A CONASET"]);

        await Importar(s);

        using var ctx = _db.NewContext();
        Assert.Null(ctx.Carpetas.Single().FechaSubida);
    }

    [Fact]
    public async Task Atencion_column_is_not_read_as_comuna()
    {
        using var s = Detalle(["CITACION", "NOMBRE", "APELLIDO", "RUT", "ATENCIÓN", "ESTADO", "X", "Y", "Z"],
            ["2026-01-15", "JUAN", "PEREZ", "12345678-5", "SI, EN AV. ARGENTINA", "CAMBIO DE DOMICILIO", "a", "AV. ARGENTINA", "b"]);

        await Importar(s);

        using var ctx = _db.NewContext();
        Assert.Null(ctx.Carpetas.Single().Comuna);
    }

    [Fact]
    public async Task Real_comuna_column_is_mapped()
    {
        using var s = Detalle(["CITACION", "NOMBRE", "APELLIDO", "RUT", "COMUNA", "ESTADO"],
            ["2026-01-15", "JUAN", "PEREZ", "12345678-5", "Quilpué", "CAMBIO DE DOMICILIO"]);

        await Importar(s);

        using var ctx = _db.NewContext();
        Assert.Equal("QUILPUÉ", ctx.Carpetas.Single().Comuna);
    }

    [Fact]
    public async Task Single_name_column_is_split_and_never_gets_VECINO_placeholder()
    {
        using var s = Detalle(["CITACION", "NOMBRE", "RUT", "ESTADO"],
            ["2026-01-15", "Juan Andres Perez Soto", "12345678-5", "SUBIDA A CONASET"]);

        await Importar(s);

        using var ctx = _db.NewContext();
        var c = ctx.Ciudadanos.Single();
        Assert.Equal("JUAN ANDRES", c.Nombre);
        Assert.Equal("PEREZ SOTO", c.Apellido);
        Assert.Equal("juan andres perez soto", c.NombreBusqueda);
    }
}
