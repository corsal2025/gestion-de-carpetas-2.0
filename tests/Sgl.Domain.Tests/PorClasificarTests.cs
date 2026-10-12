using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class PorClasificarTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static Carpeta NuevaDesdeAgenda() =>
        Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ SOTO"),
            Sede.AvArgentina,
            new DateOnly(2026, 10, 13),
            new TimeOnly(9, 30),
            TramiteAgenda.Renovacion,
            "importador",
            Now);

    public static TheoryData<EstadoCarpeta> Destinos()
    {
        var d = new TheoryData<EstadoCarpeta>();
        foreach (var e in Carpeta.DestinosClasificacion) d.Add(e);
        return d;
    }

    [Fact]
    public void CrearDesdeAgenda_starts_PorClasificar_with_agenda_data()
    {
        var c = NuevaDesdeAgenda();

        Assert.Equal(EstadoCarpeta.PorClasificar, c.Estado);
        Assert.Equal(Decision.Pendiente, c.Decision);
        Assert.Equal(TramiteAgenda.Renovacion, c.TramiteAgenda);
        Assert.Equal(new TimeOnly(9, 30), c.HoraCita);
        Assert.Equal(Bandeja.PorClasificar, c.Bandeja);
        Assert.Single(c.Historial);
    }

    [Fact]
    public void DestinosClasificacion_has_the_13_agreed_states()
    {
        Assert.Equal(13, Carpeta.DestinosClasificacion.Count);
        Assert.Contains(EstadoCarpeta.PrimeraLicencia, Carpeta.DestinosClasificacion);
        Assert.Contains(EstadoCarpeta.CanjeLicExtranjera, Carpeta.DestinosClasificacion);
        Assert.DoesNotContain(EstadoCarpeta.Otorgado, Carpeta.DestinosClasificacion);
    }

    [Theory]
    [MemberData(nameof(Destinos))]
    public void Clasificar_moves_to_target_and_audits(EstadoCarpeta destino)
    {
        var c = NuevaDesdeAgenda();

        c.Clasificar(destino, ClaseLicencia.B, null, null, "javiera", Now);

        Assert.Equal(destino, c.Estado);
        Assert.NotEqual(Bandeja.PorClasificar, c.Bandeja);
        Assert.Contains(c.Historial, h => h.Campo == "Estado" && h.Anterior == "PorClasificar" && h.Nuevo == destino.ToString() && h.Usuario == "javiera");
    }

    [Fact]
    public void Clasificar_sets_fecha_ultima_carpeta_when_given()
    {
        var c = NuevaDesdeAgenda();

        c.Clasificar(EstadoCarpeta.SeEncuentraEnArchivos, ClaseLicencia.B, "15/03/2019", null, "javiera", Now);

        Assert.Equal("15/03/2019", c.FechaUltimaCarpeta);
        Assert.Equal(SectorFisico.Archivo, SectorFisico.Calcular(c.FechaUltimaCarpeta));
    }

    [Fact]
    public void Clasificar_sets_comuna_for_cambio_de_domicilio()
    {
        var c = NuevaDesdeAgenda();

        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, "VIÑA DEL MAR", "javiera", Now);

        Assert.Equal("VIÑA DEL MAR", c.Comuna);
        Assert.Equal(Bandeja.CambioDomicilio, c.Bandeja);
    }

    [Fact]
    public void Clasificar_rejects_comuna_for_non_cambio_domicilio()
    {
        var c = NuevaDesdeAgenda();

        Assert.Throws<DomainException>(() => c.Clasificar(EstadoCarpeta.SubidaConaset, ClaseLicencia.B, null, "VIÑA DEL MAR", "javiera", Now));
        Assert.Equal(EstadoCarpeta.PorClasificar, c.Estado);
    }

    [Fact]
    public void Clasificar_rejects_non_allowed_target()
    {
        var c = NuevaDesdeAgenda();

        Assert.Throws<InvalidTransitionException>(() => c.Clasificar(EstadoCarpeta.Otorgado, ClaseLicencia.B, null, null, "javiera", Now));
    }

    [Fact]
    public void Clasificar_rejects_carpeta_already_classified()
    {
        var c = NuevaDesdeAgenda();
        c.Clasificar(EstadoCarpeta.SubidaConaset, ClaseLicencia.B, null, null, "javiera", Now);

        Assert.Throws<DomainException>(() => c.Clasificar(EstadoCarpeta.SubidaConF8, ClaseLicencia.B, null, null, "javiera", Now));
    }

    [Theory]
    [InlineData(null, Bandeja.PorClasificar)]
    [InlineData(Carpeta.ColaDeCaja, Bandeja.PorClasificar)]
    public void PorClasificar_only_falls_in_PorClasificar(string? caja, Bandeja esperada)
    {
        Assert.Equal(esperada, Carpeta.ClasificarBandeja(EstadoCarpeta.PorClasificar, Decision.Pendiente, caja));
    }
}
