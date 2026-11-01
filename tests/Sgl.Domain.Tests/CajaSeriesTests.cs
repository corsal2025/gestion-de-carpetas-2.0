using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>Tramo 7: cajas en tres series (normal, cambio de domicilio «-CD», penúltima carpeta «-PUC»). Datos sintéticos.</summary>
public class CajaSeriesTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static Carpeta Cerrada(EstadoCarpeta estado, Decision decision = Decision.Otorgado)
    {
        var c = CarpetaFactory.InEstado(estado);
        c.CambiarDecision(decision, "setup", Now);
        return c;
    }

    // Criterio 1
    [Theory]
    [InlineData("A-12", "A-12")]
    [InlineData("a 12", "A-12")]
    [InlineData("  ab-1234 ", "AB-1234")]
    [InlineData("a12", "A-12")]
    [InlineData("A-12-CD", "A-12-CD")]
    [InlineData("a 12 cd", "A-12-CD")]
    [InlineData("A12puc", "A-12-PUC")]
    public void Formatos_por_defecto_aceptan_y_normalizan_el_rotulo(string escrito, string esperado)
    {
        Assert.Equal(esperado, Carpeta.NormalizarRotulo(escrito));
    }

    [Theory]
    [InlineData("A-12345")]
    [InlineData("ABC-1")]
    [InlineData("CAJA AA-48")]
    [InlineData("12")]
    [InlineData("A-")]
    [InlineData("A-12-XX")]
    [InlineData("-CD")]
    [InlineData("")]
    [InlineData(null)]
    public void Formato_invalido_no_se_normaliza_y_se_rechaza_al_asignar(string? escrito)
    {
        Assert.Null(Carpeta.NormalizarRotulo(escrito));
        if (string.IsNullOrWhiteSpace(escrito)) return;

        var c = Cerrada(EstadoCarpeta.SubidaConaset);
        var ex = Assert.Throws<DomainException>(() => c.AsignarCaja(escrito, "Funcionaria Prueba", Now));
        Assert.Contains("formato", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(c.CajaArchivo);
    }

    [Fact]
    public void Asignar_guarda_el_rotulo_normalizado_y_deja_historial()
    {
        var c = Cerrada(EstadoCarpeta.SubidaConaset);

        c.AsignarCaja("a 12", "Funcionaria Prueba", Now);

        Assert.Equal("A-12", c.CajaArchivo);
        Assert.Contains(c.Historial, h => h.Campo == nameof(Carpeta.CajaArchivo) && h.Nuevo == "A-12");
    }

    [Fact]
    public void Los_formatos_se_pueden_configurar()
    {
        var f = new CajaFormatos(@"^[A-Z]-\d{1,2}$", "-DOM", "-PEN");
        var c = Cerrada(EstadoCarpeta.CambioDomSubidoConaset);

        Assert.Equal("B-7-DOM", Carpeta.NormalizarRotulo("b 7 dom", f));
        Assert.Null(Carpeta.NormalizarRotulo("B-777-DOM", f));
        Assert.Equal(SerieCaja.CambioDomicilio, Carpeta.SerieDeRotulo("B-7-DOM", f));
        c.AsignarCaja("b 7 dom", "Funcionaria Prueba", Now, f);
        Assert.Equal("B-7-DOM", c.CajaArchivo);
    }

    // Criterio 2
    [Theory]
    [InlineData(EstadoCarpeta.SubidaConF8, SerieCaja.PenultimaCarpeta)]
    [InlineData(EstadoCarpeta.CambioDomicilio, SerieCaja.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado, SerieCaja.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset, SerieCaja.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo, SerieCaja.CambioDomicilio)]
    [InlineData(EstadoCarpeta.SubidaConaset, SerieCaja.Normal)]
    [InlineData(EstadoCarpeta.SubidaConOficio, SerieCaja.Normal)]
    [InlineData(EstadoCarpeta.PrimeraLicencia, SerieCaja.Normal)]
    [InlineData(EstadoCarpeta.CanjeLicExtranjera, SerieCaja.Normal)]
    public void La_serie_sale_del_estado(EstadoCarpeta estado, SerieCaja esperada)
    {
        Assert.Equal(esperada, Carpeta.SerieDe(estado));
    }

    [Theory]
    [InlineData("A-12", SerieCaja.Normal)]
    [InlineData("A-12-CD", SerieCaja.CambioDomicilio)]
    [InlineData("A-12-PUC", SerieCaja.PenultimaCarpeta)]
    public void La_serie_del_rotulo_sale_del_sufijo(string rotulo, SerieCaja esperada)
    {
        Assert.Equal(esperada, Carpeta.SerieDeRotulo(rotulo));
    }

    [Fact]
    public void Una_serie_distinta_a_la_de_la_carpeta_se_rechaza()
    {
        var cambioDom = Cerrada(EstadoCarpeta.CambioDomSubidoCorreo);
        var penultima = Cerrada(EstadoCarpeta.SubidaConF8);
        var normal = Cerrada(EstadoCarpeta.SubidaConaset);

        Assert.Throws<DomainException>(() => cambioDom.AsignarCaja("A-12", "Funcionaria Prueba", Now));
        Assert.Throws<DomainException>(() => cambioDom.AsignarCaja("A-12-PUC", "Funcionaria Prueba", Now));
        Assert.Throws<DomainException>(() => penultima.AsignarCaja("A-12", "Funcionaria Prueba", Now));
        Assert.Throws<DomainException>(() => penultima.AsignarCaja("A-12-CD", "Funcionaria Prueba", Now));
        Assert.Throws<DomainException>(() => normal.AsignarCaja("A-12-CD", "Funcionaria Prueba", Now));
        Assert.Throws<DomainException>(() => normal.AsignarCaja("A-12-PUC", "Funcionaria Prueba", Now));

        cambioDom.AsignarCaja("A-12-CD", "Funcionaria Prueba", Now);
        penultima.AsignarCaja("A-12-PUC", "Funcionaria Prueba", Now);
        normal.AsignarCaja("A-12", "Funcionaria Prueba", Now);
        Assert.Equal("A-12-CD", cambioDom.CajaArchivo);
        Assert.Equal("A-12-PUC", penultima.CajaArchivo);
        Assert.Equal("A-12", normal.CajaArchivo);
    }

    // Criterio 3
    [Fact]
    public void Sin_decision_final_no_recibe_caja_ni_entra_a_la_cola_pero_se_puede_quitar()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SubidaConaset);

        var ex = Assert.Throws<DomainException>(() => c.AsignarCaja("A-12", "Funcionaria Prueba", Now));
        Assert.Contains("decisión", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<DomainException>(() => c.AsignarCaja(Carpeta.ColaDeCaja, "Funcionaria Prueba", Now));
        Assert.Null(c.CajaArchivo);

        // Una carpeta heredada (importada) con caja antigua y sin decisión: quitarla sigue permitido.
        var heredada = Carpeta.CrearImportada(Guid.NewGuid(), new Ciudadano(Rut.Parse("12345678-5"), "Ana", "Prueba"), Sede.AvArgentina,
            new DateOnly(2026, 3, 1), null, null, EstadoCarpeta.SubidaConaset, Decision.Pendiente, null, null, "CAJA 7", null, Now);
        heredada.AsignarCaja(null, "Funcionaria Prueba", Now);
        Assert.Null(heredada.CajaArchivo);
    }

    [Theory]
    [InlineData(Decision.Otorgado)]
    [InlineData(Decision.Denegado)]
    public void Con_decision_final_entra_a_la_cola_de_caja(Decision decision)
    {
        var c = Cerrada(EstadoCarpeta.SubidaConaset, decision);

        c.AsignarCaja(Carpeta.ColaDeCaja, "Funcionaria Prueba", Now);

        Assert.Equal(Carpeta.ColaDeCaja, c.CajaArchivo);
    }

    [Fact]
    public void Reabrir_devuelve_a_la_cola_incluso_una_caja_antigua_sin_decision()
    {
        var heredada = Carpeta.CrearImportada(Guid.NewGuid(), new Ciudadano(Rut.Parse("12345678-5"), "Ana", "Prueba"), Sede.AvArgentina,
            new DateOnly(2026, 3, 1), null, null, EstadoCarpeta.SubidaConaset, Decision.Pendiente, null, null, "CAJA 7", null, Now);

        heredada.DevolverACola("Funcionaria Prueba", Now);

        Assert.Equal(Carpeta.ColaDeCaja, heredada.CajaArchivo);
    }
}
