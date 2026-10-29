using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Excel;

namespace PeticionCambioDomicilio.Tests;

public sealed class ExcelPeticionImporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"peticion-test-{Guid.NewGuid():N}");

    public ExcelPeticionImporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* archivo aún tomado */ }
    }

    private ComunaDirectory Directorio()
    {
        var csv = Path.Combine(_dir, "comunas.csv");
        File.WriteAllText(csv, "Comuna,ContactEmail,Domain\n\"ARICA\",\"a@muniarica.cl\",\"muniarica.cl\"");
        return new ComunaDirectory(csv);
    }

    private static void Encabezado(IXLWorksheet ws)
    {
        ws.Cell(2, 1).SetValue("NOMBRE COMPLETO");
        ws.Cell(2, 2).SetValue("RUT");
        ws.Cell(2, 3).SetValue("ESTADO DE LA CARPETA");
        ws.Cell(2, 4).SetValue("FECHA ULTIMA CARPETA");
        ws.Cell(2, 5).SetValue("FECHA DE LA CITACION");
    }

    private string Libro(string? textoFecha, bool conHojaRota = false)
    {
        var path = Path.Combine(_dir, $"libro-{Guid.NewGuid():N}.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("ENERO PLACILLA");
        Encabezado(ws);
        ws.Cell(3, 1).SetValue("PERSONA");
        ws.Cell(3, 2).SetValue("11.111.111-1");
        ws.Cell(3, 3).SetValue("CAMBIO DE DOMICILIO");
        ws.Cell(3, 4).SetValue("ARICA");
        if (textoFecha is not null)
        {
            ws.Cell(3, 5).SetValue(textoFecha);
        }

        if (conHojaRota)
        {
            // Hoja de agenda a la que le borraron el encabezado "RUT": no se puede leer.
            wb.AddWorksheet("FEBRERO PLACILLA").Cell(2, 1).SetValue("NOMBRE");
        }

        wb.SaveAs(path);
        return path;
    }

    private DateOnly? FechaLeida(string? textoFecha)
    {
        var importer = new ExcelPeticionImporter(new AppOptions(), Directorio());
        Peticion? capturada = null;
        importer.Import(Libro(textoFecha), p => { capturada = p; return true; });
        return capturada!.FechaSolicitud;
    }

    [Theory]
    [InlineData("03/04/2024", 2024, 4, 3)]
    [InlineData("3-4-2024", 2024, 4, 3)]
    [InlineData("31.12.2024", 2024, 12, 31)]
    [InlineData("2024-04-03", 2024, 4, 3)]
    public void ReadDate_InterpretaDiaPrimeroComoEnChile(string texto, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), FechaLeida(texto));

    [Theory]
    [InlineData("12/31/2024")] // mes primero: no existe en Chile, no se adivina
    [InlineData("1999-01-01")] // fuera del rango plausible
    [InlineData("2099-01-01")]
    [InlineData("basura")]
    public void ReadDate_RechazaLoAmbiguoOImplausible(string texto) =>
        Assert.Null(FechaLeida(texto));

    [Fact]
    public void ReadDate_AceptaCeldaConFechaReal()
    {
        var path = Path.Combine(_dir, "fecha-real.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("ENERO PLACILLA");
            Encabezado(ws);
            ws.Cell(3, 2).SetValue("11.111.111-1");
            ws.Cell(3, 3).SetValue("CAMBIO DE DOMICILIO");
            ws.Cell(3, 4).SetValue("ARICA");
            ws.Cell(3, 5).SetValue(new DateTime(2024, 4, 3));
            wb.SaveAs(path);
        }

        Peticion? capturada = null;
        new ExcelPeticionImporter(new AppOptions(), Directorio()).Import(path, p => { capturada = p; return true; });

        Assert.Equal(new DateOnly(2024, 4, 3), capturada!.FechaSolicitud);
    }

    [Fact]
    public void HojaOmitida_NoSeBorranLasPeticionesQueNoReaparecen()
    {
        var importer = new ExcelPeticionImporter(new AppOptions(), Directorio());
        var existentes = new List<Peticion>
        {
            new() { Id = 2, NombreCompleto = "OTRA HOJA", Rut = "33.333.333-3", Comuna = "ARICA", Oficina = "PLACILLA" },
        };
        var borradas = new List<long>();

        var r = importer.Import(Libro("03/04/2024", conHojaRota: true), _ => true, existentes, borradas.Add);

        Assert.Empty(borradas);
        Assert.Equal(0, r.Obsoletas);
        Assert.Equal(1, r.HojasOmitidas);
        Assert.Contains(r.Avisos, a => a.Contains("no se borró", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SinHojasOmitidas_SiSeBorranLasObsoletas()
    {
        var importer = new ExcelPeticionImporter(new AppOptions(), Directorio());
        var existentes = new List<Peticion>
        {
            new() { Id = 2, NombreCompleto = "YA NO ESTA", Rut = "33.333.333-3", Comuna = "ARICA", Oficina = "PLACILLA" },
        };
        var borradas = new List<long>();

        importer.Import(Libro("03/04/2024"), _ => true, existentes, borradas.Add);

        Assert.Equal(new long[] { 2 }, borradas);
    }

    // --- H-13: el archivo se valida antes de abrirlo ---

    [Fact]
    public void ArchivoSinFirmaZip_SeRechazaAntesDeAbrirlo()
    {
        var falso = Path.Combine(_dir, "falso.xlsx");
        File.WriteAllText(falso, "no soy un zip");

        var ex = Assert.Throws<InvalidDataException>(() => LoadedWorkbook.Open(falso));

        Assert.Contains("xlsx", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZipQueDescomprimeMasDeLoPermitido_SeRechaza()
    {
        var bomba = Path.Combine(_dir, "bomba.xlsx");
        using (var zip = ZipFile.Open(bomba, ZipArchiveMode.Create))
        using (var entrada = zip.CreateEntry("xl/worksheets/sheet1.xml").Open())
        {
            entrada.Write(new byte[200_000]);
        }

        Assert.Throws<InvalidDataException>(() => WorkbookSanitizer.ValidarArchivo(bomba, maxBytesDescomprimidos: 100_000));
        WorkbookSanitizer.ValidarArchivo(bomba, maxBytesDescomprimidos: 1_000_000);
    }

    [Fact]
    public void XmlConDtd_SeRechazaAlLimpiarValidaciones()
    {
        var ruta = Path.Combine(_dir, "dtd.xlsx");
        using (var zip = ZipFile.Open(ruta, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open()))
        {
            writer.Write("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"aaaa\">]><worksheet>&a;</worksheet>");
        }

        Assert.ThrowsAny<System.Xml.XmlException>(() => WorkbookSanitizer.StripDataValidationsInPlace(ruta));
    }
}
