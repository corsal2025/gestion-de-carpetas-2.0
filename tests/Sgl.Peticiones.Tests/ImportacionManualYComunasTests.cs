using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Excel;

namespace PeticionCambioDomicilio.Tests;

public class ImportacionManualYComunasTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"peticion-test-{Guid.NewGuid():N}");

    public ImportacionManualYComunasTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string LibroConUnaFila()
    {
        var path = Path.Combine(_dir, "libro.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("ENERO PLACILLA");
        ws.Cell(1, 1).Value = "AGENDA MENSUAL PLACILLA";
        ws.Cell(2, 1).Value = "NOMBRE COMPLETO";
        ws.Cell(2, 2).Value = "RUT";
        ws.Cell(2, 3).Value = "ESTADO DE LA CARPETA";
        ws.Cell(2, 4).Value = "FECHA ULTIMA CARPETA";
        ws.Cell(3, 1).Value = "PERSONA DEL EXCEL";
        ws.Cell(3, 2).Value = "11.111.111-1";
        ws.Cell(3, 3).Value = "CAMBIO DE DOMICILIO";
        ws.Cell(3, 4).Value = "ARICA";
        wb.SaveAs(path);
        return path;
    }

    private ComunaDirectory Directorio()
    {
        var csv = Path.Combine(_dir, "comunas.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Comuna,ContactEmail,Domain",
            "\"ARICA\",\"a@muniarica.cl\",\"muniarica.cl\"",
            "\"CONCEPCION\",\"c@concepcion.cl\",\"concepcion.cl\""));
        return new ComunaDirectory(csv);
    }

    [Fact]
    public void Importar_no_borra_peticiones_cargadas_a_mano()
    {
        var importer = new ExcelPeticionImporter(new AppOptions(), Directorio());
        var existentes = new List<Peticion>
        {
            new() { Id = 1, NombreCompleto = "MANUAL", Rut = "22.222.222-2", Comuna = "CONCEPCION",
                    Oficina = Peticion.OficinaManual, Estado = EstadoPeticion.Borrador },
            new() { Id = 2, NombreCompleto = "YA NO ESTA", Rut = "33.333.333-3", Comuna = "ARICA",
                    Oficina = "PLACILLA", Estado = EstadoPeticion.Borrador },
        };
        var borradas = new List<long>();

        importer.Import(LibroConUnaFila(), _ => true, existentes, borradas.Add);

        Assert.DoesNotContain(1L, borradas);
        Assert.Contains(2L, borradas);
    }

    [Fact]
    public void Al_abrir_la_base_corrige_la_comuna_mal_escrita_CONCECPION()
    {
        var db = Path.Combine(_dir, "peticiones.db");
        var repo = new PeticionRepository(db);
        repo.AddIfNew(new Peticion { NombreCompleto = "X", Rut = "44.444.444-4", Comuna = "CONCECPION" });

        var reabierto = new PeticionRepository(db);

        Assert.Equal("CONCEPCION", Assert.Single(reabierto.All()).Comuna);
    }

    [Fact]
    public void La_correccion_de_comuna_no_choca_si_ya_existe_la_version_corregida()
    {
        var db = Path.Combine(_dir, "peticiones.db");
        var repo = new PeticionRepository(db);
        repo.AddIfNew(new Peticion { NombreCompleto = "X", Rut = "44.444.444-4", Comuna = "CONCECPION" });
        repo.AddIfNew(new Peticion { NombreCompleto = "X", Rut = "44.444.444-4", Comuna = "CONCEPCION" });

        var reabierto = new PeticionRepository(db); // no debe tirar por el índice único

        Assert.Equal(2, reabierto.All().Count);
    }

    [Fact]
    public void DeleteMarcadas_borra_solo_las_filas_marcadas()
    {
        var repo = new PeticionRepository(Path.Combine(_dir, "peticiones.db"));
        repo.AddIfNew(new Peticion { NombreCompleto = "A", Rut = "11.111.111-1", Comuna = "ARICA" });
        repo.AddIfNew(new Peticion { NombreCompleto = "B", Rut = "22.222.222-2", Comuna = "ARICA" });
        var marcada = repo.All().First(p => p.NombreCompleto == "A");
        repo.ToggleMarcada(marcada.Id);

        var n = repo.DeleteMarcadas();

        Assert.Equal(1, n);
        Assert.Equal("B", Assert.Single(repo.All()).NombreCompleto);
    }
}
