using Microsoft.Extensions.Logging;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Ews;
using Sgl.Application.Solicitudes;
using Sgl.Domain;
using Sgl.Infrastructure.Carpetas;
using Sgl.Infrastructure.Documentos;
using static Sgl.Application.Tests.SolicitudesTestSupport;

namespace Sgl.Application.Tests;

public sealed class NotificadorCertificadoCorreoTests : IDisposable
{
    private const string CorreoDirector = "director.prueba@example.test";
    private const string CorreoPrueba = "buzon.prueba@example.test";

    private static readonly Firmantes Firma =
        new("Ana Directora Prueba", "Director", CorreoDirector, "Sara Secretaria Prueba", "Secretaria Municipal I.M.V");

    private readonly TestDb _db = new();
    private readonly MailFalso _mail = new();
    private readonly LogCapturado _log = new();

    public void Dispose() => _db.Dispose();

    private NotificadorCertificadoCorreo Crear(bool activo = true, string? modoPrueba = null, IMailSender? mail = null) =>
        new(
            mail ?? _mail,
            new AppOptions { TestModeEmail = modoPrueba },
            new CertificadosOptions { EnvioCorreoActivo = activo },
            new GeneradorDocumentoCertificado(_db.Clock),
            _log);

    private static NotificacionCertificado Notificacion(
        TipoCertificado tipo = TipoCertificado.Oficio, Firmantes? firmantes = null, Dictionary<string, string>? parametros = null) =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), tipo, OrigenCertificado.Manual, "12.345.678-5", "JUAN PÉREZ PRUEBA", Sede.AvArgentina,
            "ana", new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc), "310/2026",
            parametros ?? new Dictionary<string, string> { ["Clase"] = "B", ["Motivo"] = "motivo de prueba" },
            firmantes ?? Firma);

    [Fact]
    public async Task Flag_off_sends_nothing_and_returns_false()
    {
        var enviado = await Crear(activo: false).EnviarAsync(Notificacion());

        Assert.False(enviado);
        Assert.Empty(_mail.Enviados);
    }

    [Fact]
    public async Task No_director_mail_sends_nothing_and_returns_false()
    {
        var enviado = await Crear().EnviarAsync(Notificacion(firmantes: Firma with { DirectorCorreo = "" }));

        Assert.False(enviado);
        Assert.Empty(_mail.Enviados);
    }

    [Fact]
    public async Task Unconfigured_transport_sends_nothing_and_returns_false()
    {
        _mail.Configurado = false;

        Assert.False(await Crear().EnviarAsync(Notificacion()));
        Assert.Empty(_mail.Enviados);
    }

    [Fact]
    public async Task Flag_on_with_director_mail_sends_one_message_with_the_docx_attached()
    {
        var enviado = await Crear().EnviarAsync(Notificacion());

        Assert.True(enviado);
        var m = Assert.Single(_mail.Enviados);
        Assert.Equal(CorreoDirector, m.Para);
        Assert.Equal("Solicitud de certificado – Oficio", m.Asunto);
        Assert.Contains("JUAN PÉREZ PRUEBA", m.Cuerpo);
        Assert.Contains("12.345.678-5", m.Cuerpo);
        var adjunto = Assert.Single(m.Adjuntos);
        Assert.EndsWith(".docx", adjunto.Nombre);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(adjunto.Contenido, 0, 2));
    }

    [Theory]
    [InlineData(TipoCertificado.SinCarpeta, "sin carpeta", "cuya carpeta física no fue posible localizar")]
    [InlineData(TipoCertificado.RegistroCivil, "Registro Civil", "Registro Civil")]
    [InlineData(TipoCertificado.Oficio, "Oficio", "oficio")]
    public async Task Subject_and_body_follow_the_tipo(TipoCertificado tipo, string etiqueta, string frase)
    {
        var parametros = tipo switch
        {
            TipoCertificado.SinCarpeta => new Dictionary<string, string> { ["AnioRenovacion"] = "1995" },
            TipoCertificado.RegistroCivil => new Dictionary<string, string> { ["Clase"] = "A2", ["Ley"] = "19.495", ["FechaOtorgamiento"] = "08-02-2017" },
            _ => null,
        };

        await Crear().EnviarAsync(Notificacion(tipo, parametros: parametros));

        var m = Assert.Single(_mail.Enviados);
        Assert.Equal($"Solicitud de certificado – {etiqueta}", m.Asunto);
        Assert.Contains(frase, m.Cuerpo);
    }

    [Fact]
    public async Task When_the_document_cannot_be_built_yet_the_request_still_goes_without_attachment()
    {
        // A SinCarpeta certificate opened from an F8 has no renewal year until someone captures it.
        var enviado = await Crear().EnviarAsync(Notificacion(TipoCertificado.SinCarpeta, parametros: []));

        Assert.True(enviado);
        var m = Assert.Single(_mail.Enviados);
        Assert.Empty(m.Adjuntos);
        Assert.Contains("no se pudo adjuntar", m.Cuerpo);
    }

    [Fact]
    public async Task Test_mode_redirects_to_the_test_mailbox_marks_it_and_reports_not_sent()
    {
        var enviado = await Crear(modoPrueba: CorreoPrueba).EnviarAsync(Notificacion());

        Assert.False(enviado);
        var m = Assert.Single(_mail.Enviados);
        Assert.Equal(CorreoPrueba, m.Para);
        Assert.StartsWith("[PRUEBA]", m.Asunto);
        Assert.Contains("NO SE ENVIO AL DIRECTOR", m.Cuerpo);
        Assert.Contains(CorreoDirector, m.Cuerpo);
    }

    [Fact]
    public async Task A_transport_failure_surfaces_as_a_generic_exception_and_is_logged()
    {
        _mail.Falla = new EwsSendException("detalle interno con " + CorreoDirector);

        var ex = await Assert.ThrowsAsync<EnvioCorreoFallidoException>(() => Crear().EnviarAsync(Notificacion()));

        Assert.DoesNotContain(CorreoDirector, ex.Message);
        Assert.DoesNotContain("detalle interno", ex.Message);
        Assert.Contains(_log.Entradas, e => e.Nivel == LogLevel.Error && e.Excepcion is EwsSendException);
    }

    [Fact]
    public async Task Information_logs_never_carry_addresses_or_body_text()
    {
        await Crear().EnviarAsync(Notificacion());
        await Crear(activo: false).EnviarAsync(Notificacion());
        await Crear(modoPrueba: CorreoPrueba).EnviarAsync(Notificacion());

        foreach (var e in _log.Entradas.Where(e => e.Nivel <= LogLevel.Information))
        {
            Assert.DoesNotContain(CorreoDirector, e.Texto);
            Assert.DoesNotContain(CorreoPrueba, e.Texto);
            Assert.DoesNotContain("JUAN", e.Texto);
            Assert.DoesNotContain("12.345.678-5", e.Texto);
        }
    }

    // ---- Through the real service: the "sent" flag, the stored timestamp and the generic UI error ----

    [Fact]
    public async Task Through_the_service_a_real_send_sets_EnviadoADirectorEn()
    {
        await _db.NewFirmantesService().GuardarAsync(Firma);
        var carpeta = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);

        var creado = await _db.NewCertificadosService(Crear()).CrearManualAsync(new CrearCertificadoManualCommand(
            carpeta, TipoCertificado.Oficio, new Dictionary<string, string> { ["Clase"] = "B", ["Motivo"] = "x" }, "5/2026", "ana"));

        Assert.True(creado.EnviadoADirector);
        Assert.Null(creado.ErrorEnvio);
        Assert.Single(_mail.Enviados);
        var fila = (await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery())).Items.Single();
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, fila.EnviadoADirectorEn);
    }

    [Fact]
    public async Task Through_the_service_a_failing_sender_keeps_the_request_with_a_generic_error()
    {
        await _db.NewFirmantesService().GuardarAsync(Firma);
        _mail.Falla = new InvalidOperationException("secreto del transporte");
        var carpeta = await CrearCarpetaAsync(_db, EstadoCarpeta.CrearCertificado);

        var creado = await _db.NewCertificadosService(Crear()).CrearManualAsync(new CrearCertificadoManualCommand(
            carpeta, TipoCertificado.Oficio, new Dictionary<string, string> { ["Clase"] = "B", ["Motivo"] = "x" }, "5/2026", "ana"));

        Assert.False(creado.EnviadoADirector);
        Assert.NotNull(creado.ErrorEnvio);
        Assert.DoesNotContain("secreto", creado.ErrorEnvio);
        var fila = (await _db.NewCertificadosService().ListarAsync(new ListarCertificadosQuery())).Items.Single();
        Assert.Null(fila.EnviadoADirectorEn);
    }

    [Fact]
    public void Ews_request_carries_attachments_between_body_and_recipients()
    {
        var xml = EwsMailSender.BuildSendMailRequest(
            "a@example.test", "asunto", "cuerpo", null, [new AdjuntoCorreo("x.docx", "application/octet-stream", [1, 2, 3])]);
        var sin = EwsMailSender.BuildSendMailRequest("a@example.test", "asunto", "cuerpo", null);

        Assert.True(xml.IndexOf("<t:Body", StringComparison.Ordinal) < xml.IndexOf("<t:Attachments", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<t:Attachments", StringComparison.Ordinal) < xml.IndexOf("<t:ToRecipients", StringComparison.Ordinal));
        Assert.Contains("<t:Name>x.docx</t:Name>", xml);
        Assert.Contains($"<t:Content>{Convert.ToBase64String([1, 2, 3])}</t:Content>", xml);
        Assert.DoesNotContain("Attachments", sin);
    }

    // ---- Fakes ----

    private sealed record Mensaje(string Para, string Asunto, string Cuerpo, IReadOnlyList<AdjuntoCorreo> Adjuntos);

    private sealed class MailFalso : IMailSenderConAdjuntos
    {
        public bool Configurado { get; set; } = true;

        public Exception? Falla { get; set; }

        public List<Mensaje> Enviados { get; } = [];

        public bool IsConfigured => Configurado;

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken) =>
            SendAsync(toAddress, subject, body, [], cancellationToken);

        public Task SendAsync(string toAddress, string subject, string body, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken cancellationToken)
        {
            if (Falla is not null)
            {
                return Task.FromException(Falla);
            }

            Enviados.Add(new Mensaje(toAddress, subject, body, adjuntos));
            return Task.CompletedTask;
        }
    }

    private sealed record Entrada(LogLevel Nivel, string Texto, Exception? Excepcion);

    private sealed class LogCapturado : ILogger<NotificadorCertificadoCorreo>
    {
        public List<Entrada> Entradas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entradas.Add(new Entrada(logLevel, formatter(state, exception), exception));
    }
}
