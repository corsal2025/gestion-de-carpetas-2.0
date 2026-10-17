using Sgl.Application.Solicitudes;
using Sgl.Domain;

namespace Sgl.Application.Tests;

public sealed class PresentacionSolicitudesTests
{
    [Fact]
    public void Contadores_TomanSoloLosEstadosAbiertos()
    {
        var f8 = new Dictionary<EstadoSolicitudF8, int>
        {
            [EstadoSolicitudF8.Solicitado] = 4,
            [EstadoSolicitudF8.Subido] = 9,
        };
        var cert = new Dictionary<EstadoCertificado, int>
        {
            [EstadoCertificado.PorFirmar] = 2,
            [EstadoCertificado.Firmado] = 7,
        };

        var c = ContadoresSolicitudes.Desde(f8, cert);

        Assert.Equal(4, c.F8Solicitados);
        Assert.Equal(2, c.CertificadosPorFirmar);
    }

    [Fact]
    public void Contadores_EstadosAusentesSonCero()
    {
        var c = ContadoresSolicitudes.Desde(
            new Dictionary<EstadoSolicitudF8, int>(), new Dictionary<EstadoCertificado, int>());

        Assert.Equal(ContadoresSolicitudes.Vacio, c);
        Assert.Equal(0, c.F8Solicitados);
    }

    [Theory]
    [InlineData(EstadoSolicitudF8.Solicitado, "Solicitado")]
    [InlineData(EstadoSolicitudF8.Subido, "Subido")]
    [InlineData(EstadoSolicitudF8.NoSeGenero, "No se generó")]
    public void Etiqueta_EstadoF8(EstadoSolicitudF8 estado, string esperado) =>
        Assert.Equal(esperado, EtiquetasSolicitudes.Of(estado));

    [Theory]
    [InlineData(TipoCertificado.SinCarpeta, "Sin carpeta")]
    [InlineData(TipoCertificado.RegistroCivil, "Registro Civil")]
    [InlineData(TipoCertificado.Oficio, "Oficio")]
    public void Etiqueta_Tipo(TipoCertificado tipo, string esperado) =>
        Assert.Equal(esperado, EtiquetasSolicitudes.Of(tipo));

    [Theory]
    [InlineData(OrigenCertificado.DesdeF8, "Desde F8")]
    [InlineData(OrigenCertificado.Manual, "Manual")]
    public void Etiqueta_Origen(OrigenCertificado origen, string esperado) =>
        Assert.Equal(esperado, EtiquetasSolicitudes.Of(origen));

    [Theory]
    [InlineData(EstadoCertificado.PorFirmar, "Por firmar")]
    [InlineData(EstadoCertificado.Firmado, "Firmado")]
    public void Etiqueta_EstadoCertificado(EstadoCertificado estado, string esperado) =>
        Assert.Equal(esperado, EtiquetasSolicitudes.Of(estado));

    [Fact]
    public void MensajeF8_Creada()
    {
        var r = new SolicitarF8Resultado(Guid.NewGuid(), true, false, null);
        Assert.Equal("Se creó la solicitud de F8 para JUAN PEREZ.", MensajesSolicitudes.F8Solicitada(r, "JUAN PEREZ"));
    }

    [Fact]
    public void MensajeF8_YaAbiertaUsaElMensajeDelServicio()
    {
        var r = new SolicitarF8Resultado(Guid.NewGuid(), false, true, "Ya existe una solicitud de F8 abierta para esta carpeta.");
        Assert.Equal("Ya existe una solicitud de F8 abierta para esta carpeta.", MensajesSolicitudes.F8Solicitada(r, "X"));
    }

    [Fact]
    public void MensajeF8_YaAbiertaSinMensajeTieneRespaldo()
    {
        var r = new SolicitarF8Resultado(Guid.NewGuid(), false, true, null);
        Assert.Contains("ya tiene una solicitud de F8 abierta", MensajesSolicitudes.F8Solicitada(r, "ANA"));
    }

    [Fact]
    public void MensajeNoSeGenero_IndicaCertificadoNuevoOExistente()
    {
        var nuevo = new NoSeGeneroResultado(Guid.NewGuid(), false, true, null);
        var existente = new NoSeGeneroResultado(Guid.NewGuid(), true, false, null);
        var fallo = new NoSeGeneroResultado(Guid.NewGuid(), false, false, "sin correo");

        Assert.Contains("se abrió una solicitud de certificado", MensajesSolicitudes.NoSeGenero(nuevo));
        Assert.Contains("ya existía", MensajesSolicitudes.NoSeGenero(existente));
        Assert.Contains("sin correo", MensajesSolicitudes.NoSeGenero(fallo));
    }

    [Fact]
    public void EstadoCorreo_dice_si_salio_no_salio_o_fallo()
    {
        Assert.Equal("Correo enviado al director.", MensajesSolicitudes.EstadoCorreo(true, null));
        Assert.Equal("Correo: no enviado (modo prueba / sin correo del Director).", MensajesSolicitudes.EstadoCorreo(false, null));
        Assert.Equal("No se pudo avisar al director: falla.", MensajesSolicitudes.EstadoCorreo(false, "falla."));
    }

    [Fact]
    public void MensajeNoSeGenero_incluye_el_estado_del_correo_solo_si_se_intento_avisar()
    {
        var nuevoSinCorreo = new NoSeGeneroResultado(Guid.NewGuid(), false, false, null);
        var existente = new NoSeGeneroResultado(Guid.NewGuid(), true, false, null);

        Assert.Contains("Correo: no enviado", MensajesSolicitudes.NoSeGenero(nuevoSinCorreo));
        Assert.DoesNotContain("Correo", MensajesSolicitudes.NoSeGenero(existente));
    }
}
