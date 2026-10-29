using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class BandejaTests
{
    [Theory]
    [InlineData(EstadoCarpeta.PrimeraLicencia, Bandeja.PorPreparar)]
    [InlineData(EstadoCarpeta.CanjeLicExtranjera, Bandeja.CanjeLicExtranjera)]
    [InlineData(EstadoCarpeta.CrearCertificado, Bandeja.CrearCertificado)]
    [InlineData(EstadoCarpeta.Revision, Bandeja.PorPreparar)]
    [InlineData(EstadoCarpeta.CambioDomicilio, Bandeja.CambioDomicilio)]
    [InlineData(EstadoCarpeta.CambioDomicilioSolicitado, Bandeja.CambioDomicilioSolicitado)]
    [InlineData(EstadoCarpeta.SubidaConaset, Bandeja.SubidaConaset)]
    [InlineData(EstadoCarpeta.SubidaConF8, Bandeja.SubidaConF8)]
    [InlineData(EstadoCarpeta.SubidaConOficio, Bandeja.SubidaConOficio)]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset, Bandeja.CambioDomSubidoConaset)]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo, Bandeja.CambioDomSubidoCorreo)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, Bandeja.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43, Bandeja.SeEncuentraEnOf43)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta, Bandeja.SinCarpetaF8)]
    [InlineData(EstadoCarpeta.Citada, Bandeja.ParaRevision)]
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
        Assert.Equal(Bandeja.SeEncuentraEnArchivos, c.Bandeja);
    }

    [Fact]
    public void Each_action_estado_has_its_own_bandeja_with_the_same_name()
    {
        EstadoCarpeta[] propios =
        [
            EstadoCarpeta.SubidaConaset, EstadoCarpeta.SubidaConF8, EstadoCarpeta.SubidaConOficio,
            EstadoCarpeta.CambioDomSubidoConaset, EstadoCarpeta.CambioDomSubidoCorreo,
            EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.SeEncuentraEnOf43,
            EstadoCarpeta.CrearCertificado, EstadoCarpeta.CanjeLicExtranjera,
            EstadoCarpeta.CambioDomicilio, EstadoCarpeta.CambioDomicilioSolicitado,
        ];
        foreach (var estado in propios)
        {
            Assert.Equal(estado.ToString(), Carpeta.ClasificarBandeja(estado, Decision.Pendiente, null).ToString());
        }
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
        Assert.Equal(Bandeja.SubidaConaset, Carpeta.ClasificarBandeja(EstadoCarpeta.SubidaConaset, Decision.Pendiente, Carpeta.ColaDeCaja));
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
    public void Requested_Crear_certificado_leaves_its_tray_for_En_certificado()
    {
        Assert.Equal(Bandeja.CrearCertificado, Carpeta.ClasificarBandeja(EstadoCarpeta.CrearCertificado, Decision.Pendiente, null));
        Assert.Equal(Bandeja.EnCertificado, Carpeta.ClasificarBandeja(EstadoCarpeta.CrearCertificado, Decision.Pendiente, null, pedida: true));
        Assert.Equal(Bandeja.EnCertificado, Carpeta.ClasificarBandeja(EstadoCarpeta.CrearCertificado, Decision.Pendiente, Carpeta.ColaDeCaja, pedida: true));
    }

    [Theory]
    [InlineData(EstadoCarpeta.SubidaConOficio)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    public void The_request_only_moves_carpetas_in_Crear_certificado(EstadoCarpeta estado)
    {
        Assert.Equal(
            Carpeta.ClasificarBandeja(estado, Decision.Pendiente, null),
            Carpeta.ClasificarBandeja(estado, Decision.Pendiente, null, pedida: true));
    }

    [Fact]
    public void Caja_and_final_decision_still_win_over_the_request()
    {
        Assert.Equal(Bandeja.Archivadas, Carpeta.ClasificarBandeja(EstadoCarpeta.CrearCertificado, Decision.Pendiente, "CAJA 1", pedida: true));
        Assert.Equal(Bandeja.Cerradas, Carpeta.ClasificarBandeja(EstadoCarpeta.CrearCertificado, Decision.Otorgado, null, pedida: true));
    }

    [Fact]
    public void Pedir_moves_the_carpeta_and_signing_it_away_returns_it_to_a_normal_tray()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.CrearCertificado);
        Assert.Equal(Bandeja.CrearCertificado, c.Bandeja);

        c.RegistrarPeticion("javiera", CarpetaFactory.Now);
        Assert.Equal(Bandeja.EnCertificado, c.Bandeja);

        c.ForzarEstado(EstadoCarpeta.SubidaConOficio, "javiera", CarpetaFactory.Now);
        Assert.Equal(Bandeja.SubidaConOficio, c.Bandeja);
        Assert.NotNull(c.FechaPeticion);
    }

    [Fact]
    public void Entering_Crear_certificado_again_starts_without_a_stale_request()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.CrearCertificado);
        c.RegistrarPeticion("javiera", CarpetaFactory.Now);
        c.ForzarEstado(EstadoCarpeta.SubidaConOficio, "javiera", CarpetaFactory.Now);

        c.ForzarEstado(EstadoCarpeta.CrearCertificado, "javiera", CarpetaFactory.Now);

        Assert.Null(c.FechaPeticion);
        Assert.Equal(Bandeja.CrearCertificado, c.Bandeja);
    }

    [Fact]
    public void Undo_back_into_Crear_certificado_does_not_leave_the_carpeta_requested()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.CrearCertificado);
        c.RegistrarPeticion("javiera", CarpetaFactory.Now);
        var peticion = c.FechaPeticion;
        var bandejaAntes = c.Bandeja;
        var punto = c.PuntoRestauracion();
        c.ForzarEstado(EstadoCarpeta.SubidaConOficio, "javiera", CarpetaFactory.Now);

        c.RestaurarEstado(punto, EstadoCarpeta.SubidaConOficio, "javiera", CarpetaFactory.Now);

        // The undo puts back exactly what the move found: the request it had is neither erased nor invented.
        Assert.Equal(EstadoCarpeta.CrearCertificado, c.Estado);
        Assert.Equal(peticion, c.FechaPeticion);
        Assert.Equal(bandejaAntes, c.Bandeja);
    }

    [Fact]
    public void Sql_expression_matches_pure_function_for_requested_carpetas()
    {
        var compiled = Carpeta.BandejaExpresion.Compile();
        foreach (var estado in Enum.GetValues<EstadoCarpeta>())
        foreach (var caja in new string?[] { null, "CAJA 1", Carpeta.ColaDeCaja })
        {
            var c = Carpeta.CrearImportada(Guid.NewGuid(), new Ciudadano(Rut.Parse("12345678-5"), "A", "B"), Sede.Placilla,
                new DateOnly(2026, 1, 1), null, null, estado, EstadoTransitions.DecisionFor(estado), null, null, caja, null, CarpetaFactory.Now);
            if (estado == EstadoCarpeta.CrearCertificado)
            {
                c.RegistrarPeticion("x", CarpetaFactory.Now);
            }
            Assert.Equal(Carpeta.ClasificarBandeja(c.Estado, c.Decision, c.CajaArchivo, c.FechaPeticion is not null), compiled(c));
        }
    }

    [Fact]
    public void Folder_moves_bandeja_automatically_when_estado_changes()
    {
        var c = CarpetaFactory.New();
        Assert.Equal(Bandeja.ParaRevision, c.Bandeja);
        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "u", CarpetaFactory.Now);
        Assert.Equal(Bandeja.SubidaConaset, c.Bandeja);
    }
}
