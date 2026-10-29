using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>Domain rules added by the audit improvement round (state validation, dates, undo, request cancel).</summary>
public sealed class Oleada1DominioTests
{
    [Fact]
    public void ForzarEstado_rejects_a_value_that_is_not_a_state()
    {
        var c = CarpetaFactory.New();

        Assert.Throws<DomainException>(() => c.ForzarEstado((EstadoCarpeta)9999, "ana", CarpetaFactory.Now));
        Assert.Equal(EstadoCarpeta.Citada, c.Estado);
    }

    [Fact]
    public void Editar_rejects_an_empty_citation_date_and_a_future_upload_date()
    {
        var c = CarpetaFactory.New();

        Assert.Throws<DomainException>(() => c.Editar(c.Sede, default, null, null, "ana", CarpetaFactory.Now));
        Assert.Throws<DomainException>(() => c.Editar(c.Sede, c.FechaCitacion, new DateOnly(2026, 3, 11), null, "ana", CarpetaFactory.Now));
        c.Editar(c.Sede, c.FechaCitacion, new DateOnly(2026, 3, 10), null, "ana", CarpetaFactory.Now);
        Assert.Equal(new DateOnly(2026, 3, 10), c.FechaSubida);
    }

    [Fact]
    public void Registrar_rejects_an_empty_citation_date()
    {
        var ciudadano = new Ciudadano(Rut.Parse("12345678-5"), "Juan", "Pérez");

        Assert.Throws<DomainException>(() => Carpeta.Registrar(ciudadano, Sede.Placilla, default, "ana", CarpetaFactory.Now));
    }

    [Fact]
    public void Reagendar_is_refused_when_the_upload_date_would_precede_the_new_citation()
    {
        var c = CarpetaFactory.New();
        c.Editar(c.Sede, c.FechaCitacion, new DateOnly(2026, 3, 10), null, "ana", CarpetaFactory.Now);

        Assert.Throws<DomainException>(() => c.Reagendar(new DateOnly(2026, 3, 20), c.Sede, "ana", CarpetaFactory.Now));
        Assert.Equal(new DateOnly(2026, 3, 10), c.FechaCitacion);
    }

    [Fact]
    public void Undo_restores_the_request_date_the_move_found()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.CrearCertificado);
        c.RegistrarPeticion("ana", CarpetaFactory.Now);
        var punto = c.PuntoRestauracion();
        Assert.Equal(CarpetaFactory.Now, punto.FechaPeticion);
        c.ForzarEstado(EstadoCarpeta.SubidaConOficio, "ana", CarpetaFactory.Now);

        c.RestaurarEstado(punto, EstadoCarpeta.SubidaConOficio, "ana", CarpetaFactory.Now);

        Assert.Equal(CarpetaFactory.Now, c.FechaPeticion);
    }

    [Fact]
    public void RegistrarEvento_adds_an_audited_line_and_requires_an_author()
    {
        var c = CarpetaFactory.New();

        c.RegistrarEvento("Certificado", null, "Solicitado: Oficio", "ana", CarpetaFactory.Now);

        Assert.Contains(c.Historial, h => h.Campo == "Certificado" && h.Nuevo == "Solicitado: Oficio" && h.Usuario == "ana");
        Assert.Throws<DomainException>(() => c.RegistrarEvento("Certificado", null, "x", " ", CarpetaFactory.Now));
    }

    [Fact]
    public void An_open_F8_request_can_be_cancelled_once()
    {
        var f8 = SolicitudF8.Solicitar(Guid.NewGuid(), EstadoCarpeta.SeEncuentraEnArchivos, "ana", CarpetaFactory.Now);

        f8.Cancelar("ana", CarpetaFactory.Now, "obsoleta");

        Assert.Equal(EstadoSolicitudF8.Cancelada, f8.Estado);
        Assert.False(f8.EstaAbierta);
        Assert.Throws<DomainException>(() => f8.Cancelar("ana", CarpetaFactory.Now));
    }

    [Theory]
    [InlineData("02-07-2023", true)]
    [InlineData("2023-07-02", true)]
    [InlineData("2 de julio de 2023", true)]
    [InlineData("12/31/2023", false)] // month-first is not read: there is no 31st month
    [InlineData("2099-01-01", false)] // future dates are never a valid "última carpeta"
    [InlineData("", false)]
    public void SectorFisico_reads_day_first_only_and_refuses_the_future(string texto, bool valida)
    {
        Assert.Equal(valida, SectorFisico.TryParseFecha(texto, out _));
    }

    [Fact]
    public void SectorFisico_does_not_fall_back_to_month_first()
    {
        // "03/04/2023" is 3 April (day-first), never March 4.
        Assert.True(SectorFisico.TryParseFecha("03/04/2023", out var d));
        Assert.Equal(new DateOnly(2023, 4, 3), d);
    }

    [Fact]
    public void An_invalid_RUT_message_does_not_echo_the_input()
    {
        var ex = Assert.Throws<DomainException>(() => Rut.Parse("12345678-0"));

        Assert.DoesNotContain("12345678", ex.Message);
    }

    [Fact]
    public void Autores_is_the_single_author_rule()
    {
        Assert.Equal("ana", Autores.Requerir("  ana "));
        Assert.Throws<DomainException>(() => Autores.Requerir(null));
        Assert.Throws<DomainException>(() => Autores.Requerir(new string('x', Carpeta.MaxUsuarioLength + 1)));
    }
}
