using Sgl.Application.Carpetas;
using Sgl.Application.Solicitudes;
using Sgl.Domain;

namespace Sgl.Application.Tests;

internal static class SolicitudesTestSupport
{
    public static async Task<Guid> CrearCarpetaAsync(
        TestDb db,
        EstadoCarpeta estado,
        string rut = "12.345.678-5",
        string nombre = "Juan",
        string apellido = "Pérez",
        Sede sede = Sede.AvArgentina)
    {
        var id = await db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(
            rut, nombre, apellido, sede, new DateOnly(2026, 4, 24), "ana", ConfirmarDuplicado: true));
        await db.NewService().ForzarEstadoAsync(id, estado, "ana");
        return id;
    }

    public static async Task<CarpetaDetalleDto> DetalleAsync(TestDb db, Guid id) =>
        await db.NewService().ObtenerAsync(id);
}

internal sealed class NotificadorFalso(bool resultado = false, Exception? falla = null) : INotificadorCertificado
{
    public List<NotificacionCertificado> Enviados { get; } = [];

    public Task<bool> EnviarAsync(NotificacionCertificado notificacion, CancellationToken ct = default)
    {
        Enviados.Add(notificacion);
        return falla is null ? Task.FromResult(resultado) : Task.FromException<bool>(falla);
    }
}
