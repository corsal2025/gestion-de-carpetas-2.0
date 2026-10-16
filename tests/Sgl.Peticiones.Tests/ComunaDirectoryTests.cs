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
    public void Resuelve_con_tolerancia_a_tipeos_de_hasta_dos()
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
}
