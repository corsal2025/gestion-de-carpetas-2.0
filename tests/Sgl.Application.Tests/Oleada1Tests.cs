using Sgl.Application.Carpetas;
using Sgl.Application.Estadisticas;
using Sgl.Application.Integridad;
using Sgl.Application.Solicitudes;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;
using static Sgl.Application.Tests.SolicitudesTestSupport;

namespace Sgl.Application.Tests;

/// <summary>Application rules added by the audit improvement round.</summary>
public sealed class Oleada1Tests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    // ---- A-02: one gate per circuit ----

    [Fact]
    public async Task Services_of_one_circuit_sharing_a_context_and_a_gate_survive_concurrent_use()
    {
        var a = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos, "12.345.678-5");
        var b = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos, "11.111.111-1");
        var gate = new UnidadDeTrabajoGate();
        using var ctx = _db.NewContext();
        var carpetas = new CarpetaService(new CarpetaRepository(ctx), _db.Clock, solicitudesF8: new SolicitudF8Repository(ctx), certificados: new CertificadoRepository(ctx), gate: gate);
        var f8 = new SolicitudesF8Service(new CarpetaRepository(ctx), new SolicitudF8Repository(ctx), new CertificadoRepository(ctx), new FirmantesRepository(ctx), _db.Clock, gate: gate);
        var duplicados = new DuplicadosService(new DuplicadoRepository(ctx), _db.Clock, gate);

        var tareas = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            tareas.Add(carpetas.GuardarObservacionAsync(a, $"OBS {i}", "ana"));
            tareas.Add(f8.ContarPorEstadoAsync());
            tareas.Add(carpetas.ListarAsync(new ListarCarpetasQuery(Take: 10)));
            tareas.Add(duplicados.ContarPendientesAsync());
        }
        tareas.Add(f8.SolicitarF8Async(b, "ana"));
        tareas.Add(carpetas.ObtenerAsync(a));

        await Task.WhenAll(tareas);

        Assert.Equal(1, (await f8.ContarPorEstadoAsync())[EstadoSolicitudF8.Solicitado]);
    }

    // ---- A-05: undoing the move that opened an automatic F8 ----

    [Fact]
    public async Task Undoing_the_move_to_NoExiste_cancels_the_F8_it_opened()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos);
        _db.AdvanceClock(TimeSpan.FromHours(1));
        var punto = await _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.NoExisteCarpeta, "ana"));
        Assert.Equal(1, (await _db.NewF8Service().ContarPorEstadoAsync())[EstadoSolicitudF8.Solicitado]);

        await _db.NewService().DeshacerEstadoAsync(id, punto, EstadoCarpeta.NoExisteCarpeta, "ana");

        var cuentas = await _db.NewF8Service().ContarPorEstadoAsync();
        Assert.Equal(0, cuentas[EstadoSolicitudF8.Solicitado]);
        Assert.Equal(1, cuentas[EstadoSolicitudF8.Cancelada]);
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, (await DetalleAsync(_db, id)).Estado);
    }

    [Fact]
    public async Task Undo_keeps_an_F8_the_operator_requested_before_the_move()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos);
        _db.AdvanceClock(TimeSpan.FromHours(1));
        await _db.NewF8Service().SolicitarF8Async(id, "ana");
        _db.AdvanceClock(TimeSpan.FromHours(1));
        var punto = await _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.NoExisteCarpeta, "ana"));

        await _db.NewService().DeshacerEstadoAsync(id, punto, EstadoCarpeta.NoExisteCarpeta, "ana");

        Assert.Equal(1, (await _db.NewF8Service().ContarPorEstadoAsync())[EstadoSolicitudF8.Solicitado]);
    }

    // ---- M-01: stale requests never drag the carpeta ----

    [Fact]
    public async Task MarcarSubido_on_a_stale_request_closes_it_without_moving_the_carpeta()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos);
        var solicitud = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        await _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.SubidaConaset, "ana"));

        var movida = await _db.NewF8Service().MarcarSubidoAsync(solicitud.SolicitudId, "ana");

        Assert.False(movida);
        Assert.Equal(EstadoCarpeta.SubidaConaset, (await DetalleAsync(_db, id)).Estado);
        Assert.Equal(1, (await _db.NewF8Service().ContarPorEstadoAsync())[EstadoSolicitudF8.Cancelada]);
    }

    [Fact]
    public async Task MarcarNoSeGenero_on_a_stale_request_opens_no_certificate_and_does_not_move_the_carpeta()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos);
        var solicitud = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        await _db.NewService().CambiarEstadoAsync(new CambiarEstadoCommand(id, EstadoCarpeta.SubidaConaset, "ana"));

        var resultado = await _db.NewF8Service().MarcarNoSeGeneroAsync(solicitud.SolicitudId, "ana");

        Assert.True(resultado.Obsoleta);
        Assert.Equal(EstadoCarpeta.SubidaConaset, (await DetalleAsync(_db, id)).Estado);
        Assert.Empty((await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery())).Items);
    }

    [Fact]
    public async Task Signing_a_SinCarpeta_certificate_whose_carpeta_moved_on_does_not_move_it_back()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var f8 = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        var abierto = await _db.NewF8Service().MarcarNoSeGeneroAsync(f8.SolicitudId, "ana");
        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.SubidaConaset, "ana");

        var movida = await _db.NewCertificadosService().MarcarFirmadoAsync(abierto.CertificadoId, "ana");

        Assert.False(movida);
        Assert.Equal(EstadoCarpeta.SubidaConaset, (await DetalleAsync(_db, id)).Estado);
        Assert.Equal(EstadoCertificado.Firmado, (await _db.NewCertificadosService().ObtenerAsync(abierto.CertificadoId)).Estado);
    }

    // ---- M-07: author and audit ----

    [Fact]
    public async Task Certificate_edits_require_an_author_and_are_audited_on_the_carpeta()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        var creado = await _db.NewCertificadosService().CrearManualAsync(
            new CrearCertificadoManualCommand(id, TipoCertificado.Oficio, null, null, "ana"));

        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewCertificadosService().ActualizarDatosAsync(creado.Id, null, null, "nota", " "));
        await _db.NewCertificadosService().ActualizarDatosAsync(creado.Id, null, null, "nota", "pedro");

        var historial = (await DetalleAsync(_db, id)).Historial;
        Assert.Contains(historial, h => h.Campo == "Certificado" && h.Nuevo!.StartsWith("Solicitado") && h.Usuario == "ana");
        Assert.Contains(historial, h => h.Campo == "Certificado" && h.Nuevo!.StartsWith("Datos actualizados") && h.Usuario == "pedro");
    }

    [Fact]
    public async Task Saving_the_signers_requires_the_name_of_the_official()
    {
        var firma = new Firmantes("Ana Director", "Director", "dir@example.test", "Sara", "Secretaria");

        await Assert.ThrowsAsync<DomainException>(() => _db.NewFirmantesService().GuardarAsync(firma, ""));
        Assert.Equal(Firmantes.Vacios, await _db.NewFirmantesService().ObtenerAsync());

        await _db.NewFirmantesService().GuardarAsync(firma, "pedro");
        Assert.Equal(firma, await _db.NewFirmantesService().ObtenerAsync());
    }

    [Fact]
    public async Task Requesting_an_F8_leaves_a_history_line()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos);

        await _db.NewF8Service().SolicitarF8Async(id, "ana");

        Assert.Contains((await DetalleAsync(_db, id)).Historial, h => h.Campo == "F8" && h.Nuevo == "Solicitado");
    }

    // ---- M-10: a failing request mail never fails "Pedir" ----

    [Fact]
    public async Task Pedir_survives_a_failing_mail_adapter()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        var ctx = _db.NewContext();
        var service = new CarpetaService(
            new CarpetaRepository(ctx), _db.Clock, notificador: new MailQueFalla(), certificados: new CertificadoRepository(ctx));

        var enviado = await service.PedirAsync(id, "ana");

        Assert.False(enviado);
        Assert.NotNull((await DetalleAsync(_db, id)).FechaPeticion);
    }

    private sealed class MailQueFalla : INotificadorPeticion
    {
        public Task<bool> EnviarAsync(PeticionCertificado peticion, CancellationToken ct = default) =>
            throw new InvalidOperationException("smtp://usuario:clave@servidor");
    }

    // ---- M-12: the document must be ready before the signature is recorded ----

    [Fact]
    public async Task Signing_is_refused_while_the_document_cannot_be_generated()
    {
        var id = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);
        var ctx = _db.NewContext();
        var service = new CertificadosService(
            new CarpetaRepository(ctx), new CertificadoRepository(ctx), new FirmantesRepository(ctx), _db.Clock,
            generador: new Sgl.Infrastructure.Documentos.GeneradorDocumentoCertificado(_db.Clock));
        var creado = await service.CrearManualAsync(new CrearCertificadoManualCommand(
            id, TipoCertificado.Oficio, new Dictionary<string, string> { ["Clase"] = "A2", ["Motivo"] = "Extravío" }, "1/2026", "ana"));

        await Assert.ThrowsAsync<DomainException>(() => service.MarcarFirmadoAsync(creado.Id, "ana"));
        Assert.Equal(EstadoCertificado.PorFirmar, (await service.ObtenerAsync(creado.Id)).Estado);
        Assert.Equal(EstadoCarpeta.CrearCertificado, (await DetalleAsync(_db, id)).Estado);

        await _db.NewFirmantesService().GuardarAsync(new Firmantes("Ana Directora", "Director", "", "Sara", "Secretaria"), "ana");
        Assert.True(await service.MarcarFirmadoAsync(creado.Id, "ana"));
    }

    // ---- M-14: Chile calendar ----

    [Fact]
    public void ArchivoPorMes_groups_by_the_Chile_month()
    {
        var carpeta = Guid.NewGuid();
        // 2026-06-01 02:00 UTC is still 31 May in Chile.
        var movimientos = new[] { new MovimientoCaja(carpeta, new DateTime(2026, 6, 1, 2, 0, 0, DateTimeKind.Utc), null, "CAJA 1") };

        var (porMes, _) = EstadisticasCalculo.ArchivoPorMes(movimientos);

        var mes = Assert.Single(porMes);
        Assert.Equal(5, mes.Mes);
    }

    // ---- M-20: the watchdog never hides the original failure ----

    [Fact]
    public async Task A_failing_run_rethrows_the_original_exception_even_if_recording_it_fails()
    {
        var servicio = new IntegridadService(new RepositorioQueFalla(), _db.Clock);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EjecutarAsync(OrigenIntegridad.Manual));

        Assert.Equal("falla original", ex.Message);
    }

    private sealed class RepositorioQueFalla : IIntegridadRepository
    {
        public Task<EntradaIntegridad> ObtenerEntradaAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("falla original");

        public Task GuardarCorridaAsync(IntegridadCorrida corrida, CancellationToken ct = default) =>
            throw new TimeoutException("no se pudo guardar");

        public Task<IntegridadCorrida?> UltimaCorridaAsync(CancellationToken ct = default) => Task.FromResult<IntegridadCorrida?>(null);

        public Task<IReadOnlyList<IntegridadCorrida>> UltimasCorridasAsync(int cantidad, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IntegridadCorrida>>([]);

        public Task<IReadOnlyDictionary<Guid, ProblemaIntegridadDto>> IdentificarAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProblemaIntegridadDto>>(new Dictionary<Guid, ProblemaIntegridadDto>());
    }
}
