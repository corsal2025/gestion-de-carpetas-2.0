using Sgl.Application.Solicitudes;
using Sgl.Domain;
using static Sgl.Application.Tests.SolicitudesTestSupport;

namespace Sgl.Application.Tests;

public sealed class CertificadosServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<IReadOnlyList<SolicitudCertificadoDto>> Todos(
        EstadoCertificado? estado = null, TipoCertificado? tipo = null, string? texto = null) =>
        (await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery(estado, tipo, texto))).Items;

    private Task<CertificadoCreado> CrearManual(
        Guid carpetaId,
        TipoCertificado tipo,
        Dictionary<string, string>? parametros = null,
        string? numeroOficio = null,
        INotificadorCertificado? notificador = null) =>
        _db.NewCertificadosService(notificador).CrearManualAsync(
            new CrearCertificadoManualCommand(carpetaId, tipo, parametros, numeroOficio, "ana"));

    [Fact]
    public async Task Manual_registro_civil_is_stored_with_its_parameters_and_carpeta_data()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado, sede: Sede.MercadoPuerto);

        var creado = await CrearManual(id, TipoCertificado.RegistroCivil,
            new() { ["Clase"] = "B", ["Ley"] = "18290", ["FechaOtorgamiento"] = "2020-01-02" });

        var fila = Assert.Single(await Todos());
        Assert.Equal(creado.Id, fila.Id);
        Assert.Equal(TipoCertificado.RegistroCivil, fila.Tipo);
        Assert.Equal(OrigenCertificado.Manual, fila.Origen);
        Assert.Null(fila.SolicitudF8Id);
        Assert.Equal("12.345.678-5", fila.Rut);
        Assert.Equal("JUAN PÉREZ", fila.NombreCompleto);
        Assert.Equal(Sede.MercadoPuerto, fila.Sede);
        Assert.Equal("ana", fila.SolicitadoPor);
        Assert.Equal(EstadoCertificado.PorFirmar, fila.Estado);
        Assert.Equal("B", fila.Parametros["Clase"]);
        Assert.Equal("18290", fila.Parametros["Ley"]);
        Assert.Equal("2020-01-02", fila.Parametros["FechaOtorgamiento"]);
    }

    [Fact]
    public async Task Manual_cannot_create_a_SinCarpeta_certificate()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);

        await Assert.ThrowsAsync<DomainException>(() => CrearManual(id, TipoCertificado.SinCarpeta));
    }

    [Fact]
    public async Task Only_one_open_certificate_per_carpeta_and_tipo()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        await CrearManual(id, TipoCertificado.Oficio, numeroOficio: "1/2026");

        await Assert.ThrowsAsync<DomainException>(() => CrearManual(id, TipoCertificado.Oficio));
        await CrearManual(id, TipoCertificado.RegistroCivil);

        Assert.Equal(2, (await Todos()).Count);
    }

    [Fact]
    public async Task A_new_certificate_of_the_same_tipo_is_allowed_after_signing_the_previous_one()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        var primero = await CrearManual(id, TipoCertificado.Oficio);
        await _db.NewCertificadosService().MarcarFirmadoAsync(primero.Id, "javiera", "5/2026");

        var segundo = await CrearManual(id, TipoCertificado.Oficio);

        Assert.NotEqual(primero.Id, segundo.Id);
    }

    [Fact]
    public async Task Creating_for_an_unknown_or_cancelled_carpeta_is_refused()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => CrearManual(Guid.NewGuid(), TipoCertificado.Oficio));

        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        await _db.NewService().AnularAsync(id, "Duplicada", "ana");
        await Assert.ThrowsAsync<DomainException>(() => CrearManual(id, TipoCertificado.Oficio));
    }

    [Fact]
    public async Task Creating_calls_the_notifier_with_the_signers_but_records_no_sending_for_a_noop()
    {
        var notificador = new NotificadorFalso(resultado: false);
        await _db.NewFirmantesService().GuardarAsync(new Firmantes("Ana Director", "Director", "dir@example.test", "Sara Secretaria", "Secretaria"), "javiera");
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);

        var creado = await CrearManual(id, TipoCertificado.Oficio, new() { ["Clase"] = "A2", ["Motivo"] = "Extravío" }, notificador: notificador);

        Assert.False(creado.EnviadoADirector);
        var mensaje = Assert.Single(notificador.Enviados);
        Assert.Equal(TipoCertificado.Oficio, mensaje.Tipo);
        Assert.Equal(OrigenCertificado.Manual, mensaje.Origen);
        Assert.Equal("Extravío", mensaje.Parametros["Motivo"]);
        Assert.Equal("Sara Secretaria", mensaje.Firmantes.SecretariaNombre);
        Assert.Null((await Todos()).Single().EnviadoADirectorEn);
    }

    [Fact]
    public async Task A_real_send_sets_EnviadoADirectorEn()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);

        var creado = await CrearManual(id, TipoCertificado.RegistroCivil, notificador: new NotificadorFalso(resultado: true));

        Assert.True(creado.EnviadoADirector);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, (await Todos()).Single().EnviadoADirectorEn);
    }

    [Fact]
    public async Task A_throwing_notifier_keeps_the_request_and_reports_the_error()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);

        var creado = await CrearManual(id, TipoCertificado.RegistroCivil,
            notificador: new NotificadorFalso(falla: new InvalidOperationException("EWS no responde")));

        Assert.False(creado.EnviadoADirector);
        Assert.NotNull(creado.ErrorEnvio);
        Assert.DoesNotContain("EWS no responde", creado.ErrorEnvio);
        Assert.Single(await Todos());
    }

    [Theory]
    [InlineData(EstadoCarpeta.Otorgado)]
    [InlineData(EstadoCarpeta.Denegado)]
    public async Task MarcarFirmado_never_overwrites_a_final_carpeta_state(EstadoCarpeta final)
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        var creado = await CrearManual(id, TipoCertificado.RegistroCivil);
        await _db.NewService().ForzarEstadoAsync(id, final, "ana");

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => _db.NewCertificadosService().MarcarFirmadoAsync(creado.Id, "javiera"));

        Assert.Contains("estado final", ex.Message);
        Assert.Equal(final, (await DetalleAsync(_db, id)).Estado);
        Assert.Equal(EstadoCertificado.PorFirmar, Assert.Single(await Todos()).Estado);
    }

    [Fact]
    public async Task MarcarFirmado_moves_the_carpeta_to_SubidaConOficio_with_the_actor_in_the_history()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        var creado = await CrearManual(id, TipoCertificado.Oficio);
        _db.AdvanceClock(TimeSpan.FromDays(1));

        await _db.NewCertificadosService().MarcarFirmadoAsync(creado.Id, "javiera", "77/2026");

        var fila = Assert.Single(await Todos());
        Assert.Equal(EstadoCertificado.Firmado, fila.Estado);
        Assert.Equal("javiera", fila.FirmadoPor);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, fila.FirmadoEn);
        Assert.Equal("77/2026", fila.NumeroOficio);
        var carpeta = await DetalleAsync(_db, id);
        Assert.Equal(EstadoCarpeta.SubidaConOficio, carpeta.Estado);
        Assert.NotNull(carpeta.FechaCierre);
        Assert.Contains(carpeta.Historial, h => h.Campo == "Estado" && h.Nuevo == "SubidaConOficio" && h.Usuario == "javiera");
    }

    [Fact]
    public async Task MarcarFirmado_twice_or_unknown_is_refused()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        var creado = await CrearManual(id, TipoCertificado.RegistroCivil);
        await _db.NewCertificadosService().MarcarFirmadoAsync(creado.Id, "javiera");

        await Assert.ThrowsAsync<DomainException>(() => _db.NewCertificadosService().MarcarFirmadoAsync(creado.Id, "javiera"));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.NewCertificadosService().MarcarFirmadoAsync(Guid.NewGuid(), "javiera"));
    }

    [Fact]
    public async Task ActualizarDatos_edits_parameters_while_unsigned()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        var f8 = await _db.NewF8Service().SolicitarF8Async(await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta, "9.876.543-3"), "ana");
        var sinCarpeta = await _db.NewF8Service().MarcarNoSeGeneroAsync(f8.SolicitudId, "javiera");

        await _db.NewCertificadosService().ActualizarDatosAsync(sinCarpeta.CertificadoId,
            new Dictionary<string, string> { [SolicitudCertificado.ClaveAnioRenovacion] = "2018" }, null, "revisar", "javiera");

        var fila = (await Todos(tipo: TipoCertificado.SinCarpeta)).Single();
        Assert.Equal("2018", fila.Parametros[SolicitudCertificado.ClaveAnioRenovacion]);
        Assert.Equal("revisar", fila.Observacion);
        _ = id;
    }

    [Fact]
    public async Task Listing_filters_by_state_tipo_and_text_and_counts_per_state()
    {
        var a = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado, "12.345.678-5", "Juan", "Pérez");
        var b = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado, "9.876.543-3", "María", "González");
        var oficioA = await CrearManual(a, TipoCertificado.Oficio);
        await CrearManual(b, TipoCertificado.RegistroCivil);
        await _db.NewCertificadosService().MarcarFirmadoAsync(oficioA.Id, "javiera");

        Assert.Equal(2, (await Todos()).Count);
        Assert.Single(await Todos(EstadoCertificado.PorFirmar));
        Assert.Single(await Todos(EstadoCertificado.Firmado));
        Assert.Equal(b, Assert.Single(await Todos(tipo: TipoCertificado.RegistroCivil)).CarpetaId);
        Assert.Equal(a, Assert.Single(await Todos(texto: "perez")).CarpetaId);
        Assert.Equal(b, Assert.Single(await Todos(texto: "9876543")).CarpetaId);

        var cuentas = await _db.NewCertificadosService().ContarPorEstadoAsync();
        Assert.Equal(1, cuentas[EstadoCertificado.PorFirmar]);
        Assert.Equal(1, cuentas[EstadoCertificado.Firmado]);
        var soloOficios = await _db.NewCertificadosService().ContarPorEstadoAsync(TipoCertificado.Oficio);
        Assert.Equal(0, soloOficios[EstadoCertificado.PorFirmar]);
        Assert.Equal(1, soloOficios[EstadoCertificado.Firmado]);

        var pagina = await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery(Skip: 1, Take: 1));
        Assert.Equal(2, pagina.Total);
        Assert.Single(pagina.Items);
    }
}
