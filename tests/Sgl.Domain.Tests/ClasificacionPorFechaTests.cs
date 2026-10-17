using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>R6: in "Por clasificar", a last-folder date alone proposes "Buscar carpeta física" with its destination.</summary>
public class ClasificacionPorFechaTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static Carpeta Nueva() =>
        Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ SOTO"),
            Sede.AvArgentina, new DateOnly(2026, 10, 13), new TimeOnly(9, 30), TramiteAgenda.Renovacion, "importador", Now);

    [Theory]
    [InlineData("30-06-2023", EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData("01-07-2023", EstadoCarpeta.SeEncuentraEnOf43)]
    [InlineData("2019-03-15", EstadoCarpeta.SeEncuentraEnArchivos)]
    public void DestinoPorFecha_maps_the_sector_to_a_search_state(string fecha, EstadoCarpeta esperado)
    {
        Assert.Equal(esperado, Carpeta.DestinoPorFechaUltimaCarpeta(fecha));
    }

    [Theory]
    [InlineData("2023-06-30", EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData("2023-07-01", EstadoCarpeta.SeEncuentraEnOf43)]
    public void DestinoPorFecha_boundary_with_iso_dates_from_the_date_input(string fecha, EstadoCarpeta esperado)
    {
        Assert.Equal(esperado, Carpeta.DestinoPorFechaUltimaCarpeta(fecha));
    }

    [Fact]
    public void Without_a_date_the_options_exclude_archivos_and_of43()
    {
        var opciones = Carpeta.OpcionesClasificacionSegunFecha(Carpeta.DestinosClasificacion, null);

        Assert.DoesNotContain(EstadoCarpeta.SeEncuentraEnArchivos, opciones);
        Assert.DoesNotContain(EstadoCarpeta.SeEncuentraEnOf43, opciones);
        Assert.Contains(EstadoCarpeta.PrimeraLicencia, opciones);
        Assert.Contains(EstadoCarpeta.CambioDomicilio, opciones);
        Assert.Contains(EstadoCarpeta.CanjeLicExtranjera, opciones);
    }

    [Theory]
    [InlineData("")]
    [InlineData("xx")]
    public void Unreadable_date_counts_as_no_date_for_the_options(string fecha)
    {
        var opciones = Carpeta.OpcionesClasificacionSegunFecha(Carpeta.DestinosClasificacion, fecha);

        Assert.DoesNotContain(EstadoCarpeta.SeEncuentraEnArchivos, opciones);
        Assert.DoesNotContain(EstadoCarpeta.SeEncuentraEnOf43, opciones);
    }

    [Fact]
    public void With_a_date_the_options_are_unchanged()
    {
        var opciones = Carpeta.OpcionesClasificacionSegunFecha(Carpeta.DestinosClasificacion, "2020-01-01");

        Assert.Equal(Carpeta.DestinosClasificacion, opciones);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xx")]
    public void DestinoPorFecha_is_null_without_a_valid_date(string? fecha)
    {
        Assert.Null(Carpeta.DestinoPorFechaUltimaCarpeta(fecha));
    }

    [Fact]
    public void Resolver_respects_an_explicit_state_even_when_a_date_is_present()
    {
        var destino = Carpeta.ResolverDestinoClasificacion(EstadoCarpeta.CambioDomicilio, "01-01-2019", null);

        Assert.Equal(EstadoCarpeta.CambioDomicilio, destino);
    }

    [Fact]
    public void Resolver_defaults_to_buscar_carpeta_fisica_from_the_entered_date()
    {
        Assert.Equal(EstadoCarpeta.SeEncuentraEnOf43, Carpeta.ResolverDestinoClasificacion(null, "05-08-2024", null));
    }

    [Fact]
    public void Resolver_falls_back_to_the_stored_date_when_none_is_entered()
    {
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, Carpeta.ResolverDestinoClasificacion(null, null, "2020-02-02"));
    }

    [Fact]
    public void Resolver_prefers_the_entered_date_over_the_stored_one()
    {
        Assert.Equal(EstadoCarpeta.SeEncuentraEnOf43, Carpeta.ResolverDestinoClasificacion(null, "2024-01-01", "2020-02-02"));
    }

    [Fact]
    public void Resolver_rejects_when_there_is_neither_state_nor_date()
    {
        var ex = Assert.Throws<DomainException>(() => Carpeta.ResolverDestinoClasificacion(null, " ", null));
        Assert.Contains("fecha", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolver_rejects_an_unreadable_date_without_an_explicit_state()
    {
        var ex = Assert.Throws<DomainException>(() => Carpeta.ResolverDestinoClasificacion(null, "ayer", null));
        Assert.Contains("válida", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clasificar_stores_the_date_as_iso_when_it_is_recognized()
    {
        var c = Nueva();

        c.Clasificar(EstadoCarpeta.SeEncuentraEnOf43, ClaseLicencia.B, "05-08-2024", null, "javiera", Now);

        Assert.Equal("2024-08-05", c.FechaUltimaCarpeta);
    }

    [Fact]
    public void Clasificar_keeps_legacy_free_text_when_the_date_cannot_be_read()
    {
        var c = Nueva();

        c.Clasificar(EstadoCarpeta.SubidaConaset, ClaseLicencia.B, "dato antiguo", null, "javiera", Now);

        Assert.Equal("dato antiguo", c.FechaUltimaCarpeta);
    }
}
