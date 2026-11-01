using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>
/// E12: desde 2026-08-01 la carpeta es digital (sin papel que buscar), el RUT original de la importación y la fecha de subida
/// que solo completa. Todo con datos sintéticos.
/// </summary>
public class SectorDigitalTests
{
    private static readonly DateTime At = new(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);

    // E12 criterio 1
    [Theory]
    [InlineData("2026-07-31", SectorFisico.Oficina43)]
    [InlineData("2026-08-01", SectorFisico.Digital)]
    [InlineData("2026-09-15", SectorFisico.Digital)]
    [InlineData("2023-06-30", SectorFisico.Archivo)]
    [InlineData("2023-07-01", SectorFisico.Oficina43)]
    [InlineData("01-08-2026", SectorFisico.Digital)]
    public void Sector_por_fecha_distingue_archivo_oficina_43_y_digital(string fecha, string esperado)
    {
        Assert.Equal(esperado, SectorFisico.Calcular(fecha));
        Assert.Equal(new DateOnly(2026, 8, 1), SectorFisico.CorteDigitalDefecto);
        Assert.Equal(new DateOnly(2023, 7, 1), SectorFisico.CorteDefecto);
    }

    [Fact]
    public void Con_otro_corte_digital_explicito_el_sector_cambia()
    {
        var corteDigital = new DateOnly(2026, 9, 1);
        Assert.Equal(SectorFisico.Oficina43, SectorFisico.Calcular("2026-08-15", SectorFisico.CorteDefecto, corteDigital));
        Assert.Equal(SectorFisico.Digital, SectorFisico.Calcular("2026-09-01", SectorFisico.CorteDefecto, corteDigital));
        Assert.Equal(SectorFisico.Digital, SectorFisico.Calcular("2023-07-01", new DateOnly(2023, 1, 1), new DateOnly(2023, 7, 1)));
        Assert.Equal(SectorFisico.Desconocido, SectorFisico.Calcular("no es fecha", SectorFisico.CorteDefecto, corteDigital));
    }

    [Fact]
    public void Una_fecha_digital_no_tiene_destino_fisico_ni_ofrece_archivos_u_oficina_43()
    {
        Assert.Null(Carpeta.DestinoPorFechaUltimaCarpeta("2026-08-01"));
        Assert.Equal(EstadoCarpeta.SeEncuentraEnOf43, Carpeta.DestinoPorFechaUltimaCarpeta("2026-07-31"));
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, Carpeta.DestinoPorFechaUltimaCarpeta("2023-06-30"));
        Assert.Equal(EstadoCarpeta.SeEncuentraEnOf43, Carpeta.DestinoPorFechaUltimaCarpeta("2026-08-15", SectorFisico.CorteDefecto, new DateOnly(2026, 9, 1)));

        var opciones = Carpeta.OpcionesClasificacionSegunFecha(
            [EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.SeEncuentraEnOf43, EstadoCarpeta.PrimeraLicencia], "2026-08-20");
        Assert.Equal([EstadoCarpeta.PrimeraLicencia], opciones);
    }

    [Fact]
    public void El_corte_digital_en_texto_es_iso()
    {
        Assert.Equal("2026-08-01", SectorFisico.CorteDigitalIso);
    }

    // E12 criterio 3 (dominio): el RUT original queda guardado y no cambia al corregir el RUT
    [Fact]
    public void Una_carpeta_de_la_agenda_guarda_el_rut_original_y_corregir_el_rut_no_lo_cambia()
    {
        var ciudadano = new Ciudadano(Rut.Parse("12345678-5"), "Maria", "Prueba");
        var carpeta = Carpeta.CrearDesdeAgenda(ciudadano, Sede.AvArgentina, new DateOnly(2026, 10, 30), null, null, "imp", At,
            new DateTime(2026, 10, 29, 11, 0, 0));
        Assert.Equal("12345678-5", carpeta.RutImportacion);

        carpeta.ModificarRut(Rut.Parse("11111111-1"), null, "ana", At);

        Assert.Equal("11111111-1", carpeta.Ciudadano.Rut.Value);
        Assert.Equal("12345678-5", carpeta.RutImportacion);
    }

    [Fact]
    public void Una_carpeta_registrada_a_mano_no_tiene_rut_de_importacion()
    {
        Assert.Null(CarpetaFactory.New().RutImportacion);
    }

    // E12 criterio 4 (dominio): la fecha de subida solo se completa si estaba vacía
    [Fact]
    public void Completar_fecha_de_subida_solo_si_esta_vacia_y_deja_historial()
    {
        var carpeta = CarpetaFactory.New();

        Assert.True(carpeta.CompletarFechaSubida(new DateOnly(2026, 3, 20), "importador", At));
        Assert.Equal(new DateOnly(2026, 3, 20), carpeta.FechaSubida);
        var h = carpeta.Historial.Last();
        Assert.Equal("FechaSubida", h.Campo);
        Assert.Null(h.Anterior);
        Assert.Equal("2026-03-20", h.Nuevo);

        var entradas = carpeta.Historial.Count;
        Assert.False(carpeta.CompletarFechaSubida(new DateOnly(2026, 3, 25), "importador", At));
        Assert.Equal(new DateOnly(2026, 3, 20), carpeta.FechaSubida);
        Assert.Equal(entradas, carpeta.Historial.Count);
    }

    // E12 criterio 5 (dominio): nombre, RUT y comuna dejan historial con el valor anterior; DV inválido se rechaza
    [Fact]
    public void Nombre_rut_y_comuna_dejan_historial_con_el_valor_anterior()
    {
        var carpeta = CarpetaFactory.InEstado(EstadoCarpeta.CambioDomicilio);

        carpeta.ModificarNombre("pedro prueba soto", "ana", At);
        carpeta.ModificarRut(Rut.Parse("11111111-1"), null, "ana", At);
        carpeta.AsignarComuna("Quilpué", "ana", At);
        carpeta.AsignarComuna("Viña del Mar", "ana", At);

        Assert.Contains(carpeta.Historial, h => h is { Campo: "Nombre", Anterior: "JUAN PÉREZ", Nuevo: "PEDRO PRUEBA SOTO" });
        Assert.Contains(carpeta.Historial, h => h is { Campo: "RUT", Anterior: "12.345.678-5", Nuevo: "11.111.111-1" });
        Assert.Contains(carpeta.Historial, h => h is { Campo: "Comuna", Anterior: null });
        Assert.Contains(carpeta.Historial, h => h is { Campo: "Comuna", Anterior: "QUILPUÉ", Nuevo: "VIÑA DEL MAR" });
    }

    [Theory]
    [InlineData("12.345.678-9")]
    [InlineData("12345678")]
    [InlineData("abc")]
    public void Un_rut_con_digito_verificador_invalido_se_rechaza(string texto)
    {
        Assert.Throws<DomainException>(() => Rut.Parse(texto));
    }
}
