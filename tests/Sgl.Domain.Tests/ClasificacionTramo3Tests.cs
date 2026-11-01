using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>Tramo 3 (clasificación): sin atajo Archivos/Of. 43 -> Crear certificado, corte de sector configurable, comuna obligatoria.</summary>
public class ClasificacionTramo3Tests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static Carpeta PorClasificar() =>
        Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ SOTO"),
            Sede.AvArgentina, new DateOnly(2026, 10, 13), new TimeOnly(9, 30), TramiteAgenda.Renovacion, "importador", Now);

    // ---- Criterio 1 ----

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43)]
    public void Desde_Archivos_u_Of43_no_se_puede_pasar_directo_a_CrearCertificado(EstadoCarpeta origen)
    {
        Assert.False(EstadoTransitions.CanTransition(origen, EstadoCarpeta.CrearCertificado));
        Assert.True(EstadoTransitions.CanTransition(origen, EstadoCarpeta.NoExisteCarpeta));
    }

    [Fact]
    public void NoExisteCarpeta_si_puede_pasar_a_CrearCertificado()
    {
        Assert.True(EstadoTransitions.CanTransition(EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.CrearCertificado));
    }

    // ---- Criterio 2 ----

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43)]
    public void ForzarEstado_de_Archivos_u_Of43_a_CrearCertificado_lanza_y_no_cambia(EstadoCarpeta origen)
    {
        var c = PorClasificar();
        c.ForzarEstado(origen, "ana", Now);

        Assert.Throws<DomainException>(() => c.ForzarEstado(EstadoCarpeta.CrearCertificado, "ana", Now));

        Assert.Equal(origen, c.Estado);
    }

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.SubidaConaset)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43, EstadoCarpeta.PrimeraLicencia)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.PorClasificar, EstadoCarpeta.CrearCertificado)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta, EstadoCarpeta.CrearCertificado)]
    [InlineData(EstadoCarpeta.CambioDomicilio, EstadoCarpeta.CrearCertificado)]
    public void ForzarEstado_las_demas_correcciones_manuales_siguen_libres(EstadoCarpeta origen, EstadoCarpeta destino)
    {
        var c = PorClasificar();
        c.ForzarEstado(origen, "ana", Now, SentidoCambioDomicilio.Entrante);

        c.ForzarEstado(destino, "ana", Now);

        Assert.Equal(destino, c.Estado);
    }

    [Fact]
    public void Camino_Archivos_NoExiste_CrearCertificado_funciona_en_el_dominio()
    {
        var c = PorClasificar();
        c.Clasificar(EstadoCarpeta.SeEncuentraEnArchivos, ClaseLicencia.B, "2020-01-01", null, "Funcionaria Prueba", Now);

        c.CambiarEstado(EstadoCarpeta.NoExisteCarpeta, "Funcionaria Prueba", Now);
        c.CambiarEstado(EstadoCarpeta.CrearCertificado, "Funcionaria Prueba", Now);

        Assert.Equal(EstadoCarpeta.CrearCertificado, c.Estado);
    }

    // ---- Criterio 3 ----

    [Theory]
    [InlineData("30-06-2023", SectorFisico.Archivo)]
    [InlineData("01-07-2023", SectorFisico.Oficina43)]
    public void Corte_por_defecto_2023_07_01(string fecha, string esperado)
    {
        Assert.Equal(esperado, SectorFisico.Calcular(fecha, SectorFisico.CorteDefecto));
        Assert.Equal(new DateOnly(2023, 7, 1), SectorFisico.CorteDefecto);
    }

    [Theory]
    [InlineData("31-12-2023", SectorFisico.Archivo)]
    [InlineData("01-01-2024", SectorFisico.Oficina43)]
    [InlineData("01-07-2023", SectorFisico.Archivo)] // con el corte por defecto era Of. 43
    public void Un_corte_explicito_cambia_el_resultado(string fecha, string esperado)
    {
        Assert.Equal(esperado, SectorFisico.Calcular(fecha, new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public void Destino_por_fecha_acepta_corte_explicito()
    {
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, Carpeta.DestinoPorFechaUltimaCarpeta("2023-12-31", new DateOnly(2024, 1, 1)));
        Assert.Equal(EstadoCarpeta.SeEncuentraEnOf43, Carpeta.DestinoPorFechaUltimaCarpeta("2024-01-01", new DateOnly(2024, 1, 1)));
        Assert.Null(Carpeta.DestinoPorFechaUltimaCarpeta("no es fecha", new DateOnly(2024, 1, 1)));
    }

    // ---- Criterio 4 (dominio): comuna obligatoria ----

    [Theory]
    [InlineData(EstadoCarpeta.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado)]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset)]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo)]
    public void Cambio_de_domicilio_sin_comuna_se_rechaza(EstadoCarpeta destino)
    {
        var c = PorClasificar();

        Assert.Throws<DomainException>(() => c.Clasificar(destino, ClaseLicencia.B, null, null, "Funcionaria Prueba", Now));
        Assert.Throws<DomainException>(() => c.Clasificar(destino, ClaseLicencia.B, null, "  ", "Funcionaria Prueba", Now));

        Assert.Equal(EstadoCarpeta.PorClasificar, c.Estado);
    }

    [Fact]
    public void Cambio_de_domicilio_con_comuna_valida_clasifica()
    {
        var c = PorClasificar();

        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, "VIÑA DEL MAR", "Funcionaria Prueba", Now);

        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.Equal("VIÑA DEL MAR", c.Comuna);
    }

    [Theory]
    [InlineData(EstadoCarpeta.PrimeraLicencia)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.CanjeLicExtranjera)]
    public void Los_demas_destinos_no_piden_comuna(EstadoCarpeta destino)
    {
        var c = PorClasificar();

        c.Clasificar(destino, ClaseLicencia.B, null, null, "Funcionaria Prueba", Now);

        Assert.Equal(destino, c.Estado);
    }
}
