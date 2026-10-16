using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Ews;

namespace PeticionCambioDomicilio.Tests;

public sealed class ReenviarCorreoTests
{
    private sealed class FakeMail : IMailSender
    {
        public List<(string To, string Subject, string Body)> Enviados { get; } = new();
        public bool IsConfigured => true;

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
        {
            Enviados.Add((toAddress, subject, body));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ReenviaUnCorreoPorComunaAlDestinatarioElegidoSinCambiarEstados()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "comunas.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Comuna,ContactEmail,Domain",
            "\"MAIPU\",\"maipu@ejemplo.cl\",\"ejemplo.cl\"",
            "\"PUENTE ALTO\",\"pa@ejemplo.cl\",\"ejemplo.cl\""));

        using var repository = new PeticionRepository(Path.Combine(dir, "peticiones.db"));
        var enviada = Crear("11111111-1", "MAIPU", EstadoPeticion.Enviada);
        enviada.EnviadaEn = DateTimeOffset.Parse("2026-10-01T10:00:00+00:00");
        enviada.DestinatariosCorreo = "maipu@ejemplo.cl";
        repository.AddIfNew(enviada);
        repository.AddIfNew(Crear("22222222-2", "MAIPU", EstadoPeticion.Borrador));
        repository.AddIfNew(Crear("33333333-3", "PUENTE ALTO", EstadoPeticion.Borrador));

        var mail = new FakeMail();
        var sender = new PeticionSender(repository, new ComunaDirectory(csv), mail, new AppOptions());
        var ids = repository.All().Select(p => p.Id).ToHashSet();

        var result = await sender.ReenviarAsync(ids, "jefa@ejemplo.cl", CancellationToken.None);

        Assert.Equal(EstadoPeticion.Enviada, result.Estado);
        Assert.Equal(3, result.Personas);
        Assert.Equal(2, mail.Enviados.Count);
        Assert.All(mail.Enviados, m => Assert.Equal("jefa@ejemplo.cl", m.To));
        Assert.All(mail.Enviados, m => Assert.StartsWith("[REENVÍO]", m.Subject));

        var porRut = repository.All().ToDictionary(p => p.Rut);
        Assert.Equal(EstadoPeticion.Enviada, porRut["11111111-1"].Estado);
        Assert.Equal("maipu@ejemplo.cl", porRut["11111111-1"].DestinatariosCorreo);
        Assert.Equal(EstadoPeticion.Borrador, porRut["22222222-2"].Estado);
        Assert.Null(porRut["22222222-2"].EnviadaEn);
    }

    [Fact]
    public async Task RechazaDestinatarioInvalido()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "comunas.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Comuna,ContactEmail,Domain",
            "\"MAIPU\",\"maipu@ejemplo.cl\",\"ejemplo.cl\""));
        using var repository = new PeticionRepository(Path.Combine(dir, "peticiones.db"));
        repository.AddIfNew(Crear("22222222-2", "MAIPU", EstadoPeticion.Borrador));
        var mail = new FakeMail();
        var sender = new PeticionSender(repository, new ComunaDirectory(csv), mail, new AppOptions());

        var result = await sender.ReenviarAsync(
            repository.All().Select(p => p.Id).ToHashSet(), "no-es-correo", CancellationToken.None);

        Assert.Equal(EstadoPeticion.Error, result.Estado);
        Assert.Empty(mail.Enviados);
    }

    private static Peticion Crear(string rut, string comuna, EstadoPeticion estado) => new()
    {
        NombreCompleto = "Persona " + rut,
        Rut = rut,
        Comuna = comuna,
        Origen = "Test",
        Oficina = "Test",
        Estado = estado,
    };
}
