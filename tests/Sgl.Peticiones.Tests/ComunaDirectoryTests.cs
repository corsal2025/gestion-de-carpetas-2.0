using PeticionCambioDomicilio.Comunas;

namespace PeticionCambioDomicilio.Tests;

public class ComunaDirectoryTests : IDisposable
{
    private readonly string _csv;
    private readonly ComunaDirectory _dir;

    public ComunaDirectoryTests()
    {
        _csv = Path.Combine(Path.GetTempPath(), $"comunas-test-{Guid.NewGuid():N}.csv");
        File.WriteAllText(_csv, string.Join("\n",
            "Comuna,ContactEmail,Domain",
            "\"VIÑA DEL MAR\",\"transito@munivina.cl\",\"munivina.cl\"",
            "\"QUILPUÉ\",\"licencias@muniquilpue.cl\",\"muniquilpue.cl\"",
            "\"CONCÓN\",\"a@municoncon.cl\",\"municoncon.cl\"",
            "\"CONCÓN\",\"b@municoncon.cl\",\"municoncon.cl\""));
        _dir = new ComunaDirectory(_csv);
    }

    public void Dispose()
    {
        try { File.Delete(_csv); } catch { }
    }

    [Fact]
    public void Resuelve_nombre_exacto_ignorando_tildes_y_mayusculas()
    {
        Assert.Equal("VIÑA DEL MAR", _dir.ResolveComunaName("viña del mar"));
        Assert.Equal("QUILPUÉ", _dir.ResolveComunaName("QUILPUE"));
    }

    [Fact]
    public void Resuelve_con_tolerancia_a_un_tipeo()
    {
        Assert.Equal("VIÑA DEL MAR", _dir.ResolveComunaName("VIÑA DELA MAR")); // 1 edición
        Assert.Equal("CONCÓN", _dir.ResolveComunaName("CONCON"));
    }

    [Fact]
    public void No_resuelve_texto_que_no_es_comuna()
    {
        Assert.Null(_dir.ResolveComunaName("RUT INVALIDO"));
        Assert.Null(_dir.ResolveComunaName("12345"));
        Assert.Null(_dir.ResolveComunaName("xy"));
        Assert.Null(_dir.ResolveComunaName(null));
        Assert.Null(_dir.ResolveComunaName("PENDIENTE F8"));
    }

    [Fact]
    public void No_resuelve_cuando_el_tipeo_esta_demasiado_lejos()
    {
        Assert.Null(_dir.ResolveComunaName("SANTIAGO CENTRO"));
    }

    [Fact]
    public void EmailsFor_devuelve_todos_los_correos_de_la_comuna_sin_repetir()
    {
        var correos = _dir.EmailsFor("CONCÓN");
        Assert.Equal(2, correos.Count);
        Assert.Contains("a@municoncon.cl", correos);
        Assert.Contains("b@municoncon.cl", correos);
    }

    [Fact]
    public void EmailsFor_de_comuna_desconocida_es_vacio()
    {
        Assert.Empty(_dir.EmailsFor("LA SERENA"));
        Assert.Empty(_dir.EmailsFor(null));
    }

    [Fact]
    public void AddOrUpdate_persiste_y_queda_disponible_al_recargar()
    {
        var (ok, _) = _dir.AddOrUpdate("ALGARROBO", "transito@munialgarrobo.cl");
        Assert.True(ok);
        Assert.Single(_dir.EmailsFor("ALGARROBO"));

        var recargado = new ComunaDirectory(_csv);
        Assert.Single(recargado.EmailsFor("ALGARROBO"));
    }

    [Fact]
    public void AddOrUpdate_rechaza_correo_sin_arroba()
    {
        var (ok, _) = _dir.AddOrUpdate("ALGARROBO", "no-es-correo");
        Assert.False(ok);
    }

    [Theory]
    [InlineData("a@@b.cl")]
    [InlineData("uno@x.cl, dos@x.cl")]
    [InlineData("Nombre <a@x.cl>")]
    [InlineData("con espacio@x.cl")]
    public void AddOrUpdate_y_EditarCorreo_rechazan_correos_mal_formados(string correo)
    {
        Assert.False(_dir.AddOrUpdate("ALGARROBO", correo).Ok);
        Assert.False(_dir.EditarCorreo("CONCÓN", "a@municoncon.cl", correo).Ok);
        Assert.Contains("a@municoncon.cl", _dir.EmailsFor("CONCÓN"));
    }

    [Fact]
    public void No_resuelve_con_dos_ediciones_de_distancia()
    {
        Assert.Null(_dir.ResolveComunaName("VIÑA DLA MAR")); // 2 ediciones
    }

    [Fact]
    public void No_resuelve_si_el_tipeo_es_ambiguo_entre_dos_comunas()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"comunas-amb-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(csv, string.Join("\n",
                "Comuna,ContactEmail,Domain",
                "\"TALCA\",\"a@talca.cl\",\"talca.cl\"",
                "\"TALCO\",\"a@talco.cl\",\"talco.cl\""));
            var dir = new ComunaDirectory(csv);

            Assert.Null(dir.ResolveComunaName("TALCX")); // a 1 edición de las dos
            Assert.Equal("TALCA", dir.ResolveComunaName("TALCA"));
        }
        finally
        {
            try { File.Delete(csv); } catch { }
        }
    }

    [Fact]
    public void Persistir_no_deja_temporales_y_el_CSV_queda_completo()
    {
        _dir.AddOrUpdate("ALGARROBO", "transito@munialgarrobo.cl");
        _dir.Eliminar("ALGARROBO", "transito@munialgarrobo.cl");

        var carpeta = Path.GetDirectoryName(_csv)!;
        Assert.Empty(Directory.GetFiles(carpeta, Path.GetFileName(_csv) + "*.tmp"));
        Assert.Equal(4, new ComunaDirectory(_csv).Count);
    }

    [Fact]
    public void Al_cargar_se_ignoran_filas_con_correo_invalido()
    {
        File.WriteAllText(_csv, string.Join("\n",
            "Comuna,ContactEmail,Domain",
            "\"MAL\",\"a@@b\",\"b\"",
            "\"BIEN\",\"ok@bien.cl\",\"bien.cl\""));

        var recargado = new ComunaDirectory(_csv);

        Assert.Equal(1, recargado.Count);
        Assert.Empty(recargado.EmailsFor("MAL"));
    }
}
