using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>
/// La decisión final (Otorgado / Denegado) solo se toma desde el módulo Otorgamiento (con permiso y fecha de otorgamiento):
/// ni el cambio de estado ni la corrección manual pueden dejarla. La carga histórica de la planilla es la única excepción explícita.
/// </summary>
public class DecisionSoloEnOtorgamientoTests
{
    private static readonly DateTime Despues = new(2026, 3, 12, 15, 30, 0, DateTimeKind.Utc);
    private const string Mensaje = "Otorgar o denegar se hace desde el módulo Otorgamiento.";

    [Theory]
    [InlineData(EstadoCarpeta.EsperaExamen, EstadoCarpeta.Otorgado)]
    [InlineData(EstadoCarpeta.ClasePendiente, EstadoCarpeta.Otorgado)]
    [InlineData(EstadoCarpeta.ParaDenegar, EstadoCarpeta.Denegado)]
    public void CambiarEstado_hacia_un_estado_final_se_rechaza_y_no_deja_decision(EstadoCarpeta desde, EstadoCarpeta hacia)
    {
        var c = CarpetaFactory.InEstado(desde);
        var filas = c.Historial.Count;

        var ex = Assert.Throws<DomainException>(() => c.CambiarEstado(hacia, "ana", Despues));

        Assert.Equal(Mensaje, ex.Message);
        Assert.Equal(desde, c.Estado);
        Assert.Equal(Decision.Pendiente, c.Decision);
        Assert.Null(c.FechaOtorgamiento);
        Assert.Equal(filas, c.Historial.Count);
    }

    [Theory]
    [InlineData(EstadoCarpeta.Otorgado)]
    [InlineData(EstadoCarpeta.Denegado)]
    public void ForzarEstado_hacia_un_estado_final_se_rechaza(EstadoCarpeta final)
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SubidaConaset);

        var ex = Assert.Throws<DomainException>(() => c.ForzarEstado(final, "ana", Despues));

        Assert.Equal(Mensaje, ex.Message);
        Assert.Equal(EstadoCarpeta.SubidaConaset, c.Estado);
        Assert.Equal(Decision.Pendiente, c.Decision);
    }

    [Fact]
    public void El_flujo_legitimo_de_otorgamiento_sigue_funcionando()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.EsperaExamen);

        c.Otorgar("ana", Despues);

        Assert.Equal(Decision.Otorgado, c.Decision);
        Assert.Equal(new DateOnly(2026, 3, 12), c.FechaOtorgamiento);
    }

    [Fact]
    public void Carga_historica_deja_decision_estado_y_origen_sin_fecha_inventada()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SubidaConaset);

        c.AplicarCargaHistorica(null, Decision.Otorgado, null, "importador", Despues);

        Assert.Equal(Decision.Otorgado, c.Decision);
        Assert.Null(c.FechaOtorgamiento);
        Assert.Contains(c.Historial, h => h.Campo == "Origen" && h.Nuevo == "Importación Excel (carga histórica)");
    }

    [Fact]
    public void Carga_historica_estampa_la_fecha_de_otorgamiento_solo_si_viene()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.EsperaExamen);

        c.AplicarCargaHistorica(EstadoCarpeta.Otorgado, Decision.Otorgado, new DateOnly(2026, 2, 1), "importador", Despues);

        Assert.Equal(EstadoCarpeta.Otorgado, c.Estado);
        Assert.Equal(Decision.Otorgado, c.Decision);
        Assert.Equal(new DateOnly(2026, 2, 1), c.FechaOtorgamiento);
    }

    [Fact]
    public void Carga_historica_no_pisa_una_decision_final_ya_tomada()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SubidaConaset);
        c.Otorgar("ana", Despues);

        Assert.Throws<DomainException>(() => c.AplicarCargaHistorica(null, Decision.Denegado, null, "importador", Despues));

        Assert.Equal(Decision.Otorgado, c.Decision);
    }

    [Fact]
    public void Deshacer_tardio_no_revierte_una_decision_final_tomada_despues()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SubidaConaset);
        var punto = c.PuntoRestauracion();
        c.ForzarEstado(EstadoCarpeta.CambioDomSubidoConaset, "ana", Despues, SentidoCambioDomicilio.Saliente);
        c.Otorgar("ana", Despues);

        var ex = Assert.Throws<DomainException>(() => c.RestaurarEstado(punto, EstadoCarpeta.CambioDomSubidoConaset, "ana", Despues));

        Assert.Contains("decisión", ex.Message);
        Assert.Equal(Decision.Otorgado, c.Decision);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, c.Estado);
    }

    [Fact]
    public void El_deshacer_del_propio_otorgamiento_sigue_permitido()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SubidaConaset);
        var punto = c.PuntoRestauracion();
        c.Otorgar("ana", Despues);

        c.RestaurarEstado(punto, punto.Estado, "ana", Despues, permiteDeshacerDecision: true);

        Assert.Equal(Decision.Pendiente, c.Decision);
        Assert.Null(c.FechaOtorgamiento);
    }
}
