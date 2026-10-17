using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sgl.Application.Solicitudes;
using Sgl.Domain;
using Sgl.Infrastructure.Carpetas;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class FirmantesYMigracionTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Signers_default_to_empty()
    {
        var firmantes = await _db.NewFirmantesService().ObtenerAsync();

        Assert.Equal(Firmantes.Vacios, firmantes);
        Assert.Equal(string.Empty, firmantes.DirectorCorreo);
    }

    [Fact]
    public async Task Signers_are_saved_trimmed_and_overwritten_on_the_next_save()
    {
        await _db.NewFirmantesService().GuardarAsync(new Firmantes(" Ana Díaz ", "Director (s)", "ana@example.test", "Sara", "Secretaria"));
        var guardados = await _db.NewFirmantesService().ObtenerAsync();
        Assert.Equal("Ana Díaz", guardados.DirectorNombre);
        Assert.Equal("Director (s)", guardados.DirectorCargo);

        await _db.NewFirmantesService().GuardarAsync(new Firmantes("Luis Mora", "Director", "", "", ""));

        var nuevos = await _db.NewFirmantesService().ObtenerAsync();
        Assert.Equal("Luis Mora", nuevos.DirectorNombre);
        Assert.Equal("Director", nuevos.DirectorCargo);
        Assert.Equal(string.Empty, nuevos.DirectorCorreo);
        Assert.Equal(string.Empty, nuevos.SecretariaNombre);
    }

    [Theory]
    [InlineData("no-es-un-correo")]
    [InlineData("a@")]
    [InlineData("a b@example.test")]
    public async Task An_invalid_director_email_is_rejected(string correo)
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewFirmantesService().GuardarAsync(new Firmantes("Ana", "Director", correo, "Sara", "Secretaria")));
        Assert.Equal(Firmantes.Vacios, await _db.NewFirmantesService().ObtenerAsync());
    }

    [Fact]
    public async Task Overlong_signer_fields_are_rejected()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewFirmantesService().GuardarAsync(new Firmantes(new string('x', Firmantes.MaxLength + 1), "Director", "", "", "")));
    }

    [Fact]
    public async Task The_default_notifier_sends_nothing()
    {
        var notificador = new CertificadoSinCorreo(Microsoft.Extensions.Logging.Abstractions.NullLogger<CertificadoSinCorreo>.Instance);

        var enviado = await notificador.EnviarAsync(new NotificacionCertificado(
            Guid.NewGuid(), Guid.NewGuid(), TipoCertificado.Oficio, OrigenCertificado.Manual, "12.345.678-5", "JUAN",
            Sede.AvArgentina, "ana", DateTime.UtcNow, null, new Dictionary<string, string>(), Firmantes.Vacios));

        Assert.False(enviado);
    }

    [Fact]
    public void The_migrations_apply_on_an_empty_database_and_the_model_has_no_pending_changes()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var ctx = new SglDbContext(new DbContextOptionsBuilder<SglDbContext>().UseSqlite(connection).Options);

        ctx.Database.Migrate();

        Assert.False(ctx.Database.HasPendingModelChanges());
        foreach (var tabla in new[] { "SolicitudesF8", "SolicitudesCertificado", "Configuracion" })
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tabla}'";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }
    }

    [Fact]
    public async Task The_database_itself_refuses_a_second_open_request_for_the_same_carpeta()
    {
        var id = await SolicitudesTestSupport.CrearCarpetaAsync(_db, EstadoCarpeta.NoExisteCarpeta);
        await using var ctx = _db.NewContext();
        ctx.SolicitudesF8.Add(SolicitudF8.Solicitar(id, EstadoCarpeta.NoExisteCarpeta, "ana", DateTime.UtcNow));
        ctx.SolicitudesF8.Add(SolicitudF8.Solicitar(id, EstadoCarpeta.NoExisteCarpeta, "luis", DateTime.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
