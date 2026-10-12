using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class BandejaTests
{
    [Theory]
    [InlineData(EstadoCarpeta.Citada, Bandeja.PorPreparar)]
    [InlineData(EstadoCarpeta.PrimeraLicencia, Bandeja.PorPreparar)]
    [InlineData(EstadoCarpeta.CanjeLicExtranjera, Bandeja.PorPreparar)]
    [InlineData(EstadoCarpeta.CrearCertificado, Bandeja.PorPreparar)]
    [InlineData(EstadoCarpeta.Revision, Bandeja.PorPreparar)]
    [InlineData(EstadoCarpeta.CambioDomicilio, Bandeja.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado, Bandeja.CambioDomicilioSolicitado)]
    [InlineData(EstadoCarpeta.SubidaConaset, Bandeja.Subidas)]
    [InlineData(EstadoCarpeta.SubidaConF8, Bandeja.Subidas)]
    [InlineData(EstadoCarpeta.SubidaConOficio, Bandeja.Subidas)]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset, Bandeja.Subidas)]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo, Bandeja.Subidas)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, Bandeja.PorPreparar)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43, Bandeja.PorPreparar)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta, Bandeja.SinCarpetaF8)]
    public void Open_folder_without_caja_is_classified_by_estado(EstadoCarpeta estado, Bandeja esperada)
    {
        Assert.Equal(esperada, Carpeta.ClasificarBandeja(estado, Decision.Pendiente, null));
    }

    [Fact]
    public void PendienteBusqueda_is_only_a_flag_and_keeps_the_folder_in_its_bandeja()
    {
        var c = CarpetaFactory.New();
        c.ForzarEstado(EstadoCarpeta.SeEncuentraEnArchivos, "x", CarpetaFactory.Now);
        c.MarcarPendienteBusqueda(true, "x", CarpetaFactory.Now);
        Assert.Equal(Bandeja.PorPreparar, c.Bandeja);
    }

    [Fact]
    public void Sin_carpeta_f8_wins_over_subidas_but_not_over_cerradas()
    {
        Assert.Equal(Bandeja.SinCarpetaF8, Carpeta.ClasificarBandeja(EstadoCarpeta.NoExisteCarpeta, Decision.Pendiente, Carpeta.ColaDeCaja));
        Assert.Equal(Bandeja.Cerradas, Carpeta.ClasificarBandeja(EstadoCarpeta.NoExisteCarpeta, Decision.Denegado, null));
    }

    [Theory]
    [InlineData(Decision.Otorgado)]
    [InlineData(Decision.Denegado)]
    public void Final_decision_closes_even_if_sin_carpeta(Decision decision)
    {
        Assert.Equal(Bandeja.Cerradas, Carpeta.ClasificarBandeja(EstadoCarpeta.NoExisteCarpeta, decision, " "));
    }

    [Fact]
    public void Caja_archives_over_everything()
    {
        Assert.Equal(Bandeja.Archivadas, Carpeta.ClasificarBandeja(EstadoCarpeta.SubidaConaset, Decision.Otorgado, "CAJA 12"));
    }

    [Fact]
    public void Cola_de_caja_is_not_archived_yet()
    {
        Assert.Equal(Bandeja.Subidas, Carpeta.ClasificarBandeja(EstadoCarpeta.SubidaConaset, Decision.Pendiente, Carpeta.ColaDeCaja));
    }

    [Fact]
    public void Sql_expression_matches_pure_function_for_every_combination()
    {
        var compiled = Carpeta.BandejaExpresion.Compile();
        var cajas = new string?[] { null, "", "CAJA 1", Carpeta.ColaDeCaja };
        foreach (var estado in Enum.GetValues<EstadoCarpeta>())
        foreach (var decision in Enum.GetValues<Decision>())
        foreach (var caja in cajas)
        foreach (var pend in new[] { false, true })
        {
            var c = Carpeta.CrearImportada(Guid.NewGuid(), new Ciudadano(Rut.Parse("12345678-5"), "A", "B"), Sede.Placilla,
                new DateOnly(2026, 1, 1), null, null, estado, decision, null, null, caja, null, CarpetaFactory.Now);
            if (pend && Carpeta.PermiteBusquedaPendiente(estado))
            {
                c.MarcarPendienteBusqueda(true, "x", CarpetaFactory.Now);
            }
            var esperado = Carpeta.ClasificarBandeja(c.Estado, c.Decision, c.CajaArchivo);
            Assert.Equal(esperado, compiled(c));
        }
    }

    [Fact]
    public void Folder_moves_bandeja_automatically_when_estado_changes()
    {
        var c = CarpetaFactory.New();
        Assert.Equal(Bandeja.PorPreparar, c.Bandeja);
        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "u", CarpetaFactory.Now);
        Assert.Equal(Bandeja.Subidas, c.Bandeja);
    }
}
