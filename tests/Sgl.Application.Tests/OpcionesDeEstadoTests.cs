using Sgl.Domain;
using Sgl.Web.Services;

namespace Sgl.Application.Tests;

/// <summary>M2: las opciones de estado del listado y del panel lateral salen de la misma regla, y nunca incluyen un destino que el dominio rechaza.</summary>
public class OpcionesDeEstadoTests
{
    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43)]
    public void Desde_Archivos_u_Of43_no_se_ofrece_Crear_certificado(EstadoCarpeta actual)
    {
        var opciones = Labels.OpcionesDeEstado(actual).ToList();

        Assert.DoesNotContain(EstadoCarpeta.CrearCertificado, opciones);
        Assert.Contains(EstadoCarpeta.SubidaConaset, opciones);
        Assert.Contains(EstadoCarpeta.NoExisteCarpeta, opciones);
        Assert.Contains(actual, opciones);
    }

    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    public void A_una_saliente_nunca_se_le_ofrece_F8_oficio_certificado_ni_creacion_de_F8(EstadoCarpeta actual)
    {
        var opciones = Labels.OpcionesDeEstado(actual, SentidoCambioDomicilio.Saliente).ToList();

        Assert.All(opciones.Where(o => o != actual), o => Assert.True(Carpeta.PermiteForzar(actual, o, SentidoCambioDomicilio.Saliente), $"{actual} -> {o}"));
        Assert.DoesNotContain(EstadoCarpeta.SubidaConF8, opciones);
        Assert.DoesNotContain(EstadoCarpeta.SubidaConOficio, opciones);
        Assert.DoesNotContain(EstadoCarpeta.CrearCertificado, opciones);
        Assert.DoesNotContain(EstadoCarpeta.NoExisteCarpeta, opciones);
        Assert.DoesNotContain(EstadoCarpeta.SubidaConaset, opciones);
        Assert.Contains(EstadoCarpeta.CambioDomSubidoConaset, opciones);
        // Sin dirección conocida se conserva la lista completa de siempre.
        Assert.Contains(EstadoCarpeta.NoExisteCarpeta, Labels.OpcionesDeEstado(actual));
    }

    [Fact]
    public void A_una_entrante_pendiente_solo_se_le_ofrece_quedarse_o_canje()
    {
        var opciones = Labels.OpcionesDeEstado(EstadoCarpeta.CambioDomicilio, SentidoCambioDomicilio.Entrante, pendienteReclasificacion: true).ToList();

        Assert.Equal(
            new[] { EstadoCarpeta.CambioDomicilio, EstadoCarpeta.CanjeLicExtranjera }.OrderBy(e => e),
            opciones.OrderBy(e => e));
        Assert.All(opciones.Where(o => o != EstadoCarpeta.CambioDomicilio), o => Assert.Contains(o, Carpeta.DestinosCorreccionEntrante));
    }

    [Fact]
    public void A_una_saliente_pendiente_no_se_le_ofrecen_los_estados_posteriores_a_la_puerta()
    {
        var opciones = Labels.OpcionesDeEstado(EstadoCarpeta.CambioDomicilio, SentidoCambioDomicilio.Saliente, pendienteReclasificacion: true).ToList();

        Assert.DoesNotContain(opciones, Carpeta.EsPosteriorALaPuertaDeReclasificacion);
        Assert.Contains(EstadoCarpeta.CanjeLicExtranjera, opciones);
    }

    [Theory]
    [InlineData(EstadoCarpeta.SubidaConaset)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.CambioDomicilio)]
    public void Ninguna_opcion_ofrecida_es_rechazada_por_el_dominio(EstadoCarpeta actual)
    {
        foreach (var opcion in Labels.OpcionesDeEstado(actual).Where(o => o != actual))
        {
            Assert.True(Carpeta.PermiteForzar(actual, opcion), $"{actual} -> {opcion}");
            Assert.Equal(Decision.Pendiente, EstadoTransitions.DecisionFor(opcion));
        }
    }
}
