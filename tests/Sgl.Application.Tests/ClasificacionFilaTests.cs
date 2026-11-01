using Sgl.Domain;
using Sgl.Web.Services;

namespace Sgl.Application.Tests;

public sealed class ClasificacionFilaTests
{
    [Fact]
    public void Primera_vez_extension_sin_tocar_la_fecha_precarga_Primera_Licencia()
    {
        Assert.Equal(EstadoCarpeta.PrimeraLicencia,
            ClasificacionFila.DestinoElegido(false, null, TramiteAgenda.PrimeraVezExtension, false, null));
    }

    [Fact]
    public void Una_fecha_digital_tecleada_conserva_el_destino_precargado()
    {
        var digital = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        Assert.Null(Carpeta.DestinoPorFechaUltimaCarpeta(digital));
        Assert.Equal(EstadoCarpeta.PrimeraLicencia,
            ClasificacionFila.DestinoElegido(false, null, TramiteAgenda.PrimeraVezExtension, true, digital));
    }

    [Fact]
    public void Una_fecha_fisica_tecleada_cede_el_destino_a_la_fecha()
    {
        var antigua = "2020-01-15";
        Assert.NotNull(Carpeta.DestinoPorFechaUltimaCarpeta(antigua));
        Assert.Null(ClasificacionFila.DestinoElegido(false, null, TramiteAgenda.PrimeraVezExtension, true, antigua));
    }

    [Fact]
    public void Una_eleccion_explicita_siempre_gana_y_otro_tramite_no_precarga()
    {
        Assert.Equal(EstadoCarpeta.SubidaConaset,
            ClasificacionFila.DestinoElegido(true, EstadoCarpeta.SubidaConaset, TramiteAgenda.PrimeraVezExtension, true, "2020-01-15"));
        Assert.Null(ClasificacionFila.DestinoElegido(true, null, TramiteAgenda.PrimeraVezExtension, false, null));
        Assert.Null(ClasificacionFila.DestinoElegido(false, null, TramiteAgenda.Renovacion, false, null));
    }
}
