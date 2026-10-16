using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Tests;

public class DeadlineCalculatorTests
{
    [Fact]
    public void AddBusinessDays_salta_fin_de_semana()
    {
        // viernes 2026-01-02 + 1 hábil = lunes 2026-01-05
        var viernes = new DateOnly(2026, 1, 2);
        Assert.Equal(new DateOnly(2026, 1, 5), DeadlineCalculator.AddBusinessDays(viernes, 1));
    }

    [Fact]
    public void AddBusinessDays_quince_habiles_son_tres_semanas()
    {
        // lunes 2026-01-05 + 15 hábiles = lunes 2026-01-26
        var lunes = new DateOnly(2026, 1, 5);
        Assert.Equal(new DateOnly(2026, 1, 26), DeadlineCalculator.AddBusinessDays(lunes, 15));
    }

    [Fact]
    public void BusinessDaysBetween_no_cuenta_el_inicio_ni_los_findes()
    {
        var lunes = new DateOnly(2026, 1, 5);
        var viernes = new DateOnly(2026, 1, 9);
        Assert.Equal(4, DeadlineCalculator.BusinessDaysBetween(lunes, viernes));
    }

    [Fact]
    public void BusinessDaysBetween_fechas_invertidas_o_iguales_es_cero()
    {
        var d = new DateOnly(2026, 1, 5);
        Assert.Equal(0, DeadlineCalculator.BusinessDaysBetween(d, d));
        Assert.Equal(0, DeadlineCalculator.BusinessDaysBetween(new DateOnly(2026, 1, 9), new DateOnly(2026, 1, 5)));
    }

    [Fact]
    public void PlazoInfo_sin_envio_no_hay_reloj()
    {
        var p = new Peticion { NombreCompleto = "X", Rut = "1-9", Comuna = "Y", EnviadaEn = null };
        var info = PlazoInfo.Para(p);

        Assert.Null(info.Inicio);
        Assert.Null(info.Vence);
        Assert.False(info.Vencido);
        Assert.Equal(15, info.DiasRestantes);
    }

    [Fact]
    public void PlazoInfo_recien_enviada_tiene_quince_dias()
    {
        var p = new Peticion
        {
            NombreCompleto = "X", Rut = "1-9", Comuna = "Y",
            EnviadaEn = DateTimeOffset.Now,
        };
        var info = PlazoInfo.Para(p);

        Assert.NotNull(info.Inicio);
        Assert.NotNull(info.Vence);
        Assert.False(info.Vencido);
        Assert.Equal(0, info.DiasTranscurridos);
        Assert.Equal(15, info.DiasRestantes);
    }

    [Fact]
    public void PlazoInfo_envio_viejo_esta_vencido()
    {
        var p = new Peticion
        {
            NombreCompleto = "X", Rut = "1-9", Comuna = "Y",
            EnviadaEn = DateTimeOffset.Now.AddDays(-40),
        };
        var info = PlazoInfo.Para(p);

        Assert.True(info.Vencido);
        Assert.True(info.DiasRestantes < 0);
    }
}
