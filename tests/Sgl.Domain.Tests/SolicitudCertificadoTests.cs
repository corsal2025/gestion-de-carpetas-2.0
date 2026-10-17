using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class SolicitudCertificadoTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Carpeta = Guid.NewGuid();

    [Fact]
    public void SinCarpeta_comes_from_an_F8_and_starts_unsigned()
    {
        var f8 = Guid.NewGuid();

        var c = SolicitudCertificado.CrearDesdeF8(Carpeta, f8, " ana ", Now);

        Assert.Equal(TipoCertificado.SinCarpeta, c.Tipo);
        Assert.Equal(OrigenCertificado.DesdeF8, c.Origen);
        Assert.Equal(f8, c.SolicitudF8Id);
        Assert.Equal(Carpeta, c.CarpetaId);
        Assert.Equal("ana", c.SolicitadoPor);
        Assert.Equal(Now, c.SolicitadoEn);
        Assert.Equal(EstadoCertificado.PorFirmar, c.Estado);
        Assert.True(c.EstaAbierta);
        Assert.Null(c.EnviadoADirectorEn);
        Assert.Empty(c.Parametros);
    }

    [Fact]
    public void SinCarpeta_from_Pedir_is_manual_without_an_F8_and_starts_unsigned()
    {
        var c = SolicitudCertificado.CrearDesdePedir(Carpeta, " ana ", Now);

        Assert.Equal(TipoCertificado.SinCarpeta, c.Tipo);
        Assert.Equal(OrigenCertificado.Manual, c.Origen);
        Assert.Null(c.SolicitudF8Id);
        Assert.Equal("ana", c.SolicitadoPor);
        Assert.Equal(Now, c.SolicitadoEn);
        Assert.True(c.EstaAbierta);
    }

    [Fact]
    public void SinCarpeta_from_Pedir_requires_carpeta_and_author()
    {
        Assert.Throws<DomainException>(() => SolicitudCertificado.CrearDesdePedir(Guid.Empty, "ana", Now));
        Assert.Throws<DomainException>(() => SolicitudCertificado.CrearDesdePedir(Carpeta, " ", Now));
    }

    [Fact]
    public void Manual_creation_still_rejects_SinCarpeta()
    {
        Assert.Throws<DomainException>(() => SolicitudCertificado.CrearManual(Carpeta, TipoCertificado.SinCarpeta, null, null, "ana", Now));
    }

    [Fact]
    public void SinCarpeta_requires_the_F8_request()
    {
        Assert.Throws<DomainException>(() => SolicitudCertificado.CrearDesdeF8(Carpeta, Guid.Empty, "ana", Now));
    }

    [Fact]
    public void Manual_registro_civil_keeps_its_parameters()
    {
        var c = SolicitudCertificado.CrearManual(
            Carpeta,
            TipoCertificado.RegistroCivil,
            new Dictionary<string, string> { ["Clase"] = "B", ["Ley"] = "18290", ["FechaOtorgamiento"] = "2020-01-02", ["Vacio"] = "  " },
            null,
            "ana",
            Now);

        Assert.Equal(OrigenCertificado.Manual, c.Origen);
        Assert.Null(c.SolicitudF8Id);
        Assert.Equal("B", c.Parametros["Clase"]);
        Assert.Equal("18290", c.Parametros["Ley"]);
        Assert.False(c.Parametros.ContainsKey("Vacio"));
    }

    [Fact]
    public void Manual_cannot_create_a_SinCarpeta()
    {
        Assert.Throws<DomainException>(() =>
            SolicitudCertificado.CrearManual(Carpeta, TipoCertificado.SinCarpeta, null, null, "ana", Now));
    }

    [Fact]
    public void Numero_oficio_is_only_for_oficio()
    {
        var oficio = SolicitudCertificado.CrearManual(Carpeta, TipoCertificado.Oficio, null, " 123/2026 ", "ana", Now);
        Assert.Equal("123/2026", oficio.NumeroOficio);

        Assert.Throws<DomainException>(() =>
            SolicitudCertificado.CrearManual(Carpeta, TipoCertificado.RegistroCivil, null, "123", "ana", Now));
    }

    [Fact]
    public void Parameters_are_bounded()
    {
        var demasiados = Enumerable.Range(0, SolicitudCertificado.MaxParametros + 1).ToDictionary(i => $"K{i}", _ => "v");
        Assert.Throws<DomainException>(() => SolicitudCertificado.CrearManual(Carpeta, TipoCertificado.Oficio, demasiados, null, "ana", Now));
        Assert.Throws<DomainException>(() => SolicitudCertificado.CrearManual(
            Carpeta, TipoCertificado.Oficio, new Dictionary<string, string> { ["Motivo"] = new string('x', SolicitudCertificado.MaxValorParametro + 1) }, null, "ana", Now));
        Assert.Throws<DomainException>(() => SolicitudCertificado.CrearManual(
            Carpeta, TipoCertificado.Oficio, new Dictionary<string, string> { [""] = "x" }, null, "ana", Now));
    }

    [Fact]
    public void AnioRenovacion_must_be_a_plausible_year()
    {
        var c = SolicitudCertificado.CrearDesdeF8(Carpeta, Guid.NewGuid(), "ana", Now);

        c.ActualizarDatos(new Dictionary<string, string> { [SolicitudCertificado.ClaveAnioRenovacion] = "2019" }, null, "pendiente de firma");
        Assert.Equal("2019", c.Parametros[SolicitudCertificado.ClaveAnioRenovacion]);
        Assert.Equal("pendiente de firma", c.Observacion);

        Assert.Throws<DomainException>(() =>
            c.ActualizarDatos(new Dictionary<string, string> { [SolicitudCertificado.ClaveAnioRenovacion] = "abc" }, null, null));
        Assert.Throws<DomainException>(() =>
            c.ActualizarDatos(new Dictionary<string, string> { [SolicitudCertificado.ClaveAnioRenovacion] = "1850" }, null, null));
    }

    [Fact]
    public void MarcarFirmado_records_signer_and_optionally_the_oficio_number()
    {
        var c = SolicitudCertificado.CrearManual(Carpeta, TipoCertificado.Oficio, null, null, "ana", Now);

        c.MarcarFirmado("javiera", Now.AddDays(1), "55/2026");

        Assert.Equal(EstadoCertificado.Firmado, c.Estado);
        Assert.False(c.EstaAbierta);
        Assert.Equal("javiera", c.FirmadoPor);
        Assert.Equal(Now.AddDays(1), c.FirmadoEn);
        Assert.Equal("55/2026", c.NumeroOficio);
    }

    [Fact]
    public void A_signed_certificate_is_frozen()
    {
        var c = SolicitudCertificado.CrearDesdeF8(Carpeta, Guid.NewGuid(), "ana", Now);
        c.MarcarFirmado("javiera", Now);

        Assert.Throws<DomainException>(() => c.MarcarFirmado("javiera", Now));
        Assert.Throws<DomainException>(() => c.ActualizarDatos(null, null, "x"));
    }

    [Fact]
    public void Sending_to_the_director_is_recorded_once()
    {
        var c = SolicitudCertificado.CrearDesdeF8(Carpeta, Guid.NewGuid(), "ana", Now);
        Assert.Null(c.EnviadoADirectorEn);

        c.RegistrarEnvioADirector(Now.AddMinutes(1));
        c.RegistrarEnvioADirector(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(1), c.EnviadoADirectorEn);
    }

    [Fact]
    public void Firmado_requires_author()
    {
        var c = SolicitudCertificado.CrearDesdeF8(Carpeta, Guid.NewGuid(), "ana", Now);
        Assert.Throws<DomainException>(() => c.MarcarFirmado(" ", Now));
        Assert.True(c.EstaAbierta);
    }
}
