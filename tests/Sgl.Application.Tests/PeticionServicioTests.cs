using Sgl.Application.Carpetas;
using Sgl.Application.Solicitudes;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>"Pedir" from Crear certificado / oficio: the request is stored and handed to the mail port.</summary>
public sealed class PeticionServicioTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private sealed class NotificadorFalso(bool enviado) : INotificadorPeticion
    {
        public List<PeticionCertificado> Enviadas { get; } = [];

        public Task<bool> EnviarAsync(PeticionCertificado peticion, CancellationToken ct = default)
        {
            Enviadas.Add(peticion);
            return Task.FromResult(enviado);
        }
    }

    private async Task<Guid> EnCertificadoAsync()
    {
        var id = await _db.NewService().RegistrarAsync(new RegistrarCarpetaCommand("12345678-5", "JUAN", "PEREZ", Sede.Placilla, new DateOnly(2026, 5, 4), "ana"));
        await _db.NewService().ForzarEstadoAsync(id, EstadoCarpeta.CrearCertificado, "ana");
        return id;
    }

    [Fact]
    public async Task Pedir_stores_the_request_date_and_sends_it_through_the_mail_port()
    {
        var id = await EnCertificadoAsync();
        var correo = new NotificadorFalso(enviado: true);
        var ctx = _db.NewContext();
        var service = new CarpetaService(new CarpetaRepository(ctx), _db.Clock, notificador: correo, certificados: new CertificadoRepository(ctx));

        var enviado = await service.PedirAsync(id, "javiera");

        Assert.True(enviado);
        var p = Assert.Single(correo.Enviadas);
        Assert.Equal(id, p.CarpetaId);
        Assert.Equal("12.345.678-5", p.Rut);
        Assert.Equal("JUAN PEREZ", p.NombreCompleto);
        Assert.Equal("javiera", p.SolicitadoPor);
        var det = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, det.FechaPeticion);
        Assert.Contains(det.Historial, h => h.Campo == "FechaPeticion");
    }

    [Fact]
    public async Task Pedir_without_mail_configured_still_records_the_request()
    {
        var id = await EnCertificadoAsync();

        var enviado = await _db.NewService().PedirAsync(id, "javiera");

        Assert.False(enviado);
        Assert.NotNull((await _db.NewService().ObtenerAsync(id)).FechaPeticion);
    }

    private async Task<IReadOnlyList<SolicitudCertificadoDto>> CertificadosAsync(EstadoCertificado? estado = null) =>
        (await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery(estado, null, null))).Items;

    private async Task<IReadOnlyDictionary<Bandeja, int>> ConteoAsync() =>
        await new CarpetaRepository(_db.NewContext()).CountByBandejaAsync(new CarpetaSearch(null, null, null, null, null, null, null, null, 0, null));

    private async Task<Bandeja> BandejaDeAsync(Guid id)
    {
        var d = await _db.NewService().ObtenerAsync(id);
        return Carpeta.ClasificarBandeja(d.Estado, d.Decision, d.CajaArchivo, d.FechaPeticion is not null);
    }

    [Fact]
    public async Task Pedir_hands_the_carpeta_to_certificates_and_it_leaves_the_tray()
    {
        var id = await EnCertificadoAsync();
        Assert.Equal(1, (await ConteoAsync())[Bandeja.CrearCertificado]);

        await _db.NewService().PedirAsync(id, "javiera");

        var fila = Assert.Single(await CertificadosAsync(EstadoCertificado.PorFirmar));
        Assert.Equal(id, fila.CarpetaId);
        Assert.Equal(TipoCertificado.SinCarpeta, fila.Tipo);
        Assert.Equal(OrigenCertificado.Manual, fila.Origen);
        Assert.Equal("javiera", fila.SolicitadoPor);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, (await _db.NewService().ObtenerAsync(id)).FechaPeticion);
        var conteo = await ConteoAsync();
        Assert.Equal(0, conteo[Bandeja.CrearCertificado]);
        Assert.Equal(1, conteo[Bandeja.EnCertificado]);
        Assert.Equal(1, conteo.Values.Sum());
        Assert.Equal(Bandeja.EnCertificado, await BandejaDeAsync(id));
    }

    [Fact]
    public async Task Pedir_twice_is_idempotent()
    {
        var id = await EnCertificadoAsync();
        var correo = new NotificadorFalso(enviado: true);
        await _db.NewService().PedirAsync(id, "javiera");
        _db.AdvanceClock(TimeSpan.FromHours(2));
        var ctx = _db.NewContext();
        var service = new CarpetaService(new CarpetaRepository(ctx), _db.Clock, notificador: correo, certificados: new CertificadoRepository(ctx));

        var enviado = await service.PedirAsync(id, "ana");

        Assert.False(enviado);
        Assert.Empty(correo.Enviadas);
        Assert.Single(await CertificadosAsync());
        var det = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(1, det.Historial.Count(h => h.Campo == "FechaPeticion"));
    }

    [Fact]
    public async Task Pedir_reuses_the_certificate_opened_by_the_F8_flow()
    {
        var id = await SolicitudesTestSupport.CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        var f8 = await _db.NewF8Service().SolicitarF8Async(id, "ana");
        var resultado = await _db.NewF8Service().MarcarNoSeGeneroAsync(f8.SolicitudId, "ana");

        await _db.NewService().PedirAsync(id, "javiera");

        var fila = Assert.Single(await CertificadosAsync());
        Assert.Equal(resultado.CertificadoId, fila.Id);
        Assert.Equal(OrigenCertificado.DesdeF8, fila.Origen);
        Assert.Equal(EstadoCarpeta.CrearCertificado, (await _db.NewService().ObtenerAsync(id)).Estado);
        Assert.Equal(Bandeja.EnCertificado, await BandejaDeAsync(id));
    }

    [Fact]
    public async Task Pedir_outside_Crear_certificado_is_refused_and_creates_nothing()
    {
        var id = await SolicitudesTestSupport.CrearCarpetaAsync(_db, EstadoCarpeta.SeEncuentraEnArchivos);

        await Assert.ThrowsAsync<DomainException>(() => _db.NewService().PedirAsync(id, "javiera"));

        Assert.Empty(await CertificadosAsync());
        Assert.Null((await _db.NewService().ObtenerAsync(id)).FechaPeticion);
    }

    [Fact]
    public async Task Signing_the_certificate_sends_the_requested_carpeta_to_Subida_con_oficio()
    {
        var id = await EnCertificadoAsync();
        await _db.NewService().PedirAsync(id, "javiera");
        var cert = Assert.Single(await CertificadosAsync());

        await _db.NewCertificadosService().MarcarFirmadoAsync(cert.Id, "javiera");

        var det = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(EstadoCarpeta.SubidaConOficio, det.Estado);
        Assert.Equal(Bandeja.SubidaConOficio, await BandejaDeAsync(id));
        Assert.NotNull(det.FechaPeticion);
        var conteo = await ConteoAsync();
        Assert.Equal(0, conteo[Bandeja.EnCertificado]);
        Assert.Equal(1, conteo[Bandeja.SubidaConOficio]);
    }
}
