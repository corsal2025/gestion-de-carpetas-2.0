using Sgl.Application.Estadisticas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

public sealed class EstadisticasCalculoTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    [Fact]
    public void Tiempos_returns_mean_median_max_and_handles_empty()
    {
        var t = EstadisticasCalculo.Tiempos([4, 1, 10, 3]);
        Assert.Equal(4, t.Muestras);
        Assert.Equal(4.5, t.Promedio);
        Assert.Equal(3.5, t.Mediana);
        Assert.Equal(10, t.Maximo);

        var vacio = EstadisticasCalculo.Tiempos([]);
        Assert.Equal(0, vacio.Muestras);
        Assert.Null(vacio.Mediana);
    }

    [Theory]
    [InlineData(EstadoCarpeta.SubidaConaset, "Conaset")]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset, "Conaset")]
    [InlineData(EstadoCarpeta.SubidaConF8, "F8")]
    [InlineData(EstadoCarpeta.SubidaConOficio, "Oficio")]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo, "Correo")]
    [InlineData(EstadoCarpeta.PrimeraLicencia, "Otra")]
    public void ViaDeSubida_maps_estado_to_channel(EstadoCarpeta estado, string via) =>
        Assert.Equal(via, EstadisticasCalculo.ViaDeSubida(estado));

    [Fact]
    public void Busquedas_classifies_found_not_found_and_open_episodes()
    {
        var t0 = new DateTime(2026, 5, 4, 9, 0, 0);
        var marcas = new[]
        {
            new MarcaBusqueda(A, t0, true),
            new MarcaBusqueda(A, t0.AddHours(3), false),               // found after 3h
            new MarcaBusqueda(B, t0, true),
            new MarcaBusqueda(B, t0.AddHours(5), false),               // cleared because NoExiste
            new MarcaBusqueda(C, t0.AddDays(-3), true),                // still open, 3 days
        };
        var noExiste = new[] { new MarcaNoExiste(B, t0.AddHours(5)) };
        var sectores = new Dictionary<Guid, string> { [A] = SectorFisico.Archivo };

        var r = EstadisticasCalculo.AnalizarBusquedas(marcas, noExiste, sectores, ahora: t0.AddHours(6), diasAbierta: 1);

        Assert.True(r.HayDatos);
        Assert.Equal(3, r.Iniciadas);
        Assert.Equal(1, r.Encontradas);
        Assert.Equal(1, r.NoEncontradas);
        Assert.Equal(1, r.Abiertas);
        Assert.Equal(1, r.AbiertasVencidas);
        Assert.Equal(3, r.HorasParaEncontrar.Mediana);
        Assert.Equal(1, r.EncontradasArchivo);
        Assert.Equal(0, r.EncontradasOficina43);
    }

    [Fact]
    public void Busquedas_without_history_reports_no_data()
    {
        var r = EstadisticasCalculo.AnalizarBusquedas([], [], new Dictionary<Guid, string>(), DateTime.Now, 1);
        Assert.False(r.HayDatos);
        Assert.Null(r.HorasParaEncontrar.Promedio);
    }

    [Fact]
    public void TerminosFrecuentes_uppercases_strips_accents_stopwords_and_numbers()
    {
        var top = EstadisticasCalculo.TerminosFrecuentes(
            ["falta certificado de residencia", "Falta certificado médico", "carpeta 12345 en revisión", "FALTA firma"], 3);

        Assert.Equal(new ConteoDto("FALTA", 3), top[0]);
        Assert.Equal(new ConteoDto("CERTIFICADO", 2), top[1]);
        Assert.DoesNotContain(top, t => t.Clave is "DE" or "EN" or "12345");
        Assert.Equal(2, top.Count); // single-occurrence words (possible names) are never shown
    }

    [Theory]
    [InlineData("VALPARAISO", true)]
    [InlineData("15.305.424-K", false)]
    [InlineData("19", false)]
    [InlineData("", false)]
    public void EsEtiquetaSegura_rejects_keys_with_digits(string clave, bool esperado) =>
        Assert.Equal(esperado, EstadisticasCalculo.EsEtiquetaSegura(clave));

    [Fact]
    public void ArchivoPorMes_counts_carpetas_and_distinct_cajas_ignoring_queue()
    {
        var movs = new[]
        {
            new MovimientoCaja(A, new DateTime(2026, 4, 2), null, "CAJA 1"),
            new MovimientoCaja(B, new DateTime(2026, 4, 2), null, "CAJA 1"),
            new MovimientoCaja(C, new DateTime(2026, 5, 1), null, Carpeta.ColaDeCaja),
            new MovimientoCaja(C, new DateTime(2026, 5, 3), Carpeta.ColaDeCaja, "CAJA 2"),
            new MovimientoCaja(A, new DateTime(2026, 5, 4), "CAJA 1", Carpeta.ColaDeCaja),
        };

        var (porMes, reabiertas) = EstadisticasCalculo.ArchivoPorMes(movs);

        Assert.Equal([new ArchivoMesDto(2026, 4, 2, 1), new ArchivoMesDto(2026, 5, 1, 1)], porMes);
        Assert.Equal(1, reabiertas);
    }
}
