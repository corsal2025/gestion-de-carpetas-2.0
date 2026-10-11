using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging.Abstractions;
using Sgl.Infrastructure.Carpetas;

namespace Sgl.Application.Tests;

public sealed class ExcelImportServiceTests
{
    [Fact]
    public async Task ImportarExcelAsync_ImportsRows_WhenWorkbookHasDataValidationLongerThan255Chars()
    {
        // Arrange
        using var db = new TestDb();
        using var stream = BuildWorkbookWithLongDataValidation();
        var service = new ExcelImportService(db.NewContext(), db.Clock, NullLogger<ExcelImportService>.Instance);

        // Act
        var result = await service.ImportarExcelAsync(stream, "tester");

        // Assert
        Assert.Equal(1, result.CarpetasCreadas);
        Assert.Empty(result.Errores);
    }

    private static MemoryStream BuildWorkbookWithLongDataValidation()
    {
        var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("AV ARGENTINA");
            ws.Cell(1, 1).Value = "CITACION";
            ws.Cell(1, 4).Value = "NOMBRE";
            ws.Cell(1, 5).Value = "APELLIDO";
            ws.Cell(1, 7).Value = "RUT";
            ws.Cell(2, 1).Value = "2026-01-15";
            ws.Cell(2, 4).Value = "Test";
            ws.Cell(2, 5).Value = "Synthetic";
            ws.Cell(2, 7).Value = "11111111-1";
            wb.SaveAs(ms);
        }

        using (var doc = SpreadsheetDocument.Open(ms, true))
        {
            var worksheet = doc.WorkbookPart!.WorksheetParts.First().Worksheet;
            var longList = "\"" + string.Join(",", Enumerable.Range(1, 80).Select(i => $"OPCION{i}")) + "\"";
            var validations = new DataValidations(
                new DataValidation(new Formula1(longList))
                {
                    Type = DataValidationValues.List,
                    SequenceOfReferences = new ListValue<StringValue> { InnerText = "I2:I100" },
                }) { Count = 1 };
            var anchor = worksheet.Elements<SheetData>().First();
            worksheet.InsertAfter(validations, anchor);
            worksheet.Save();
        }

        ms.Position = 0;
        return ms;
    }
}
