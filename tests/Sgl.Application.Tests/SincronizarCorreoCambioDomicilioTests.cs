using Microsoft.EntityFrameworkCore;
using Sgl.Application.SincronizacionCorreo;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class SincronizarCorreoCambioDomicilioTests : IDisposable
{
    private const string Remitente = "partes@munivina.cl";
    private readonly TestDb _db = new();
    private readonly FakeEmailReader _correo = new();
    private readonly CorreoCambioDomicilioOptions _opciones = new();

    private SincronizarCorreoCambioDomicilio NewCaso()
    {
        var ctx = _db.NewContext();
        return new(
            _correo,
            new FakeComunas(),
            new CarpetaRepository(ctx),
            new CorreoCambioDomicilioRepository(ctx),
            _opciones,
            _db.Clock);
    }

    private static CorreoEntrante Correo(string id, string cuerpo, string asunto = "Solicitud carpeta", string remitente = Remitente) =>
        new(id, asunto, remitente, cuerpo, new DateTimeOffset(2026, 5, 2, 15, 0, 0, TimeSpan.Zero));

    private List<Carpeta> Carpetas()
    {
        using var ctx = _db.NewContext();
        return ctx.Carpetas.Include(c => c.Ciudadano).Include(c => c.Historial).AsNoTracking().ToList();
    }

    [Fact]
    public async Task NoConfigurado_DevuelveCorreoNoConfiguradoYNoHaceNada()
    {
        _correo.Configurado = false;
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "GUSTAVO PEÑA CASTRO RUT: 18.785.387-7")];

        var r = await NewCaso().EjecutarAsync();

        Assert.False(r.Configurado);
        Assert.Equal(ResultadoSincronizacionCorreo.MensajeNoConfigurado, r.Error);
        Assert.Equal(0, _correo.Lecturas);
        Assert.Empty(Carpetas());
    }

    [Fact]
    public async Task CorreoValido_CreaCarpetaEnCambioDomicilioConComunaYFechaDeRecepcion()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "Se solicita la carpeta de GUSTAVO ANDRÉS PEÑA CASTRO RUT: 18.785.387-7.")];

        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(1, r.Nuevas);
        Assert.Equal(0, r.ParaRevision);
        var c = Assert.Single(Carpetas());
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.Equal(Bandeja.CambioDomicilio, c.Bandeja);
        Assert.Equal(ComunasChile.Normalizar("Viña del Mar"), c.Comuna);
        Assert.NotNull(c.Comuna);
        Assert.Equal(new DateOnly(2026, 5, 2), c.FechaCitacion);
        Assert.Equal("18785387-7", c.Ciudadano.Rut.Value);
        Assert.Equal("GUSTAVO ANDRÉS", c.Ciudadano.Nombre);
        Assert.Equal("PEÑA CASTRO", c.Ciudadano.Apellido);
        Assert.Null(c.IdoneidadMoral);
        Assert.All(c.Historial, h => Assert.Equal(SincronizarCorreoCambioDomicilio.Autor, h.Usuario));
    }

    [Fact]
    public async Task Resincronizar_EsIdempotentePorMessageId()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];

        await NewCaso().EjecutarAsync();
        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(0, r.Nuevas);
        Assert.Equal(1, r.YaExistentes);
        Assert.Single(Carpetas());
    }

    [Fact]
    public async Task OtroCorreoMismaPersonaYaEnBandeja_NoDuplica()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] =
        [
            Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7"),
            Correo("<b@x>", "Reenvío: PEDRO SOTO LARA RUN 18.785.387-7"),
        ];

        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(1, r.Nuevas);
        Assert.Equal(1, r.YaExistentes);
        Assert.Single(Carpetas());
    }

    [Fact]
    public async Task VariasPersonasEnUnCorreo_CreaUnaCarpetaPorRut()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] =
            [Correo("<a@x>", "PEDRO SOTO LARA RUT 18.785.387-7\nMARIA-ANTONIA RECULE RIVERA RUT 18.586.259-3")];

        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(2, r.Nuevas);
        Assert.Equal(2, Carpetas().Count);
    }

    [Fact]
    public async Task SinRutOConRutInvalido_SeDescartaSinCuerpo()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] =
        [
            Correo("<a@x>", "Hola, adjunto la solicitud.", "Sin datos"),
            Correo("<b@x>", "PEDRO SOTO LARA RUT 18.785.387-1", "Dígito malo"),
        ];

        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(2, r.Descartadas);
        Assert.Equal(0, r.Nuevas);
        Assert.Empty(Carpetas());
        Assert.Equal(["Sin datos", "Dígito malo"], r.Descartados.Select(d => d.Asunto));
        Assert.All(r.Descartados, d => Assert.Equal(Remitente, d.Remitente));
    }

    [Fact]
    public async Task NombreIncierto_CreaConObservacionDeRevision()
    {
        // RUT only in the subject: the name is never trusted from there.
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "Adjunto antecedentes.", "RV: SOLICITUD ANTECEDENTES RUN 18.785.387-7")];

        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(1, r.Nuevas);
        Assert.Equal(1, r.ParaRevision);
        var c = Assert.Single(Carpetas());
        Assert.Equal(Carpeta.ObservacionRevisarNombreCorreo, c.IdoneidadMoral);
        Assert.Equal(Ciudadano.SinNombre, c.Ciudadano.Nombre);
    }

    [Fact]
    public async Task RemitenteDesconocido_CreaSinComuna()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7", remitente: "alguien@gmail.com")];

        await NewCaso().EjecutarAsync();

        Assert.Null(Assert.Single(Carpetas()).Comuna);
    }

    [Fact]
    public async Task YaSubidas_MueveLaCarpetaACambioDomSubidoCorreo()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];
        await NewCaso().EjecutarAsync();

        // The operator files the same email (same Message-Id) in "CARP. YA SUBIDAS".
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [];
        _correo.Carpetas[_opciones.CarpetaYaSubidas] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];
        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(1, r.MovidasASubidas);
        var c = Assert.Single(Carpetas());
        Assert.Equal(EstadoCarpeta.CambioDomSubidoCorreo, c.Estado);
        Assert.Equal(Bandeja.CambioDomSubidoCorreo, c.Bandeja);

        var otra = await NewCaso().EjecutarAsync();
        Assert.Equal(0, otra.MovidasASubidas);
    }

    [Fact]
    public async Task YaSubidas_PorRutDeOtroCorreo_TambienMueve()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];
        await NewCaso().EjecutarAsync();

        _correo.Carpetas[_opciones.CarpetaParaPedir] = [];
        _correo.Carpetas[_opciones.CarpetaYaSubidas] = [Correo("<respuesta@x>", "Carpeta subida: RUT 18.785.387-7")];
        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(1, r.MovidasASubidas);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoCorreo, Assert.Single(Carpetas()).Estado);
    }

    [Fact]
    public async Task YaSubidas_NuncaCreaCarpetas()
    {
        _correo.Carpetas[_opciones.CarpetaYaSubidas] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];

        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(0, r.Nuevas);
        Assert.Equal(0, r.MovidasASubidas);
        Assert.Empty(Carpetas());
    }

    [Fact]
    public async Task FalloDelBuzon_DevuelveErrorSinExcepcion()
    {
        _correo.Falla = true;

        var r = await NewCaso().EjecutarAsync();

        Assert.True(r.Configurado);
        Assert.StartsWith("No se pudo leer el correo", r.Error);
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeEmailReader : IEmailReader
    {
        public bool Configurado { get; set; } = true;
        public bool Falla { get; set; }
        public int Lecturas { get; private set; }
        public Dictionary<string, IReadOnlyList<CorreoEntrante>> Carpetas { get; } = [];

        public bool EstaConfigurado => Configurado;

        public Task<IReadOnlyList<CorreoEntrante>> LeerCarpetaAsync(string carpeta, CancellationToken ct = default)
        {
            Lecturas++;
            if (Falla)
            {
                throw new HttpRequestException("sin red");
            }
            return Task.FromResult(Carpetas.GetValueOrDefault(carpeta) ?? []);
        }
    }

    private sealed class FakeComunas : IComunaRemitente
    {
        public string? ComunaDe(string remitente) =>
            remitente.EndsWith("@munivina.cl", StringComparison.OrdinalIgnoreCase) ? "VIÑA DEL MAR" : null;
    }
}
