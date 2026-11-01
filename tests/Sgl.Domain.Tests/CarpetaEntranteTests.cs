using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>E13: «Nos piden carpetas». Reglas del caso entrante: sector por fecha y las tres salidas cuando la carpeta no se encuentra.</summary>
public class CarpetaEntranteTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private static Carpeta Entrante(string? fecha = null)
    {
        var c = Carpeta.RegistrarDesdeCorreo(
            new Ciudadano(Rut.Parse("12345678-5"), "ROSA", "MELENDEZ ARAYA"), Sede.AvArgentina,
            new DateOnly(2026, 10, 29), "Viña del Mar", false, "Sistema · correo", Now);
        c.ConfirmarReclasificacion("ana", Now);
        if (fecha is not null)
        {
            c.ClasificarEntrante(fecha, "ana", Now);
        }
        return c;
    }

    private static Carpeta Saliente()
    {
        var c = Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse("12345678-5"), "ROSA", "MELENDEZ ARAYA"), Sede.AvArgentina,
            new DateOnly(2026, 10, 30), new TimeOnly(9, 30), TramiteAgenda.Renovacion, "importador", Now);
        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, "Viña del Mar", "javiera", Now);
        c.ConfirmarReclasificacion("javiera", Now);
        return c;
    }

    [Theory]
    [InlineData("2020-05-01", SectorFisico.Archivo)]
    [InlineData("2023-06-30", SectorFisico.Archivo)]
    [InlineData("2023-07-01", SectorFisico.Oficina43)]
    [InlineData("2024-01-10", SectorFisico.Oficina43)]
    [InlineData("2026-08-01", SectorFisico.Digital)]
    [InlineData("2026-09-01", SectorFisico.Digital)]
    public void Clasificar_guarda_la_fecha_ISO_y_el_sector_sale_de_ella(string fecha, string sector)
    {
        var c = Entrante();
        Assert.Equal(SectorFisico.Desconocido, c.SectorPorFecha);

        c.ClasificarEntrante(fecha, "ana", Now);

        Assert.Equal(fecha, c.FechaUltimaCarpeta);
        Assert.Equal(sector, c.SectorPorFecha);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es fecha")]
    [InlineData("2099-01-01")]
    public void Clasificar_rechaza_una_fecha_ausente_invalida_o_futura(string? fecha)
    {
        var c = Entrante();

        Assert.Throws<DomainException>(() => c.ClasificarEntrante(fecha, "ana", Now));
        Assert.Null(c.FechaUltimaCarpeta);
    }

    [Fact]
    public void Solo_se_trabaja_una_entrante_abierta_en_cambio_de_domicilio()
    {
        var saliente = Saliente();
        Assert.Throws<DomainException>(() => saliente.ClasificarEntrante("2024-01-10", "ana", Now));
        Assert.Throws<DomainException>(() => saliente.MarcarSubidaEntrante("ana", Now));
        Assert.Throws<DomainException>(() => saliente.MarcarNoEncontradaEntrante("ana", Now));
        Assert.Throws<DomainException>(() => saliente.CerrarEntranteSinRegistro(null, "ana", Now));
        Assert.Throws<DomainException>(() => saliente.CerrarEntranteCarpetaDigital("ana", Now));

        var cerrada = Entrante("2024-01-10");
        cerrada.MarcarSubidaEntrante("ana", Now);
        Assert.Throws<DomainException>(() => cerrada.ClasificarEntrante("2020-01-01", "ana", Now));
        Assert.Throws<DomainException>(() => cerrada.MarcarNoEncontradaEntrante("ana", Now));
    }

    // Criterio 2: la subida de una entrante es «Cambio dom. subido a Conaset», nunca «Subida a Conaset».
    [Theory]
    [InlineData("2020-05-01")]
    [InlineData("2024-01-10")]
    public void Subida_de_una_entrante_da_CambioDomSubidoConaset_con_la_fecha_de_hoy(string fecha)
    {
        var c = Entrante(fecha);

        c.MarcarSubidaEntrante("ana", Now);

        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, c.Estado);
        Assert.Equal(new DateOnly(2026, 10, 30), c.FechaSubida);
        Assert.Equal(SentidoCambioDomicilio.Entrante, c.SentidoCambioDomicilio);
    }

    [Fact]
    public void No_se_sube_sin_fecha_ni_una_digital_porque_no_hay_carpeta_fisica()
    {
        Assert.Throws<DomainException>(() => Entrante().MarcarSubidaEntrante("ana", Now));
        Assert.Throws<DomainException>(() => Entrante("2026-09-01").MarcarSubidaEntrante("ana", Now));
    }

    // Salida A: la persona está en nuestra base (tiene fecha) pero la carpeta física no aparece.
    [Fact]
    public void Salida_A_pasa_a_NoExisteCarpeta_solo_con_fecha_fisica()
    {
        var c = Entrante("2024-01-10");
        c.AvanzarLupa("ana", Now);
        Assert.True(c.PendienteBusqueda);

        c.MarcarNoEncontradaEntrante("ana", Now);

        Assert.Equal(EstadoCarpeta.NoExisteCarpeta, c.Estado);
        Assert.Equal(SentidoCambioDomicilio.Entrante, c.SentidoCambioDomicilio);

        Assert.Throws<DomainException>(() => Entrante().MarcarNoEncontradaEntrante("ana", Now));
        Assert.Throws<DomainException>(() => Entrante("2026-09-01").MarcarNoEncontradaEntrante("ana", Now));
    }

    // Salida B: sin registro en nuestra base. No aplica a una digital (esa solo recibe la plantilla C).
    [Theory]
    [InlineData(null)]
    [InlineData("2020-05-01")]
    [InlineData("2024-01-10")]
    public void Salida_B_cierra_como_sin_registro_y_deja_la_comuna_consultada_en_el_historial(string? fecha)
    {
        var c = Entrante(fecha);

        c.CerrarEntranteSinRegistro("Quilpué", "ana", Now);

        Assert.Equal(EstadoCarpeta.CambioDomSinRegistro, c.Estado);
        Assert.Equal(new DateOnly(2026, 10, 30), c.FechaCierre);
        Assert.Contains(c.Historial, h => h.Nuevo is not null && h.Nuevo.Contains("Quilpué", StringComparison.Ordinal));
    }

    [Fact]
    public void Salida_B_no_se_ofrece_a_una_digital()
    {
        var c = Entrante("2026-09-01");

        Assert.Throws<DomainException>(() => c.CerrarEntranteSinRegistro(null, "ana", Now));
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
    }

    // Salida C: la última carpeta es digital (fecha >= 2026-08-01).
    [Fact]
    public void Salida_C_cierra_como_carpeta_digital_solo_con_fecha_digital()
    {
        var c = Entrante("2026-09-01");

        c.CerrarEntranteCarpetaDigital("ana", Now);

        Assert.Equal(EstadoCarpeta.CambioDomCarpetaDigital, c.Estado);
        Assert.Equal(new DateOnly(2026, 10, 30), c.FechaCierre);

        Assert.Throws<DomainException>(() => Entrante("2024-01-10").CerrarEntranteCarpetaDigital("ana", Now));
        Assert.Throws<DomainException>(() => Entrante().CerrarEntranteCarpetaDigital("ana", Now));
    }

    [Fact]
    public void La_lupa_de_una_entrante_solo_aplica_a_una_carpeta_fisica_y_cicla_sus_tres_posiciones()
    {
        var c = Entrante("2024-01-10");

        c.AvanzarLupaEntrante("ana", Now);
        Assert.True(c.PendienteBusqueda);
        c.AvanzarLupaEntrante("ana", Now);
        Assert.True(c.CarpetaEncontrada);
        c.AvanzarLupaEntrante("ana", Now);
        Assert.False(c.PendienteBusqueda || c.CarpetaEncontrada);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);

        Assert.Throws<DomainException>(() => Entrante().AvanzarLupaEntrante("ana", Now));
        Assert.Throws<DomainException>(() => Entrante("2026-09-01").AvanzarLupaEntrante("ana", Now));
        Assert.Throws<DomainException>(() => Saliente().AvanzarLupaEntrante("ana", Now));
    }
}
