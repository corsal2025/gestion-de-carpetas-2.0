using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Ews;

namespace PeticionCambioDomicilio.Tests;

public sealed class PeticionSenderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly PeticionRepository _repo;
    private readonly ComunaDirectory _directory;

    public PeticionSenderTests()
    {
        Directory.CreateDirectory(_dir);
        var csv = Path.Combine(_dir, "comunas.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Comuna,ContactEmail,Domain",
            "\"MAIPU\",\"uno@maipu.cl\",\"maipu.cl\"",
            "\"MAIPU\",\"dos@maipu.cl\",\"maipu.cl\""));
        _repo = new PeticionRepository(Path.Combine(_dir, "peticiones.db"));
        _directory = new ComunaDirectory(csv);
    }

    public void Dispose()
    {
        _repo.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* archivo aún tomado por el proceso */ }
    }

    private sealed class FakeMail : IMailSender
    {
        public List<(string To, string Subject, bool PuedeCancelarse)> Enviados { get; } = new();
        public Func<Task>? Antes { get; set; }
        public Exception? Falla { get; set; }
        public bool IsConfigured => true;

        public async Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
        {
            lock (Enviados) { Enviados.Add((toAddress, subject, cancellationToken.CanBeCanceled)); }
            if (Antes is not null) { await Antes(); }
            if (Falla is not null) { throw Falla; }
        }
    }

    private PeticionSender Sender(FakeMail mail, string? modoPrueba = null) =>
        new(_repo, _directory, mail, new AppOptions { TestModeEmail = modoPrueba });

    private long Agregar(string rut, string estadoCarpeta = "CAMBIO DE DOMICILIO")
    {
        _repo.AddIfNew(new Peticion { NombreCompleto = "P " + rut, Rut = rut, Comuna = "MAIPU", EstadoCarpeta = estadoCarpeta });
        return _repo.All().Single(p => p.Rut == rut).Id;
    }

    [Fact]
    public async Task ModoPrueba_NoMarcaEnviadaNiCambiaEstadoDeCarpeta()
    {
        var id = Agregar("11111111-1");
        var mail = new FakeMail();

        var r = await Sender(mail, "prueba@interno.cl").SendComunaAsync("MAIPU", CancellationToken.None);

        Assert.Single(mail.Enviados);
        Assert.Equal("prueba@interno.cl", mail.Enviados[0].To);
        Assert.True(r.EsPrueba);
        Assert.NotEqual(EstadoPeticion.Enviada, r.Estado);
        var p = _repo.Get(id)!;
        Assert.Equal(EstadoPeticion.Borrador, p.Estado);
        Assert.Null(p.EnviadaEn);
        Assert.Equal("CAMBIO DE DOMICILIO", p.EstadoCarpeta);
    }

    [Fact]
    public async Task VariosDestinatarios_UnSoloCorreoConTodos()
    {
        var a = Agregar("11111111-1");
        var b = Agregar("22222222-2");
        var mail = new FakeMail();

        var r = await Sender(mail).SendComunaAsync("MAIPU", CancellationToken.None);

        Assert.Single(mail.Enviados);
        Assert.Contains("uno@maipu.cl", mail.Enviados[0].To);
        Assert.Contains("dos@maipu.cl", mail.Enviados[0].To);
        Assert.Equal(EstadoPeticion.Enviada, r.Estado);
        Assert.Equal(2, r.Personas);
        var pa = _repo.Get(a)!;
        Assert.Equal(EstadoPeticion.Enviada, pa.Estado);
        Assert.Equal("CAMBIO DE DOMICILIO SOLICITADO", pa.EstadoCarpeta);
        Assert.Equal(EstadoPeticion.Enviada, _repo.Get(b)!.Estado);
        Assert.False(mail.Enviados[0].PuedeCancelarse);
    }

    [Fact]
    public async Task DobleEnvioConcurrente_SoloSale_UnCorreo()
    {
        var id = Agregar("11111111-1");
        var abrir = new TaskCompletionSource();
        var mail = new FakeMail { Antes = () => abrir.Task };
        var sender = Sender(mail);

        var primero = sender.SendComunaAsync("MAIPU", CancellationToken.None);
        while (_repo.Get(id)!.Estado != EstadoPeticion.Enviando) { await Task.Delay(5); }
        var segundo = sender.SendComunaAsync("MAIPU", CancellationToken.None);
        abrir.SetResult();
        await Task.WhenAll(primero, segundo);

        Assert.Single(mail.Enviados);
        Assert.Equal(EstadoPeticion.Enviada, _repo.Get(id)!.Estado);
    }

    [Fact]
    public async Task ErrorEws_MarcaErrorYNoDejaEnviando()
    {
        var id = Agregar("11111111-1");
        var mail = new FakeMail { Falla = new EwsSendException("EWS caído") };

        var r = await Sender(mail).SendComunaAsync("MAIPU", CancellationToken.None);

        Assert.Equal(EstadoPeticion.Error, r.Estado);
        var p = _repo.Get(id)!;
        Assert.Equal(EstadoPeticion.Error, p.Estado);
        Assert.Null(p.EnviadaEn);
        Assert.Equal("CAMBIO DE DOMICILIO", p.EstadoCarpeta);
    }

    [Fact]
    public async Task CancelacionDuranteElEnvio_NoPropagaYMarcaError()
    {
        var id = Agregar("11111111-1");
        var mail = new FakeMail { Falla = new OperationCanceledException() };

        var r = await Sender(mail).SendComunaAsync("MAIPU", new CancellationTokenSource().Token);

        Assert.Equal(EstadoPeticion.Error, r.Estado);
        Assert.Equal(EstadoPeticion.Error, _repo.Get(id)!.Estado);
    }

    [Fact]
    public void EnviandoHuerfano_AlReabrirLaBaseQuedaEnErrorConAviso()
    {
        var id = Agregar("11111111-1");
        Assert.True(_repo.TryMarcarEnviando(id));
        Assert.False(_repo.TryMarcarEnviando(id));

        using var reabierto = new PeticionRepository(Path.Combine(_dir, "peticiones.db"));

        var p = reabierto.Get(id)!;
        Assert.Equal(EstadoPeticion.Error, p.Estado);
        Assert.Contains("interrump", p.DetalleEstado, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpsertManual_NoPisaUnaPeticionEnEnviando()
    {
        var id = Agregar("11111111-1");
        Assert.True(_repo.TryMarcarEnviando(id));

        var devuelto = _repo.UpsertManual(new Peticion { NombreCompleto = "Otro", Rut = "11111111-1", Comuna = "MAIPU", EstadoCarpeta = "CAMBIO DE DOMICILIO" });

        Assert.Equal(id, devuelto);
        Assert.Equal(EstadoPeticion.Enviando, _repo.Get(id)!.Estado);
        Assert.False(_repo.TryMarcarEnviando(id));
    }

    [Fact]
    public void UpsertManual_ReabreUnaPeticionConError()
    {
        var id = Agregar("11111111-1");
        _repo.TryMarcarEnviando(id);
        using (var reabierto = new PeticionRepository(Path.Combine(_dir, "peticiones.db")))
        {
            Assert.Equal(EstadoPeticion.Error, reabierto.Get(id)!.Estado);
            reabierto.UpsertManual(new Peticion { NombreCompleto = "Otro", Rut = "11111111-1", Comuna = "MAIPU", EstadoCarpeta = "CAMBIO DE DOMICILIO" });
            Assert.Equal(EstadoPeticion.Borrador, reabierto.Get(id)!.Estado);
        }
    }
}
