using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Tests;

public sealed class MarcarEnviadasSinCorreoTests
{
    [Fact]
    public void MarcaSoloLasPeticionesPendientesSeleccionadasSinDestinatario()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            using var repository = new PeticionRepository(Path.Combine(directory, "peticiones.db"));
            var pendingMarked = CreatePetition("11111111-1", marcada: true);
            var pendingUnmarked = CreatePetition("22222222-2", marcada: false);
            var sentMarked = CreatePetition("33333333-3", marcada: true, EstadoPeticion.Enviada);
            sentMarked.EnviadaEn = DateTimeOffset.Parse("2026-01-01T10:00:00+00:00");
            sentMarked.DestinatariosCorreo = "ya-enviado@ejemplo.cl";

            repository.AddIfNew(pendingMarked);
            repository.AddIfNew(pendingUnmarked);
            repository.AddIfNew(sentMarked);

            var sentAt = DateTimeOffset.Parse("2026-10-01T12:00:00+00:00");
            var count = repository.MarcarMarcadasComoEnviadasSinCorreo(sentAt);

            var records = repository.All().ToDictionary(p => p.Rut);
            Assert.Equal(1, count);
            Assert.Equal(EstadoPeticion.Enviada, records[pendingMarked.Rut].Estado);
            Assert.Equal(sentAt, records[pendingMarked.Rut].EnviadaEn);
            Assert.Null(records[pendingMarked.Rut].DestinatariosCorreo);
            Assert.Contains("sin enviar correo", records[pendingMarked.Rut].DetalleEstado,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(records[pendingMarked.Rut].Marcada);
            Assert.Equal(EstadoPeticion.Borrador, records[pendingUnmarked.Rut].Estado);
            Assert.Equal("ya-enviado@ejemplo.cl", records[sentMarked.Rut].DestinatariosCorreo);
        }
        finally
        {
            try
            {
                var dbPath = Path.Combine(directory, "peticiones.db");
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // El cierre de SQLite en Windows puede dejar el archivo bloqueado por el proceso;
                // el test valida la lógica del estado, no la limpieza del directorio temporal.
            }
        }
    }

    [Fact]
    public void MarcaTodasLasPendientesComoEnviadasSinCorreoYConservaLasYaEnviadas()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            using var repository = new PeticionRepository(Path.Combine(directory, "peticiones.db"));
            var pending = CreatePetition("44444444-4", marcada: false);
            var noEmail = CreatePetition("55555555-5", marcada: false, EstadoPeticion.SinCorreoComuna);
            var sent = CreatePetition("66666666-6", marcada: false, EstadoPeticion.Enviada);
            sent.DestinatariosCorreo = "ya-enviado@ejemplo.cl";

            repository.AddIfNew(pending);
            repository.AddIfNew(noEmail);
            repository.AddIfNew(sent);

            var sentAt = DateTimeOffset.Parse("2026-10-01T12:00:00+00:00");
            var count = repository.MarcarTodasComoEnviadasSinCorreo(sentAt);

            var records = repository.All().ToDictionary(p => p.Rut);
            Assert.Equal(2, count);
            Assert.Equal(EstadoPeticion.Enviada, records[pending.Rut].Estado);
            Assert.Equal(EstadoPeticion.Enviada, records[noEmail.Rut].Estado);
            Assert.Null(records[pending.Rut].DestinatariosCorreo);
            Assert.Null(records[noEmail.Rut].DestinatariosCorreo);
            Assert.Contains("sin enviar correo", records[pending.Rut].DetalleEstado,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal("ya-enviado@ejemplo.cl", records[sent.Rut].DestinatariosCorreo);
        }
        finally
        {
            try
            {
                var dbPath = Path.Combine(directory, "peticiones.db");
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // El cierre de SQLite en Windows puede dejar el archivo bloqueado por el proceso;
                // el test valida la lógica del estado, no la limpieza del directorio temporal.
            }
        }
    }

    private static Peticion CreatePetition(
        string rut, bool marcada, EstadoPeticion estado = EstadoPeticion.Borrador) => new()
    {
        NombreCompleto = $"Persona {rut}",
        Rut = rut,
        Comuna = "Valparaíso",
        Marcada = marcada,
        Estado = estado,
        CreadaEn = DateTimeOffset.Parse("2026-09-01T10:00:00+00:00"),
    };
}