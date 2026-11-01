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

    private static CorreoEntrante Correo(
        string id, string cuerpo, string asunto = "Solicitud carpeta", string remitente = Remitente, DateTimeOffset? recibido = null) =>
        new(id, asunto, remitente, cuerpo, recibido ?? new DateTimeOffset(2026, 5, 2, 15, 0, 0, TimeSpan.Zero));

    private List<Carpeta> Carpetas()
    {
        using var ctx = _db.NewContext();
        return ctx.Carpetas.Include(c => c.Ciudadano).Include(c => c.Historial).AsNoTracking().ToList();
    }

    /// <summary>A person confirmed in «Reclasificación» every carpeta that was waiting there (the closing mails only touch confirmed ones).</summary>
    private async Task ConfirmarPendientesAsync()
    {
        await using var ctx = _db.NewContext();
        foreach (var c in await ctx.Carpetas.Where(c => c.PendienteReclasificacion).ToListAsync())
        {
            c.ConfirmarReclasificacion("setup", _db.Clock.GetUtcNow().UtcDateTime);
        }
        await ctx.SaveChangesAsync();
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
        Assert.True(c.PendienteReclasificacion);
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

    // Reclasificación criterio 7: un correo «ya subida» no cierra una entrante que aún espera su confirmación.
    [Fact]
    public async Task YaSubidas_NoCierraUnaEntrantePendienteDeReclasificacion()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];
        await NewCaso().EjecutarAsync();

        _correo.Carpetas[_opciones.CarpetaParaPedir] = [];
        _correo.Carpetas[_opciones.CarpetaYaSubidas] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];
        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(0, r.MovidasASubidas);
        var c = Assert.Single(Carpetas());
        Assert.Equal(EstadoCarpeta.CambioDomicilio, c.Estado);
        Assert.True(c.PendienteReclasificacion);

        await ConfirmarPendientesAsync();
        var tras = await NewCaso().EjecutarAsync();

        Assert.Equal(1, tras.MovidasASubidas);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, Assert.Single(Carpetas()).Estado);
    }

    [Fact]
    public async Task YaSubidas_MueveLaEntranteACambioDomSubidoConaset()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];
        await NewCaso().EjecutarAsync();
        await ConfirmarPendientesAsync();

        // The operator files the same email (same Message-Id) in "CARP. YA SUBIDAS".
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [];
        _correo.Carpetas[_opciones.CarpetaYaSubidas] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];
        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(1, r.MovidasASubidas);
        var c = Assert.Single(Carpetas());
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, c.Estado);
        Assert.Equal(Bandeja.CambioDomSubidoConaset, c.Bandeja);
        Assert.Equal(SentidoCambioDomicilio.Entrante, c.SentidoCambioDomicilio);
        Assert.NotNull(c.FechaSubida);

        var otra = await NewCaso().EjecutarAsync();
        Assert.Equal(0, otra.MovidasASubidas);
    }

    [Fact]
    public async Task YaSubidas_PorRutDeOtroCorreo_TambienMueve()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];
        await NewCaso().EjecutarAsync();
        await ConfirmarPendientesAsync();

        _correo.Carpetas[_opciones.CarpetaParaPedir] = [];
        // The reply arrives after the carpeta was registered (clock: 2026-05-04 10:00 UTC).
        _correo.Carpetas[_opciones.CarpetaYaSubidas] =
            [Correo("<respuesta@x>", "Carpeta subida: RUT 18.785.387-7", recibido: new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero))];
        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(1, r.MovidasASubidas);
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, Assert.Single(Carpetas()).Estado);
    }

    private const string RutSaliente = "18785387-7";
    private static readonly DateTime Antes = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>A saliente of the agenda: classified as change of address and, when asked, already requested.</summary>
    private async Task<Guid> SembrarSalienteAsync(bool solicitada)
    {
        await using var ctx = _db.NewContext();
        var persona = new Ciudadano(Rut.Parse(RutSaliente), "PEDRO", "SOTO LARA");
        var c = Carpeta.CrearDesdeAgenda(persona, Sede.AvArgentina, new DateOnly(2026, 5, 1), new TimeOnly(9, 0), TramiteAgenda.Renovacion, "setup", Antes);
        c.Clasificar(EstadoCarpeta.CambioDomicilio, ClaseLicencia.B, null, "Viña del Mar", "setup", Antes);
        if (solicitada)
        {
            c.ConfirmarReclasificacion("setup", Antes);
            c.CambiarEstado(EstadoCarpeta.CambioDomicilioSolicitado, "setup", Antes);
        }
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    // E8 criterio 4
    [Fact]
    public async Task YaSubidas_LaSalienteSolicitadaQuedaEnSubidoAConasetYNuncaEnCorreo()
    {
        await SembrarSalienteAsync(solicitada: true);
        _correo.Carpetas[_opciones.CarpetaYaSubidas] = [Correo("<respuesta@x>", "Carpeta subida: RUT 18.785.387-7")];

        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(1, r.MovidasASubidas);
        var c = Assert.Single(Carpetas());
        Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, c.Estado);
        Assert.Equal(SentidoCambioDomicilio.Saliente, c.SentidoCambioDomicilio);
        Assert.NotEqual(EstadoCarpeta.CambioDomSubidoCorreo, c.Estado);
    }

    [Fact]
    public async Task YaSubidas_LaSalienteQueAunNoSePidioNoSeMueve()
    {
        await SembrarSalienteAsync(solicitada: false);
        _correo.Carpetas[_opciones.CarpetaYaSubidas] = [Correo("<respuesta@x>", "Carpeta subida: RUT 18.785.387-7")];

        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(0, r.MovidasASubidas);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, Assert.Single(Carpetas()).Estado);
    }

    // E8 criterio 5
    [Fact]
    public async Task ParaPedir_ConUnaSalienteAbiertaDelMismoRut_CreaUnaEntranteNuevaSinEnlazarla()
    {
        var saliente = await SembrarSalienteAsync(solicitada: false);
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "Se solicita la carpeta de PEDRO SOTO LARA RUN 18785387-7")];

        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(1, r.Nuevas);
        Assert.Equal(0, r.YaExistentes);
        var todas = Carpetas();
        Assert.Equal(2, todas.Count);
        var entrante = Assert.Single(todas, c => c.Id != saliente);
        Assert.Equal(SentidoCambioDomicilio.Entrante, entrante.SentidoCambioDomicilio);
        Assert.Equal(SentidoCambioDomicilio.Saliente, todas.Single(c => c.Id == saliente).SentidoCambioDomicilio);

        await using var ctx = _db.NewContext();
        var enlace = await ctx.CorreosCambioDomicilio.SingleAsync();
        Assert.Equal(entrante.Id, enlace.CarpetaId);
    }

    [Fact]
    public async Task YaSubidas_ConUnaSalienteYUnaEntranteDelMismoRut_CadaUnaSigueSuRegla()
    {
        var saliente = await SembrarSalienteAsync(solicitada: true);
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7", recibido: new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero))];
        await NewCaso().EjecutarAsync();
        await ConfirmarPendientesAsync();

        _correo.Carpetas[_opciones.CarpetaParaPedir] = [];
        _correo.Carpetas[_opciones.CarpetaYaSubidas] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7", recibido: new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero))];
        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(2, r.MovidasASubidas);
        Assert.All(Carpetas(), c => Assert.Equal(EstadoCarpeta.CambioDomSubidoConaset, c.Estado));
        Assert.Contains(Carpetas(), c => c.Id == saliente);
    }

    [Fact]
    public async Task YaSubidas_UnCorreoViejoPorRut_NoCierraUnaCarpetaRegistradaDespues()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7")];
        await NewCaso().EjecutarAsync();
        await ConfirmarPendientesAsync();

        // Another message (not the one that created the carpeta) from before the carpeta existed names the same RUT.
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [];
        _correo.Carpetas[_opciones.CarpetaYaSubidas] =
            [Correo("<viejo@x>", "Carpeta subida: RUT 18.785.387-7", recibido: new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero))];
        var r = await NewCaso().EjecutarAsync();

        Assert.Equal(0, r.MovidasASubidas);
        Assert.Equal(EstadoCarpeta.CambioDomicilio, Assert.Single(Carpetas()).Estado);
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
        Assert.Equal(SincronizarCorreoCambioDomicilio.ErrorGenerico, r.Error);
        Assert.DoesNotContain("sin red", r.Error);
    }

    // E13 criterio 5: el correo sin RUT válido queda en CorreosDescartados con su motivo; resincronizar no duplica.
    [Fact]
    public async Task SinRutValido_QuedaEnCorreosDescartadosConSuMotivoYResincronizarNoDuplica()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] =
        [
            Correo("<sin-rut@x>", "Hola, adjunto la solicitud.", "Sin datos"),
            Correo("<dv-malo@x>", "PEDRO SOTO LARA RUT 18.785.387-1", "Dígito malo"),
            Correo("<bueno@x>", "PEDRO SOTO LARA RUN 18785387-7", "Solicitud"),
        ];

        await NewCaso().EjecutarAsync();
        var segunda = await NewCaso().EjecutarAsync();
        await NewCaso().EjecutarAsync();

        Assert.Equal(2, segunda.Descartadas);
        await using var ctx = _db.NewContext();
        var filas = await ctx.CorreosDescartados.AsNoTracking().OrderBy(d => d.MessageId).ToListAsync();
        Assert.Equal(["<dv-malo@x>", "<sin-rut@x>"], filas.Select(f => f.MessageId));
        Assert.All(filas, f =>
        {
            Assert.Equal("No se encontró un RUT válido", f.Motivo);
            Assert.Equal(Remitente, f.Remitente);
            Assert.Equal(new DateTime(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc), f.DescartadoEn);
            Assert.Equal(new DateTime(2026, 5, 2, 15, 0, 0, DateTimeKind.Utc), f.RecibidoEn);
        });
        Assert.Equal(["Dígito malo", "Sin datos"], filas.Select(f => f.Asunto));
        Assert.Single(Carpetas());
    }

    [Fact]
    public async Task ElDescartadoGuardaAsuntoYRemitenteTruncadosYNuncaElCuerpo()
    {
        var asunto = new string('A', 400);
        var remitente = new string('r', 300) + "@munivina.cl";
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<largo@x>", "Texto privado del cuerpo sin ningún RUT", asunto, remitente)];

        await NewCaso().EjecutarAsync();

        await using var ctx = _db.NewContext();
        var fila = await ctx.CorreosDescartados.AsNoTracking().SingleAsync();
        Assert.Equal(CorreoDescartadoRegistro.MaxAsuntoLength, fila.Asunto.Length);
        Assert.Equal(CorreoDescartadoRegistro.MaxRemitenteLength, fila.Remitente.Length);
        Assert.DoesNotContain(typeof(CorreoDescartadoRegistro).GetProperties(), p => p.Name.Contains("Cuerpo", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Texto privado", fila.Motivo);
    }

    [Fact]
    public async Task UnCorreoQueSiCreaCarpetaNoQuedaEnDescartados()
    {
        _correo.Carpetas[_opciones.CarpetaParaPedir] = [Correo("<a@x>", "PEDRO SOTO LARA RUN 18785387-7", remitente: "alguien@gmail.com")];

        await NewCaso().EjecutarAsync();

        await using var ctx = _db.NewContext();
        Assert.Empty(ctx.CorreosDescartados);
        Assert.Single(Carpetas());
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
