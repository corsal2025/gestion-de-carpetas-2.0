using System.Xml.Linq;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Ews;

namespace PeticionCambioDomicilio.Tests;

public sealed class EwsSeguridadTests
{
    [Fact]
    public void SinControl_QuitaCaracteresInvalidosEnXmlYConservaSaltosYTildes()
    {
        var sucio = "Pérez\u0001 Gón\u000bzález\u0000\ta\nb\r\n￾";

        var limpio = TextoSeguro.SinControl(sucio);

        Assert.Equal("Pérez Gónzález\ta\nb\r\n", limpio);
        Assert.Equal("", TextoSeguro.SinControl(null));
    }

    [Fact]
    public void SinControl_ConservaParesSurrogadosYQuitaSurrogadosSueltos()
    {
        Assert.Equal("a😀b", TextoSeguro.SinControl("a😀b"));
        Assert.Equal("ab", TextoSeguro.SinControl("a\uD800b"));
    }

    [Fact]
    public void ElSobreSoap_NoContieneControlesAunqueAsuntoYCuerpoLosTraigan()
    {
        var xml = EwsMailSender.BuildSendMailRequest("a@b.cl", "Asunto\u0001", "Cuerpo\u0002 ok", null);

        var doc = XDocument.Parse(xml); // con caracteres de control habría lanzado XmlException
        Assert.Contains("Cuerpo ok", doc.ToString());
    }

    [Fact]
    public void VariosDestinatarios_SeEmitenComoVariosMailbox()
    {
        var xml = EwsMailSender.BuildSendMailRequest("uno@x.cl; dos@x.cl, tres@x.cl", "s", "b", null);

        var correos = XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName == "EmailAddress").Select(e => e.Value).ToList();
        Assert.Equal(new[] { "uno@x.cl", "dos@x.cl", "tres@x.cl" }, correos);
    }

    [Theory]
    [InlineData("http://mail.example/ews")]
    [InlineData("ftp://mail.example/ews")]
    [InlineData("mail.example/ews")]
    public void UrlNoHttps_NoQuedaConfigurada(string url)
    {
        var opciones = new AppOptions { Ews = new EwsOptions { Url = url, Username = "u", Password = "p" } };

        using var sender = new EwsMailSender(opciones);
        using var lector = new EwsEmailReader(opciones);

        Assert.False(sender.IsConfigured);
        Assert.False(lector.EstaConfigurado);
    }

    [Fact]
    public async Task UrlNoHttps_EnviarExplicaElMotivo()
    {
        using var sender = new EwsMailSender(new AppOptions { Ews = new EwsOptions { Url = "http://x/ews", Username = "u", Password = "p" } });

        var ex = await Assert.ThrowsAsync<EwsSendException>(() => sender.SendAsync("a@b.cl", "s", "b", CancellationToken.None));

        Assert.Contains("https", ex.Message);
    }

    [Fact]
    public void UrlHttps_QuedaConfigurada()
    {
        using var sender = new EwsMailSender(new AppOptions { Ews = new EwsOptions { Url = "https://mail.example/ews", Username = "u", Password = "p" } });

        Assert.True(sender.IsConfigured);
    }

    [Fact]
    public void XmlMalformado_SeTraduceAEwsSendException()
    {
        var ex = Assert.Throws<EwsSendException>(() => EwsXml.Parsear("<html><body>Proxy"));

        Assert.DoesNotContain("<html>", ex.Message);
    }

    [Fact]
    public void XmlValido_SeParsea()
    {
        Assert.NotNull(EwsXml.Parsear("<a><b/></a>"));
    }
}
