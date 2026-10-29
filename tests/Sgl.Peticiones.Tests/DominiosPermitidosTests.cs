using PeticionCambioDomicilio;

namespace Sgl.Peticiones.Tests;

public sealed class DominiosPermitidosTests
{
    [Fact]
    public void Sin_dominios_configurados_no_hay_restriccion()
    {
        var o = new AppOptions();
        Assert.True(o.DestinatarioPermitido("cualquiera@ejemplo.com"));
    }

    [Theory]
    [InlineData("jefa@munivalpo.cl", true)]
    [InlineData("JEFA@MuniValpo.CL", true)]
    [InlineData("jefa@tramites.munivalpo.cl", true)]
    [InlineData("jefa@evilmunivalpo.cl", false)]
    [InlineData("jefa@munivalpo.cl.evil.com", false)]
    [InlineData("jefa@gmail.com", false)]
    [InlineData("no-es-correo", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Con_dominios_solo_pasan_los_permitidos(string? correo, bool esperado)
    {
        var o = new AppOptions { DominiosPermitidos = ["munivalpo.cl"] };
        Assert.Equal(esperado, o.DestinatarioPermitido(correo));
    }

    [Fact]
    public void Acepta_el_dominio_con_arroba_y_espacios()
    {
        var o = new AppOptions { DominiosPermitidos = [" @munivalpo.cl "] };
        Assert.True(o.DestinatarioPermitido("a@munivalpo.cl"));
    }
}
