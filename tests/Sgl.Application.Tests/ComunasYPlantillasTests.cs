using Microsoft.Extensions.Logging.Abstractions;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Ews;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;
using Sgl.Infrastructure.CambioDomicilio;

namespace Sgl.Application.Tests;

/// <summary>E13: pantalla «Comunas» (directorio, un correo por comuna) y editor de plantillas (B, C y confirmación).</summary>
public sealed class ComunasYPlantillasTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sgl-comunas-ui-{Guid.NewGuid():N}");
    private readonly string _csv;
    private readonly ComunaDirectory _directorio;
    private readonly DirectorioComunasAdapter _adaptador;

    public ComunasYPlantillasTests()
    {
        Directory.CreateDirectory(_dir);
        _csv = Path.Combine(_dir, "comunas.csv");
        File.WriteAllText(_csv, string.Join("\n",
            "Comuna,ContactEmail,Domain",
            "\"MAIPU\",\"licencias@maipu.example.test\",\"maipu.example.test\"",
            "\"RENCA\",\"licencias@renca.example.test\",\"renca.example.test\""));
        _directorio = new ComunaDirectory(_csv);
        _adaptador = new DirectorioComunasAdapter(_directorio);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* archivo aún tomado por el proceso */ }
    }

    private CorreoComunasService Correo() =>
        new(_db.NewContext(), new MailNulo(), _directorio, new AppOptions(), new CambioDomicilioOptions(), new UnidadDeTrabajoGate(), _db.Clock,
            NullLogger<CorreoComunasService>.Instance);

    [Fact]
    public void El_directorio_lista_con_su_correo_y_dominio_ordenado_por_comuna()
    {
        var lista = _adaptador.Listar();

        Assert.Equal(["MAIPU", "RENCA"], lista.Select(c => c.Comuna));
        Assert.Equal("licencias@maipu.example.test", lista[0].Correo);
        Assert.Equal("maipu.example.test", lista[0].Dominio);
    }

    [Fact]
    public void Crear_una_comuna_guarda_en_el_CSV_y_solo_admite_un_correo_por_comuna()
    {
        var (ok, _) = _adaptador.CrearComuna("quilpué", "licencias@quilpue.example.test");
        var (otra, mensaje) = _adaptador.CrearComuna("QUILPUE", "otro@quilpue.example.test");

        Assert.True(ok);
        Assert.False(otra);
        Assert.Contains("ya tiene", mensaje);
        Assert.Contains("licencias@quilpue.example.test", File.ReadAllText(_csv));
        Assert.DoesNotContain("otro@quilpue.example.test", File.ReadAllText(_csv));
        Assert.Equal(["licencias@quilpue.example.test"], _adaptador.CorreosDe("Quilpue"));
        Assert.False(_adaptador.CrearComuna("X", "no-es-correo").Ok);
    }

    [Fact]
    public void Editar_y_eliminar_persisten_en_el_CSV()
    {
        var (editado, _) = _adaptador.EditarCorreo("MAIPU", "licencias@maipu.example.test", "nuevo@maipu.example.test");
        Assert.True(editado);
        Assert.Equal(["nuevo@maipu.example.test"], _adaptador.CorreosDe("MAIPU"));

        var releido = new DirectorioComunasAdapter(new ComunaDirectory(_csv));
        Assert.Equal(["nuevo@maipu.example.test"], releido.CorreosDe("MAIPU"));

        var (eliminado, _) = _adaptador.Eliminar("MAIPU", "nuevo@maipu.example.test");
        Assert.True(eliminado);
        Assert.Empty(new DirectorioComunasAdapter(new ComunaDirectory(_csv)).CorreosDe("MAIPU"));
        Assert.False(_adaptador.Eliminar("MAIPU", "nuevo@maipu.example.test").Ok);
        Assert.False(_adaptador.EditarCorreo("MAIPU", "no@existe.test", "x@y.test").Ok);
    }

    [Fact]
    public void Las_comunas_de_Chile_sin_correo_excluyen_las_del_directorio()
    {
        var sin = _adaptador.ComunasSinCorreo();

        Assert.DoesNotContain(sin, c => string.Equals(_adaptador.NombreDe(c), "MAIPU", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(sin);
    }

    [Fact]
    public async Task Las_tres_plantillas_se_leen_con_su_texto_aprobado_y_se_guardan_editadas()
    {
        var correo = Correo();

        foreach (var clave in PlantillasCambioDomicilio.Claves)
        {
            var p = await correo.ObtenerPlantillaAsync(clave);
            Assert.Equal(clave, p.Clave);
            Assert.False(string.IsNullOrWhiteSpace(p.Cuerpo));
        }
        Assert.Equal(["SinRegistro", "Digital", "ConfirmacionSubida"], PlantillasCambioDomicilio.Claves);

        await correo.GuardarPlantillaAsync("ConfirmacionSubida", "Carpeta lista", "Hola {nombre}, RUT {rut}.");

        var vista = await Correo().VistaPreviaAsync("ConfirmacionSubida", "Ana Prueba", "11.111.111-1", "01-09-2026", null);
        Assert.Equal("Carpeta lista", vista.Asunto);
        Assert.Equal("Hola Ana Prueba, RUT 11.111.111-1.", vista.Cuerpo);
        await Assert.ThrowsAsync<DomainException>(() => Correo().GuardarPlantillaAsync("ConfirmacionSubida", " ", "x"));
        await Assert.ThrowsAsync<DomainException>(() => Correo().GuardarPlantillaAsync("Inventada", "a", "b"));
    }

    [Theory]
    [InlineData(TipoEnvioCorreoComuna.SinRegistro)]
    [InlineData(TipoEnvioCorreoComuna.Digital)]
    [InlineData(TipoEnvioCorreoComuna.ConfirmacionSubida)]
    public void La_clave_de_la_aplicacion_coincide_con_la_de_las_plantillas(TipoEnvioCorreoComuna tipo) =>
        Assert.Equal(PlantillasCambioDomicilio.ClaveDe(tipo), EntrantesService.ClaveDe(tipo));

    private sealed class MailNulo : IMailSender
    {
        public bool IsConfigured => true;

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
