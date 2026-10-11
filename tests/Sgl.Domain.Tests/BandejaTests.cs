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
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado, Bandeja.CambioDomicilio)]
    [InlineData(EstadoCarpeta.SubidaConaset, Bandeja.Subidas)]
    [InlineData(EstadoCarpeta.SubidaConF8, Bandeja.Subidas)]
    [InlineData(EstadoCarpeta.SubidaConOficio, Bandeja.Subidas)]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset, Bandeja.Subidas)]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo, Bandeja.Subidas)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, Bandeja.EnBusqueda)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43, Bandeja.EnBusqueda)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta, Bandeja.EnBusqueda)]
    public void Open_folder_without_caja_is_classified_by_estado(EstadoCarpeta estado, Bandeja esperada)
    {
        Assert.Equal(esperada, Carpeta.ClasificarBandeja(estado, Decision.Pendiente, null, false));
    }

    [Fact]
    public void PendienteBusqueda_wins_over_estado()
    {
        Assert.Equal(Bandeja.EnBusqueda, Carpeta.ClasificarBandeja(EstadoCarpeta.Citada, Decision.Pendiente, null, true));
    }

    [Theory]
    [InlineData(Decision.Otorgado)]
    [InlineData(Decision.Denegado)]
    public void Final_decision_closes_even_if_en_busqueda(Decision decision)
    {
        Assert.Equal(Bandeja.Cerradas, Carpeta.ClasificarBandeja(EstadoCarpeta.SeEncuentraEnArchivos, decision, " ", true));
    }

    [Fact]
    public void Caja_archives_over_everything()
    {
        Assert.Equal(Bandeja.Archivadas, Carpeta.ClasificarBandeja(EstadoCarpeta.SubidaConaset, Decision.Otorgado, "CAJA 12", true));
    }

    [Fact]
    public void Cola_de_caja_is_not_archived_yet()
    {
        Assert.Equal(Bandeja.Subidas, Carpeta.ClasificarBandeja(EstadoCarpeta.SubidaConaset, Decision.Pendiente, Carpeta.ColaDeCaja, false));
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
            var esperado = Carpeta.ClasificarBandeja(c.Estado, c.Decision, c.CajaArchivo, c.PendienteBusqueda);
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
