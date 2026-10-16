using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Tests;

public class EstadoCarpetaCatalogTests
{
    [Theory]
    [InlineData("CAMBIO DE DOMICILIO", 1)]
    [InlineData("cambio de domicilio", 1)]
    [InlineData("CAMBIO DE DOMICILIO SOLICITADO", 2)]
    [InlineData("CAMBIO DOM. SUBIDO A CONASET", 3)]
    [InlineData("SUBIDA A CONASET", 3)]
    [InlineData("SUBIDA CON F8", 3)]
    [InlineData("SUBIDA CON OFICIO", 3)]
    [InlineData("CAMBIO DOM. SUBIDO CON CORREO", 3)]
    [InlineData("1° LICENCIA", 0)]
    [InlineData("NO EXISTE CARPETA", 0)]
    [InlineData("", 1)]
    public void Rango_ubica_el_estado_en_su_etapa(string estado, int esperado)
    {
        Assert.Equal(esperado, EstadoCarpetaCatalog.Rango(estado));
    }

    [Fact]
    public void Rango_tolera_tipeos_conocidos_del_libro()
    {
        // variante sin punto que ya maneja Normalizar
        Assert.Equal(3, EstadoCarpetaCatalog.Rango("cambio dom subido a conaset"));
    }

    [Theory]
    [InlineData("SUBIDA A CONASET", true, false)]
    [InlineData("CAMBIO DOM. SUBIDO A CONASET", true, false)]
    [InlineData("SUBIDA CON F8", false, true)]
    [InlineData("SUBIDA CON OFICIO", false, true)]
    [InlineData("CAMBIO DOM. SUBIDO CON CORREO", false, true)]
    [InlineData("CAMBIO DE DOMICILIO SOLICITADO", false, false)]
    public void Distingue_quien_subio_la_carpeta(string estado, bool comuna, bool nosotros)
    {
        Assert.Equal(comuna, EstadoCarpetaCatalog.SubidaPorComuna(estado));
        Assert.Equal(nosotros, EstadoCarpetaCatalog.SubidaPorNosotros(estado));
    }

    [Theory]
    [InlineData("CAMBIO DE DOMICILIO", "CAMBIO DE DOMICILIO SOLICITADO")]
    [InlineData("CAMBIO DE DOMICILIO SOLICITADO", "CAMBIO DE DOMICILIO SOLICITADO")]
    [InlineData("SUBIDA A CONASET", "CAMBIO DOM. SUBIDO A CONASET")]
    [InlineData("CAMBIO DOM. SUBIDO A CONASET", "CAMBIO DOM. SUBIDO A CONASET")]
    [InlineData("SUBIDA CON F8", "CAMBIO DOM. SUBIDO CON CORREO")]
    [InlineData("SUBIDA CON OFICIO", "CAMBIO DOM. SUBIDO CON CORREO")]
    [InlineData("CAMBIO DOM. SUBIDO CON CORREO", "CAMBIO DOM. SUBIDO CON CORREO")]
    public void OpcionDashboard_colapsa_a_tres_situaciones(string estado, string esperado)
    {
        Assert.Equal(esperado, EstadoCarpetaCatalog.OpcionDashboard(estado));
    }
}
