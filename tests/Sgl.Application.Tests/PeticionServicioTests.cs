using Sgl.Application.Carpetas;
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
        var service = new CarpetaService(new CarpetaRepository(_db.NewContext()), _db.Clock, notificador: correo);

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
}
