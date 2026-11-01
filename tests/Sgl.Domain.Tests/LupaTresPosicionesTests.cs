using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>Tramo 5: la lupa tiene 3 posiciones (sin marcar, en búsqueda, encontrada) y «Subida a Conaset» cierra la búsqueda.</summary>
public class LupaTresPosicionesTests
{
    private static readonly DateTime T1 = new(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = T1.AddMinutes(5);
    private static readonly DateTime T3 = T1.AddMinutes(10);

    private static Carpeta EnArchivos() => CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);

    [Fact]
    public void Nueva_carpeta_no_esta_encontrada()
    {
        Assert.False(CarpetaFactory.New().CarpetaEncontrada);
    }

    [Fact]
    public void AvanzarLupa_cicla_sin_marcar_en_busqueda_encontrada_sin_marcar_y_nunca_deja_ambas_marcas()
    {
        var c = EnArchivos();

        c.AvanzarLupa("ana", T1);
        Assert.True(c.PendienteBusqueda);
        Assert.False(c.CarpetaEncontrada);

        c.AvanzarLupa("ana", T2);
        Assert.False(c.PendienteBusqueda);
        Assert.True(c.CarpetaEncontrada);

        c.AvanzarLupa("ana", T3);
        Assert.False(c.PendienteBusqueda);
        Assert.False(c.CarpetaEncontrada);
    }

    [Fact]
    public void Cada_toque_de_la_lupa_deja_linea_en_el_historial_con_usuario_y_hora()
    {
        var c = EnArchivos();
        var antes = c.Historial.Count;

        c.AvanzarLupa("ana", T1);
        c.AvanzarLupa("luis", T2);
        c.AvanzarLupa("marta", T3);

        var nuevas = c.Historial.Skip(antes).ToList();
        Assert.Contains(nuevas, h => h.Campo == "PendienteBusqueda" && h.Nuevo == "Si" && h.Usuario == "ana" && h.Fecha == T1);
        Assert.Contains(nuevas, h => h.Campo == "PendienteBusqueda" && h.Nuevo == "No" && h.Usuario == "luis" && h.Fecha == T2);
        Assert.Contains(nuevas, h => h.Campo == "CarpetaEncontrada" && h.Nuevo == "Si" && h.Usuario == "luis" && h.Fecha == T2);
        Assert.Contains(nuevas, h => h.Campo == "CarpetaEncontrada" && h.Nuevo == "No" && h.Usuario == "marta" && h.Fecha == T3);
    }

    [Fact]
    public void AvanzarLupa_fuera_de_los_estados_permitidos_lanza_y_no_cambia_nada()
    {
        var c = CarpetaFactory.New();

        Assert.Throws<DomainException>(() => c.AvanzarLupa("ana", T1));
        Assert.False(c.PendienteBusqueda);
        Assert.False(c.CarpetaEncontrada);
    }

    [Fact]
    public void AvanzarLupa_exige_autor()
    {
        Assert.ThrowsAny<DomainException>(() => EnArchivos().AvanzarLupa(" ", T1));
    }

    [Fact]
    public void Encontrada_se_conserva_entre_archivos_y_of43_y_se_limpia_al_salir_de_los_estados_permitidos()
    {
        var c = EnArchivos();
        c.AvanzarLupa("ana", T1);
        c.AvanzarLupa("ana", T2);

        c.ForzarEstado(EstadoCarpeta.SeEncuentraEnOf43, "ana", T2);
        Assert.True(c.CarpetaEncontrada);

        c.ForzarEstado(EstadoCarpeta.Citada, "ana", T3);
        Assert.False(c.CarpetaEncontrada);
        Assert.False(c.PendienteBusqueda);
        Assert.Contains(c.Historial, h => h.Campo == "CarpetaEncontrada" && h.Nuevo == "No" && h.Fecha == T3);
    }

    [Fact]
    public void En_busqueda_tambien_se_limpia_al_salir_de_los_estados_permitidos()
    {
        var c = EnArchivos();
        c.AvanzarLupa("ana", T1);

        c.ForzarEstado(EstadoCarpeta.Citada, "ana", T2);

        Assert.False(c.PendienteBusqueda);
        Assert.False(c.CarpetaEncontrada);
    }

    [Fact]
    public void MarcarPendienteBusqueda_true_sobre_encontrada_pasa_a_en_busqueda_sin_ambas_marcas()
    {
        var c = EnArchivos();
        c.AvanzarLupa("ana", T1);
        c.AvanzarLupa("ana", T2);

        c.MarcarPendienteBusqueda(true, "ana", T3);

        Assert.True(c.PendienteBusqueda);
        Assert.False(c.CarpetaEncontrada);
    }

    [Fact]
    public void MarcarPendienteBusqueda_false_limpia_tambien_encontrada()
    {
        var c = EnArchivos();
        c.AvanzarLupa("ana", T1);
        c.AvanzarLupa("ana", T2);

        c.MarcarPendienteBusqueda(false, "ana", T3);

        Assert.False(c.PendienteBusqueda);
        Assert.False(c.CarpetaEncontrada);
    }

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43)]
    public void MarcarSubidaConaset_pasa_a_SubidaConaset_estampa_fecha_de_hoy_y_borra_la_lupa(EstadoCarpeta origen)
    {
        var c = CarpetaFactory.InEstado(origen);
        c.AvanzarLupa("ana", T1);
        c.AvanzarLupa("ana", T2);

        c.MarcarSubidaConaset("Funcionaria Prueba", T3);

        Assert.Equal(EstadoCarpeta.SubidaConaset, c.Estado);
        Assert.Equal(HoraChile.Fecha(T3), c.FechaSubida);
        Assert.False(c.PendienteBusqueda);
        Assert.False(c.CarpetaEncontrada);
    }

    [Fact]
    public void MarcarSubidaConaset_desde_otro_estado_lanza()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.NoExisteCarpeta);

        Assert.Throws<DomainException>(() => c.MarcarSubidaConaset("ana", T1));
        Assert.Equal(EstadoCarpeta.NoExisteCarpeta, c.Estado);
    }

    [Fact]
    public void Deshacer_la_subida_restaura_estado_fecha_y_la_posicion_de_la_lupa()
    {
        var c = EnArchivos();
        c.AvanzarLupa("ana", T1);
        c.AvanzarLupa("ana", T2);
        var fechaAntes = c.FechaSubida;
        var punto = c.PuntoRestauracion();
        c.MarcarSubidaConaset("Funcionaria Prueba", T3);

        c.RestaurarEstado(punto, EstadoCarpeta.SubidaConaset, "Funcionaria Prueba", T3.AddMinutes(1));

        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, c.Estado);
        Assert.Equal(fechaAntes, c.FechaSubida);
        Assert.True(c.CarpetaEncontrada);
        Assert.False(c.PendienteBusqueda);
    }
}
