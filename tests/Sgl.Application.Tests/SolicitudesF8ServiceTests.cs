using Sgl.Application.Carpetas;
using Sgl.Application.Solicitudes;
using Sgl.Domain;
using static Sgl.Application.Tests.SolicitudesTestSupport;

namespace Sgl.Application.Tests;

public sealed class SolicitudesF8ServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<IReadOnlyList<SolicitudF8Dto>> Todas(EstadoSolicitudF8? estado = null, string? texto = null) =>
        (await _db.NewF8Service().ListarAsync(new ListarSolicitudesF8Query(estado, texto))).Items;

    [Fact]
    public async Task Solicitar_creates_an_open_request_with_the_carpeta_data()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos, nombre: "juan", apellido: "perez", sede: Sede.Placilla);

        var resultado = await _db.NewF8Service().SolicitarF8Async(id, "ana");

        Assert.True(resultado.Creada);
        Assert.False(resultado.YaAbierta);
        var fila = Assert.Single(await Todas());
        Assert.Equal(resultado.SolicitudId, fila.Id);
        Assert.Equal(id, fila.CarpetaId);
        Assert.Equal("12.345.678-5", fila.Rut);
        Assert.Equal("JUAN PEREZ", fila.NombreCompleto);
        Assert.Equal(Sede.Placilla, fila.Sede);
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, fila.EstadoOrigen);
        Assert.Equal(EstadoSolicitudF8.Solicitado, fila.Estado);
        Assert.Equal("ana", fila.SolicitadoPor);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, fila.SolicitadoEn);
    }

    [Fact]
    public async Task Solicitar_twice_returns_the_open_one_without_duplicating()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var primera = await _db.NewF8Service().SolicitarF8Async(id, "ana");

        var segunda = await _db.NewF8Service().SolicitarF8Async(id, "luis");

        Assert.False(segunda.Creada);
        Assert.True(segunda.YaAbierta);
        Assert.Equal(primera.SolicitudId, segunda.SolicitudId);
        Assert.False(string.IsNullOrWhiteSpace(segunda.Mensaje));
        Assert.Single(await Todas());
    }

    [Fact]
    public async Task Solicitar_from_a_state_that_cannot_request_is_refused()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.EsperaExamen);

        await Assert.ThrowsAsync<DomainException>(() => _db.NewF8Service().SolicitarF8Async(id, "ana"));
        Assert.Empty(await Todas());
    }

    [Fact]
    public async Task Solicitar_unknown_carpeta_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _db.NewF8Service().SolicitarF8Async(Guid.NewGuid(), "ana"));
    }

    [Fact]
    public async Task Solicitar_again_is_allowed_once_the_previous_request_was_resolved()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CambioDomicilio);
        var primera = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        await _db.NewF8Service().MarcarSubidoAsync(primera.SolicitudId, "javiera");
        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.CambioDomicilio, "ana");

        var nueva = await _db.NewF8Service().SolicitarF8Async(id, "ana");

        Assert.True(nueva.Creada);
        Assert.NotEqual(primera.SolicitudId, nueva.SolicitudId);
    }

    [Fact]
    public async Task MarcarSubido_moves_the_carpeta_to_SubidaConF8_with_audit_and_closing_date()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var solicitud = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        _db.AdvanceClock(TimeSpan.FromHours(3));

        await _db.NewF8Service().MarcarSubidoAsync(solicitud.SolicitudId, "javiera", " listo ");

        var fila = Assert.Single(await Todas());
        Assert.Equal(EstadoSolicitudF8.Subido, fila.Estado);
        Assert.Equal("javiera", fila.ResueltoPor);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, fila.ResueltoEn);
        Assert.Equal("listo", fila.Observacion);
        var carpeta = await DetalleAsync(_db, id);
        Assert.Equal(EstadoCarpeta.SubidaConF8, carpeta.Estado);
        Assert.NotNull(carpeta.FechaCierre);
        Assert.Contains(carpeta.Historial, h => h.Campo == "Estado" && h.Nuevo == "SubidaConF8" && h.Usuario == "javiera");
    }

    [Fact]
    public async Task Resolving_twice_is_refused()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var solicitud = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        await _db.NewF8Service().MarcarSubidoAsync(solicitud.SolicitudId, "javiera");

        await Assert.ThrowsAsync<DomainException>(() => _db.NewF8Service().MarcarSubidoAsync(solicitud.SolicitudId, "javiera"));
        await Assert.ThrowsAsync<DomainException>(() => _db.NewF8Service().MarcarNoSeGeneroAsync(solicitud.SolicitudId, "javiera"));
    }

    [Fact]
    public async Task Resolving_an_unknown_request_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _db.NewF8Service().MarcarSubidoAsync(Guid.NewGuid(), "javiera"));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.NewF8Service().MarcarNoSeGeneroAsync(Guid.NewGuid(), "javiera"));
    }

    [Fact]
    public async Task MarcarNoSeGenero_moves_the_carpeta_to_CrearCertificado_and_opens_a_SinCarpeta_certificate()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnOf43);
        var solicitud = await _db.NewF8Service().SolicitarF8Async(id, "ana");

        var resultado = await _db.NewF8Service().MarcarNoSeGeneroAsync(solicitud.SolicitudId, "javiera", "no aparece");

        var fila = Assert.Single(await Todas());
        Assert.Equal(EstadoSolicitudF8.NoSeGenero, fila.Estado);
        Assert.Equal("no aparece", fila.Observacion);
        Assert.Equal(EstadoCarpeta.CrearCertificado, (await DetalleAsync(_db, id)).Estado);

        var certificado = Assert.Single((await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery())).Items);
        Assert.Equal(resultado.CertificadoId, certificado.Id);
        Assert.Equal(TipoCertificado.SinCarpeta, certificado.Tipo);
        Assert.Equal(OrigenCertificado.DesdeF8, certificado.Origen);
        Assert.Equal(solicitud.SolicitudId, certificado.SolicitudF8Id);
        Assert.Equal(EstadoCertificado.PorFirmar, certificado.Estado);
        Assert.Equal("javiera", certificado.SolicitadoPor);
        Assert.Null(certificado.EnviadoADirectorEn);
    }

    [Fact]
    public async Task MarcarNoSeGenero_notifies_through_the_port_and_stays_unsent_when_nothing_was_sent()
    {
        var notificador = new NotificadorFalso(resultado: false);
        await _db.NewFirmantesService().GuardarAsync(new Firmantes("Ana Director", "Director (s)", "director@example.test", "Sara", "Secretaria"));
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var solicitud = await _db.NewF8Service(notificador).SolicitarF8Async(id, "ana");

        var resultado = await _db.NewF8Service(notificador).MarcarNoSeGeneroAsync(solicitud.SolicitudId, "javiera");

        Assert.False(resultado.EnviadoADirector);
        var mensaje = Assert.Single(notificador.Enviados);
        Assert.Equal(resultado.CertificadoId, mensaje.CertificadoId);
        Assert.Equal(TipoCertificado.SinCarpeta, mensaje.Tipo);
        Assert.Equal("12.345.678-5", mensaje.Rut);
        Assert.Equal("director@example.test", mensaje.Firmantes.DirectorCorreo);
        Assert.Equal("Director (s)", mensaje.Firmantes.DirectorCargo);
        Assert.Null((await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery())).Items.Single().EnviadoADirectorEn);
    }

    [Fact]
    public async Task MarcarNoSeGenero_records_the_sending_only_when_the_adapter_really_sent()
    {
        var notificador = new NotificadorFalso(resultado: true);
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var solicitud = await _db.NewF8Service(notificador).SolicitarF8Async(id, "ana");

        var resultado = await _db.NewF8Service(notificador).MarcarNoSeGeneroAsync(solicitud.SolicitudId, "javiera");

        Assert.True(resultado.EnviadoADirector);
        var certificado = (await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery())).Items.Single();
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, certificado.EnviadoADirectorEn);
    }

    [Fact]
    public async Task A_failing_notifier_never_loses_the_resolution()
    {
        var notificador = new NotificadorFalso(falla: new InvalidOperationException("servidor caído"));
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var solicitud = await _db.NewF8Service().SolicitarF8Async(id, "ana");

        var resultado = await _db.NewF8Service(notificador).MarcarNoSeGeneroAsync(solicitud.SolicitudId, "javiera");

        Assert.False(resultado.EnviadoADirector);
        Assert.NotNull(resultado.ErrorEnvio);
        Assert.DoesNotContain("servidor caído", resultado.ErrorEnvio);
        Assert.Equal(EstadoCarpeta.CrearCertificado, (await DetalleAsync(_db, id)).Estado);
        Assert.Single((await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery())).Items);
    }

    [Fact]
    public async Task MarcarNoSeGenero_reuses_an_open_SinCarpeta_certificate_instead_of_duplicating_it()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var primera = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        var uno = await _db.NewF8Service().MarcarNoSeGeneroAsync(primera.SolicitudId, "javiera");
        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.NoExisteCarpeta, "ana");
        var segunda = await _db.NewF8Service().SolicitarF8Async(id, "ana");

        var dos = await _db.NewF8Service().MarcarNoSeGeneroAsync(segunda.SolicitudId, "javiera");

        Assert.Equal(uno.CertificadoId, dos.CertificadoId);
        Assert.Single((await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery())).Items);
    }

    [Fact]
    public async Task Listing_filters_by_state_and_by_rut_or_name_and_pages()
    {
        var a = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta, "12.345.678-5", "Juan", "Pérez");
        var b = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta, "9.876.543-3", "María", "González");
        var c = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta, "7.654.321-6", "Pedro", "Soto");
        var sa = await _db.NewF8Service().SolicitarF8Async(a, "ana");
        _db.AdvanceClock(TimeSpan.FromMinutes(1));
        await _db.NewF8Service().SolicitarF8Async(b, "ana");
        _db.AdvanceClock(TimeSpan.FromMinutes(1));
        await _db.NewF8Service().SolicitarF8Async(c, "ana");
        await _db.NewF8Service().MarcarSubidoAsync(sa.SolicitudId, "javiera");

        Assert.Equal(3, (await Todas()).Count);
        Assert.Equal(2, (await Todas(EstadoSolicitudF8.Solicitado)).Count);
        Assert.Single(await Todas(EstadoSolicitudF8.Subido));
        Assert.Equal(b, Assert.Single(await Todas(texto: "maria")).CarpetaId);
        Assert.Equal(c, Assert.Single(await Todas(texto: "7654321")).CarpetaId);
        Assert.Equal(c, Assert.Single(await Todas(texto: "7.654.321-6")).CarpetaId);

        var pagina = await _db.NewF8Service().ListarAsync(new ListarSolicitudesF8Query(EstadoSolicitudF8.Solicitado, Skip: 1, Take: 1));
        Assert.Equal(2, pagina.Total);
        Assert.Single(pagina.Items);
        // The open queue is oldest first, so the second page holds the newest request.
        Assert.Equal(c, pagina.Items[0].CarpetaId);
    }

    [Fact]
    public async Task Counts_per_state_include_zeros_and_ignore_cancelled_carpetas()
    {
        var a = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta, "12.345.678-5");
        var b = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta, "9.876.543-3");
        await _db.NewF8Service().SolicitarF8Async(a, "ana");
        await _db.NewF8Service().SolicitarF8Async(b, "ana");

        var antes = await _db.NewF8Service().ContarPorEstadoAsync();
        Assert.Equal(2, antes[EstadoSolicitudF8.Solicitado]);
        Assert.Equal(0, antes[EstadoSolicitudF8.Subido]);
        Assert.Equal(0, antes[EstadoSolicitudF8.NoSeGenero]);

        await _db.NewService().AnularAsync(b, "Duplicada", "ana");

        var despues = await _db.NewF8Service().ContarPorEstadoAsync();
        Assert.Equal(1, despues[EstadoSolicitudF8.Solicitado]);
        Assert.Single(await Todas());
    }

    [Fact]
    public async Task Resolving_the_request_of_a_cancelled_carpeta_is_refused_and_changes_nothing()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var solicitud = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        await _db.NewService().AnularAsync(id, "Duplicada", "ana");

        await Assert.ThrowsAsync<DomainException>(() => _db.NewF8Service().MarcarSubidoAsync(solicitud.SolicitudId, "javiera"));
    }

    // ---- Automatic request when a searched carpeta turns out not to exist ----

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43)]
    public async Task Moving_to_NoExisteCarpeta_from_a_search_tray_creates_the_F8_request(EstadoCarpeta bandejaBusqueda)
    {
        var id = await CrearCarpetaAsync(_db, bandejaBusqueda);

        await _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.NoExisteCarpeta, "javiera"));

        var fila = Assert.Single(await Todas());
        Assert.Equal(id, fila.CarpetaId);
        Assert.Equal(EstadoSolicitudF8.Solicitado, fila.Estado);
        Assert.Equal(bandejaBusqueda, fila.EstadoOrigen);
        Assert.Equal("javiera", fila.SolicitadoPor);
        Assert.Equal(EstadoCarpeta.NoExisteCarpeta, fila.EstadoCarpeta);
    }

    [Fact]
    public async Task The_manual_correction_path_creates_the_F8_request_too()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos);

        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.NoExisteCarpeta, "javiera");

        Assert.Single(await Todas());
    }

    [Fact]
    public async Task The_automatic_request_never_duplicates_an_open_one()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos);
        await _db.NewF8Service().SolicitarF8Async(id, "ana");

        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.NoExisteCarpeta, "javiera");

        var fila = Assert.Single(await Todas());
        Assert.Equal("ana", fila.SolicitadoPor);
    }

    [Theory]
    [InlineData(EstadoCarpeta.Citada)]
    [InlineData(EstadoCarpeta.PorClasificar)]
    [InlineData(EstadoCarpeta.CambioDomicilio)]
    public async Task Moving_to_NoExisteCarpeta_from_other_states_does_not_create_a_request(EstadoCarpeta origen)
    {
        var id = await CrearCarpetaAsync(_db, origen);

        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.NoExisteCarpeta, "javiera");

        Assert.Empty(await Todas());
    }

    [Fact]
    public async Task Other_moves_out_of_a_search_tray_do_not_create_a_request()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos);

        await _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.SubidaConaset, "javiera"));

        Assert.Empty(await Todas());
    }

    // ---- Stale rows: the carpeta reached a final state after the F8 was requested ----

    [Theory]
    [InlineData(EstadoCarpeta.Otorgado)]
    [InlineData(EstadoCarpeta.Denegado)]
    public async Task MarcarSubido_is_refused_when_the_carpeta_is_already_final_and_the_request_stays_open(EstadoCarpeta final)
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var solicitud = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        await _db.NewService().ForzarEstadoAsync(id, final, "javiera");

        var ex = await Assert.ThrowsAsync<DomainException>(() => _db.NewF8Service().MarcarSubidoAsync(solicitud.SolicitudId, "javiera"));

        Assert.Contains("estado final", ex.Message);
        Assert.Equal(EstadoSolicitudF8.Solicitado, Assert.Single(await Todas()).Estado);
        Assert.Equal(final, (await DetalleAsync(_db, id)).Estado);
    }

    [Theory]
    [InlineData(EstadoCarpeta.Otorgado)]
    [InlineData(EstadoCarpeta.Denegado)]
    public async Task MarcarNoSeGenero_is_refused_when_the_carpeta_is_already_final_and_opens_no_certificate(EstadoCarpeta final)
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var solicitud = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        await _db.NewService().ForzarEstadoAsync(id, final, "javiera");

        var ex = await Assert.ThrowsAsync<DomainException>(() => _db.NewF8Service().MarcarNoSeGeneroAsync(solicitud.SolicitudId, "javiera"));

        Assert.Contains("estado final", ex.Message);
        Assert.Equal(EstadoSolicitudF8.Solicitado, Assert.Single(await Todas()).Estado);
        Assert.Equal(final, (await DetalleAsync(_db, id)).Estado);
        Assert.Empty((await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery())).Items);
    }
}
