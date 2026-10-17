using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class SolicitudF8Tests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Carpeta = Guid.NewGuid();

    [Theory]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43)]
    [InlineData(EstadoCarpeta.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    public void Solicitar_records_origin_author_and_utc_instant(EstadoCarpeta origen)
    {
        var s = SolicitudF8.Solicitar(Carpeta, origen, "  ana ", Now);

        Assert.NotEqual(Guid.Empty, s.Id);
        Assert.Equal(Carpeta, s.CarpetaId);
        Assert.Equal(origen, s.EstadoOrigen);
        Assert.Equal("ana", s.SolicitadoPor);
        Assert.Equal(Now, s.SolicitadoEn);
        Assert.Equal(EstadoSolicitudF8.Solicitado, s.Estado);
        Assert.True(s.EstaAbierta);
        Assert.Null(s.ResueltoPor);
        Assert.Null(s.ResueltoEn);
    }

    [Theory]
    [InlineData(EstadoCarpeta.Citada)]
    [InlineData(EstadoCarpeta.Otorgado)]
    [InlineData(EstadoCarpeta.PorClasificar)]
    [InlineData(EstadoCarpeta.SubidaConF8)]
    public void Solicitar_is_refused_from_states_that_cannot_request_an_F8(EstadoCarpeta origen)
    {
        Assert.False(SolicitudF8.PuedeSolicitarseDesde(origen));
        Assert.Throws<DomainException>(() => SolicitudF8.Solicitar(Carpeta, origen, "ana", Now));
    }

    [Fact]
    public void Solicitar_requires_carpeta_and_author()
    {
        Assert.Throws<DomainException>(() => SolicitudF8.Solicitar(Guid.Empty, EstadoCarpeta.NoExisteCarpeta, "ana", Now));
        Assert.Throws<DomainException>(() => SolicitudF8.Solicitar(Carpeta, EstadoCarpeta.NoExisteCarpeta, " ", Now));
        Assert.Throws<DomainException>(() => SolicitudF8.Solicitar(Carpeta, EstadoCarpeta.NoExisteCarpeta, new string('x', 101), Now));
    }

    [Fact]
    public void MarcarSubido_closes_the_request_with_who_when_and_note()
    {
        var s = SolicitudF8.Solicitar(Carpeta, EstadoCarpeta.NoExisteCarpeta, "ana", Now);

        s.MarcarSubido("javiera", Now.AddHours(2), "  subida ok ");

        Assert.Equal(EstadoSolicitudF8.Subido, s.Estado);
        Assert.False(s.EstaAbierta);
        Assert.Equal("javiera", s.ResueltoPor);
        Assert.Equal(Now.AddHours(2), s.ResueltoEn);
        Assert.Equal("subida ok", s.Observacion);
    }

    [Fact]
    public void MarcarNoSeGenero_closes_the_request_as_not_generated()
    {
        var s = SolicitudF8.Solicitar(Carpeta, EstadoCarpeta.SeEncuentraEnOf43, "ana", Now);

        s.MarcarNoSeGenero("javiera", Now.AddHours(1));

        Assert.Equal(EstadoSolicitudF8.NoSeGenero, s.Estado);
        Assert.Equal("javiera", s.ResueltoPor);
        Assert.Null(s.Observacion);
    }

    [Fact]
    public void A_resolved_request_cannot_be_resolved_again()
    {
        var s = SolicitudF8.Solicitar(Carpeta, EstadoCarpeta.NoExisteCarpeta, "ana", Now);
        s.MarcarSubido("javiera", Now);

        Assert.Throws<DomainException>(() => s.MarcarSubido("javiera", Now));
        Assert.Throws<DomainException>(() => s.MarcarNoSeGenero("javiera", Now));
    }

    [Fact]
    public void Resolution_requires_author_and_bounded_note()
    {
        var s = SolicitudF8.Solicitar(Carpeta, EstadoCarpeta.NoExisteCarpeta, "ana", Now);

        Assert.Throws<DomainException>(() => s.MarcarSubido("", Now));
        Assert.Throws<DomainException>(() => s.MarcarNoSeGenero("javiera", Now, new string('x', SolicitudF8.MaxObservacionLength + 1)));
        Assert.True(s.EstaAbierta);
    }
}
