using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Sgl.Infrastructure.Persistence;
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

    [Fact]
    public async Task ImportarExcelAsync_SkipsBadRowCleanly_AndImportsOthers()
    {
        // Arrange: the middle row fails on flush after its ciudadano + carpeta were tracked
        using var db = new TestDb();
        using var stream = BuildWorkbook(
            ("2026-01-15", null, "11111111-1"),
            ("2026-01-15", null, "22222222-2"),
            ("2026-01-15", null, "33333333-3"));
        var service = NewServiceFailingOn(db, ["22222222"]);

        // Act
        var result = await service.ImportarExcelAsync(stream, "tester");

        // Assert
        using var check = db.NewContext();
        Assert.Equal(2, check.Carpetas.Count());
        Assert.Equal(2, check.Ciudadanos.Count());
        Assert.Equal(1, result.TotalErrores);
        Assert.Single(result.Errores);
    }

    [Fact]
    public async Task ImportarExcelAsync_ReportsTotalErrorCount_WhenMessagesTruncated()
    {
        // Arrange
        using var db = new TestDb();
        var rows = Enumerable.Range(1, 25)
            .Select(i => ("2026-01-15", (string?)null, ConDv(1000000 + i)))
            .Append(("2026-01-15", null, "11111111-1"))
            .ToArray();
        using var stream = BuildWorkbook(rows);
        var service = NewServiceFailingOn(db, Enumerable.Range(1, 25).Select(i => $"{1000000 + i}"));

        // Act
        var result = await service.ImportarExcelAsync(stream, "tester");

        // Assert
        Assert.Equal(25, result.TotalErrores);
        Assert.Equal(20, result.Errores.Count);
        Assert.Equal(1, result.CarpetasCreadas);
    }

    [Fact]
    public async Task ImportarExcelAsync_RollsBackEverything_WhenCancelledMidway()
    {
        // Arrange: cancel right after the first flush to the database
        using var db = new TestDb();
        using var cts = new CancellationTokenSource();
        var rows = Enumerable.Range(1, 600)
            .Select(i => ("2026-01-15", (string?)null, ConDv(2000000 + i)))
            .ToArray();
        using var stream = BuildWorkbook(rows);
        var options = new DbContextOptionsBuilder<SglDbContext>()
            .UseSqlite(db.NewContext().Database.GetDbConnection())
            .AddInterceptors(new CancelAfterSaveInterceptor(cts))
            .Options;
        var service = new ExcelImportService(new SglDbContext(options), db.Clock, NullLogger<ExcelImportService>.Instance);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportarExcelAsync(stream, "tester", cts.Token));

        // Assert
        using var check = db.NewContext();
        Assert.Equal(0, check.Carpetas.Count());
        Assert.Equal(0, check.Ciudadanos.Count());
    }

    /// <summary>RUT con dígito verificador correcto (el importador ya no acepta DV erróneos).</summary>
    private static string ConDv(int cuerpo) => $"{cuerpo}-{Sgl.Domain.Rut.ComputeCheckDigit(cuerpo.ToString())}";

    private static ExcelImportService NewServiceFailingOn(TestDb db, IEnumerable<string> rutBodies)
    {
        var options = new DbContextOptionsBuilder<SglDbContext>()
            .UseSqlite(db.NewContext().Database.GetDbConnection())
            .AddInterceptors(new FailRowInterceptor(rutBodies.ToHashSet()))
            .Options;
        return new ExcelImportService(new SglDbContext(options), db.Clock, NullLogger<ExcelImportService>.Instance);
    }

    /// <summary>Simulates a row failing after its entities were tracked (e.g. a constraint violation).</summary>
    private sealed class FailRowInterceptor(HashSet<string> rutBodies) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var hit = eventData.Context!.ChangeTracker.Entries<Sgl.Domain.Ciudadano>()
                .Any(e => e.State == EntityState.Added && rutBodies.Contains(e.Entity.Rut.Value.Split('-')[0]));
            if (hit) throw new InvalidOperationException("Simulated row failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CancelAfterSaveInterceptor(CancellationTokenSource cts) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            return ValueTask.FromResult(result);
        }
    }

    private static MemoryStream BuildWorkbook(params (string Citacion, string? Subida, string Rut)[] rows)
    {
        var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("AV ARGENTINA");
            ws.Cell(1, 1).Value = "CITACION";
            ws.Cell(1, 2).Value = "SUBIDA";
            ws.Cell(1, 4).Value = "NOMBRE";
            ws.Cell(1, 5).Value = "APELLIDO";
            ws.Cell(1, 7).Value = "RUT";
            for (var i = 0; i < rows.Length; i++)
            {
                var r = i + 2;
                ws.Cell(r, 1).Value = rows[i].Citacion;
                if (rows[i].Subida is not null) ws.Cell(r, 2).Value = rows[i].Subida;
                ws.Cell(r, 4).Value = "Test";
                ws.Cell(r, 5).Value = "Synthetic";
                ws.Cell(r, 7).Value = rows[i].Rut;
            }
            wb.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
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


