using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>Tramo 6: «Otorgamiento» decide las carpetas ya subidas; la resolución intermedia vive aparte del estado.</summary>
public class OtorgamientoTests
{
    // 02:30 UTC del 1 de julio es todavía 30 de junio en Chile: prueba que la fecha sale de la hora de Chile.
    private static readonly DateTime NocheUtc = new(2026, 7, 1, 2, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly HoyChile = new(2026, 6, 30);

    public static TheoryData<EstadoCarpeta> EstadosParaOtorgar => new()
    {
        EstadoCarpeta.SubidaConaset, EstadoCarpeta.SubidaConF8, EstadoCarpeta.SubidaConOficio,
        EstadoCarpeta.CambioDomSubidoConaset, EstadoCarpeta.CambioDomSubidoCorreo, EstadoCarpeta.CanjeLicExtranjera,
        EstadoCarpeta.PrimeraLicencia, EstadoCarpeta.EsperaExamen, EstadoCarpeta.ClasePendiente, EstadoCarpeta.ParaDenegar,
    };

    [Theory]
    [MemberData(nameof(EstadosParaOtorgar))]
    public void Otorgar_estampa_la_fecha_de_hoy_en_Chile_pasa_a_Cerradas_y_limpia_la_resolucion(EstadoCarpeta estado)
    {
        var c = CarpetaFactory.InEstado(estado);
        c.AsignarResolucion(Resolucion.ClasePendiente, "ana", CarpetaFactory.Now);

        c.Otorgar("ana", NocheUtc);

        Assert.Equal(Decision.Otorgado, c.Decision);
        Assert.Equal(HoyChile, c.FechaOtorgamiento);
        Assert.Null(c.Resolucion);
        Assert.Equal(Bandeja.Cerradas, c.Bandeja);
        Assert.Equal(estado, c.Estado);
        Assert.Contains(c.Historial, h => h.Campo == nameof(Carpeta.FechaOtorgamiento) && h.Nuevo == "2026-06-30" && h.Usuario == "ana");
    }

    [Fact]
    public void Denegar_cierra_sin_fecha_de_otorgamiento()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SubidaConaset);

        c.Denegar("ana", NocheUtc);

        Assert.Equal(Decision.Denegado, c.Decision);
        Assert.Null(c.FechaOtorgamiento);
        Assert.Equal(Bandeja.Cerradas, c.Bandeja);
    }

    [Theory]
    [InlineData(EstadoCarpeta.Citada)]
    [InlineData(EstadoCarpeta.CambioDomicilio)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.CrearCertificado)]
    public void Otorgar_Denegar_y_resolucion_fuera_de_los_estados_para_otorgar_lanzan(EstadoCarpeta estado)
    {
        var c = CarpetaFactory.InEstado(estado);

        Assert.False(Carpeta.EsParaOtorgar(estado));
        Assert.Throws<DomainException>(() => c.Otorgar("ana", NocheUtc));
        Assert.Throws<DomainException>(() => c.Denegar("ana", NocheUtc));
        Assert.Throws<DomainException>(() => c.AsignarResolucion(Resolucion.EsperaExamen, "ana", NocheUtc));
        Assert.Equal(Decision.Pendiente, c.Decision);
        Assert.Null(c.FechaOtorgamiento);
    }

    [Fact]
    public void Con_la_decision_ya_final_Otorgar_y_Denegar_lanzan()
    {
        var otorgada = CarpetaFactory.InEstado(EstadoCarpeta.SubidaConaset);
        otorgada.Otorgar("ana", NocheUtc);
        Assert.Throws<DomainException>(() => otorgada.Otorgar("ana", NocheUtc));
        Assert.Throws<DomainException>(() => otorgada.Denegar("ana", NocheUtc));
        Assert.Throws<DomainException>(() => otorgada.AsignarResolucion(Resolucion.EsperaExamen, "ana", NocheUtc));

        var denegada = CarpetaFactory.InEstado(EstadoCarpeta.PrimeraLicencia);
        denegada.Denegar("ana", NocheUtc);
        Assert.Throws<DomainException>(() => denegada.Otorgar("ana", NocheUtc));
        Assert.Null(denegada.FechaOtorgamiento);
    }

    [Fact]
    public void La_resolucion_es_editable_y_cada_cambio_queda_auditado()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SubidaConF8);
        Assert.Null(c.Resolucion);

        c.AsignarResolucion(Resolucion.EsperaExamen, "ana", NocheUtc);
        c.AsignarResolucion(Resolucion.ParaDenegar, "luis", NocheUtc.AddMinutes(1));
        c.AsignarResolucion(Resolucion.ParaDenegar, "luis", NocheUtc.AddMinutes(2));

        Assert.Equal(Resolucion.ParaDenegar, c.Resolucion);
        Assert.Equal(Decision.Pendiente, c.Decision);
        var lineas = c.Historial.Where(h => h.Campo == nameof(Carpeta.Resolucion)).ToList();
        Assert.Equal(2, lineas.Count);
        Assert.Contains(lineas, h => h.Anterior is null && h.Nuevo == "EsperaExamen" && h.Usuario == "ana");
        Assert.Contains(lineas, h => h.Anterior == "EsperaExamen" && h.Nuevo == "ParaDenegar" && h.Usuario == "luis");
    }

    [Theory]
    [InlineData(EstadoCarpeta.EsperaExamen, Resolucion.EsperaExamen)]
    [InlineData(EstadoCarpeta.ClasePendiente, Resolucion.ClasePendiente)]
    [InlineData(EstadoCarpeta.ParaDenegar, Resolucion.ParaDenegar)]
    public void Un_estado_legado_muestra_su_resolucion_derivada_sin_guardar_nada(EstadoCarpeta estado, Resolucion esperada)
    {
        var c = CarpetaFactory.InEstado(estado);

        Assert.Equal(esperada, c.ResolucionEfectiva);
        Assert.Null(c.Resolucion);
        Assert.Equal(estado, c.Estado);
    }

    [Fact]
    public void Una_resolucion_guardada_gana_sobre_la_derivada_del_estado_legado()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.EsperaExamen);

        c.AsignarResolucion(Resolucion.ClasePendiente, "ana", NocheUtc);

        Assert.Equal(Resolucion.ClasePendiente, c.ResolucionEfectiva);
        Assert.Equal(EstadoCarpeta.EsperaExamen, c.Estado);
    }

    [Fact]
    public void El_punto_de_restauracion_devuelve_Pendiente_y_quita_la_fecha_de_otorgamiento_y_restaura_la_resolucion()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.PrimeraLicencia);
        c.AsignarResolucion(Resolucion.EsperaExamen, "ana", CarpetaFactory.Now);
        var punto = c.PuntoRestauracion();
        c.Otorgar("ana", NocheUtc);
        Assert.Equal(Decision.Otorgado, c.Decision);

        c.RestaurarEstado(punto, EstadoCarpeta.PrimeraLicencia, "ana", NocheUtc.AddMinutes(1), permiteDeshacerDecision: true);

        Assert.Equal(Decision.Pendiente, c.Decision);
        Assert.Null(c.FechaOtorgamiento);
        Assert.Equal(Resolucion.EsperaExamen, c.Resolucion);
        Assert.Null(c.FechaCierre);
        Assert.Equal(Bandeja.PorPreparar, c.Bandeja);
    }
}
