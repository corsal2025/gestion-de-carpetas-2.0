using System.Xml.Linq;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Ews;

namespace Sgl.Peticiones.Tests;

public sealed class EwsLecturaYComunaRemitenteTests : IDisposable
{
    private const string T = "http://schemas.microsoft.com/exchange/services/2006/types";
    private const string M = "http://schemas.microsoft.com/exchange/services/2006/messages";
    private readonly string _csv = Path.Combine(Path.GetTempPath(), $"comunas-{Guid.NewGuid():N}.csv");

    [Fact]
    public async Task SinUrlOClave_ElLectorNoEstaConfiguradoYNoLee()
    {
        using var lector = new EwsEmailReader(new AppOptions { Ews = new EwsOptions { Url = "https://mail.example/ews", Username = "u" } });

        Assert.False(lector.EstaConfigurado);
        Assert.Empty(await lector.LeerCarpetaAsync("CARP. PARA PEDIR"));
    }

    [Fact]
    public void CarpetasPorDefecto()
    {
        var ews = new EwsOptions();
        Assert.Equal("CARP. PARA PEDIR", ews.CarpetaParaPedir);
        Assert.Equal("CARP. YA SUBIDAS", ews.CarpetaYaSubidas);
    }

    [Fact]
    public void FindFolder_EscapaElNombreYUsaElBuzonCompartido()
    {
        var xml = XDocument.Parse(EwsLectura.FindFolder("CARP. <PARA> PEDIR", "depto@muni.cl"));

        Assert.Equal("CARP. <PARA> PEDIR", xml.Descendants(XName.Get("Constant", T)).Single().Attribute("Value")!.Value);
        Assert.Equal("depto@muni.cl", xml.Descendants(XName.Get("EmailAddress", T)).Single().Value);
    }

    [Fact]
    public void ParsearGetItem_OmiteMensajesSinMessageId()
    {
        var doc = XDocument.Parse($"""
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:m="{M}" xmlns:t="{T}"><s:Body>
              <m:GetItemResponse><m:ResponseMessages><m:GetItemResponseMessage ResponseClass="Success"><m:Items>
                <t:Message><t:Subject>Solicitud</t:Subject><t:Body>RUT 18.785.387-7</t:Body>
                  <t:DateTimeReceived>2026-05-02T15:00:00Z</t:DateTimeReceived>
                  <t:From><t:Mailbox><t:EmailAddress>partes@munivina.cl</t:EmailAddress></t:Mailbox></t:From>
                  <t:InternetMessageId>&lt;a@x&gt;</t:InternetMessageId></t:Message>
                <t:Message><t:Subject>Sin id</t:Subject></t:Message>
              </m:Items></m:GetItemResponseMessage></m:ResponseMessages></m:GetItemResponse></s:Body></s:Envelope>
            """);

        var correo = Assert.Single(EwsLectura.ParsearGetItem(doc));

        Assert.Equal("<a@x>", correo.MessageId);
        Assert.Equal("partes@munivina.cl", correo.Remitente);
        Assert.Equal(new DateTimeOffset(2026, 5, 2, 15, 0, 0, TimeSpan.Zero), correo.RecibidoEn);
    }

    [Fact]
    public void RespuestaDeError_Lanza()
    {
        var doc = XDocument.Parse($"""
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" xmlns:m="{M}"><s:Body>
              <m:FindFolderResponse><m:ResponseMessages><m:FindFolderResponseMessage ResponseClass="Error">
                <m:ResponseCode>ErrorAccessDenied</m:ResponseCode></m:FindFolderResponseMessage></m:ResponseMessages>
              </m:FindFolderResponse></s:Body></s:Envelope>
            """);

        Assert.Throws<EwsSendException>(() => EwsLectura.ParsearFindFolder(doc));
    }

    [Fact]
    public void ComunaRemitente_CorreoExactoLuegoDominioUnico()
    {
        File.WriteAllText(_csv, "comuna,email,domain\nViña del Mar,partes@munivina.cl,munivina.cl\nQuilpué,oficina@quilpue.cl,quilpue.cl\nQuilpué,otra@quilpue.cl,quilpue.cl\n");
        var directorio = new ComunaDirectory(_csv);
        Assert.Equal(3, directorio.Count);
        var resolver = new ComunaRemitente(directorio);

        Assert.Equal("Viña del Mar", resolver.ComunaDe("PARTES@munivina.cl"));
        Assert.Equal("Viña del Mar", resolver.ComunaDe("otro@munivina.cl"));
        Assert.Equal("Quilpué", resolver.ComunaDe("x@quilpue.cl"));
        Assert.Null(resolver.ComunaDe("alguien@gmail.com"));
        Assert.Null(resolver.ComunaDe("sin-arroba"));
    }

    public void Dispose()
    {
        if (File.Exists(_csv)) File.Delete(_csv);
    }
}
