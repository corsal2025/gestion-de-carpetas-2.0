using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Ews;

namespace PeticionCambioDomicilio.Domain;

public sealed record SendResult(EstadoPeticion Estado, string Mensaje, int Personas = 0);

/// <summary>
/// Envía las peticiones de cambio de domicilio. Regla central: <b>una comuna recibe un solo
/// correo por tanda</b>, con la lista de todas sus personas pendientes. Busca el/los correo(s)
/// de la comuna en el directorio, arma el correo con la plantilla del art. 14, lo manda por EWS
/// y actualiza el estado de cada petición del grupo.
/// </summary>
public sealed class PeticionSender
{
    private readonly PeticionRepository _repository;
    private readonly ComunaDirectory _directory;
    private readonly IMailSender _mail;
    private readonly AppOptions _options;

    public PeticionSender(
        PeticionRepository repository,
        ComunaDirectory directory,
        IMailSender mail,
        AppOptions options)
    {
        _repository = repository;
        _directory = directory;
        _mail = mail;
        _options = options;
    }

    private static bool EsPendiente(Peticion p) =>
        p.Estado is EstadoPeticion.Borrador or EstadoPeticion.SinCorreoComuna or EstadoPeticion.Error;

    /// <summary>Comunas con al menos una petición pendiente y sin correo en el directorio.</summary>
    public IReadOnlyList<string> ComunasPendientesSinCorreo() =>
        _repository.All()
            .Where(EsPendiente)
            .Select(p => p.Comuna)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(c => _directory.EmailsFor(c).Count == 0)
            .OrderBy(c => c, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>
    /// Envía UNA petición puntual en un correo de una sola persona. Solo lo usa el diagnóstico
    /// <c>--send-test</c>; la interfaz siempre agrupa por comuna con <see cref="SendComunaAsync"/>.
    /// </summary>
    public async Task<SendResult> SendAsync(long peticionId, CancellationToken cancellationToken)
    {
        var p = _repository.Get(peticionId);
        if (p is null)
        {
            return new SendResult(EstadoPeticion.Error, "La petición ya no existe.");
        }

        if (p.Estado == EstadoPeticion.Enviada)
        {
            return new SendResult(EstadoPeticion.Enviada, "Ya estaba enviada.");
        }

        return await SendGrupoAsync(p.Comuna, new[] { p }, cancellationToken);
    }

    /// <summary>
    /// Envía en UN correo las peticiones pendientes de una comuna (Borrador / Sin correo /
    /// Error). No toca las que ya están Enviadas. Devuelve el estado resultante del grupo.
    /// Si <paramref name="soloIds"/> viene, se limita a esas peticiones (envío de las marcadas);
    /// null = todas las pendientes de la comuna.
    /// </summary>
    public async Task<SendResult> SendComunaAsync(
        string comuna, CancellationToken cancellationToken, IReadOnlySet<long>? soloIds = null)
    {
        var pendientes = _repository.All()
            .Where(p => string.Equals(p.Comuna, comuna, StringComparison.OrdinalIgnoreCase))
            .Where(EsPendiente)
            .Where(p => soloIds is null || soloIds.Contains(p.Id))
            .OrderBy(p => p.OrdenImportacion).ThenBy(p => p.Id)
            .ToList();

        if (pendientes.Count == 0)
        {
            return new SendResult(EstadoPeticion.Enviada, $"{comuna}: no hay peticiones pendientes.");
        }

        return await SendGrupoAsync(comuna, pendientes, cancellationToken);
    }

    /// <summary>
    /// Reenvía una copia de las peticiones indicadas a un destinatario elegido por el operador
    /// (jefatura, la persona, otra oficina). UN correo por comuna, con la misma plantilla.
    /// NO cambia el estado de ninguna petición: es solo una copia informativa.
    /// </summary>
    public async Task<SendResult> ReenviarAsync(
        IReadOnlySet<long> ids, string destinatario, CancellationToken cancellationToken)
    {
        destinatario = (destinatario ?? "").Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(destinatario, out var addr) || addr.Address != destinatario)
        {
            return new SendResult(EstadoPeticion.Error, $"\"{destinatario}\" no es un correo válido.");
        }

        if (!_mail.IsConfigured)
        {
            return new SendResult(EstadoPeticion.Error,
                "El correo EWS no está configurado (Peticion:Ews en appsettings.Local.json).");
        }

        var grupos = _repository.All()
            .Where(p => ids.Contains(p.Id))
            .GroupBy(p => p.Comuna, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.CurrentCulture)
            .ToList();

        if (grupos.Count == 0)
        {
            return new SendResult(EstadoPeticion.Error, "No hay peticiones seleccionadas para reenviar.");
        }

        var personas = 0;
        try
        {
            foreach (var g in grupos)
            {
                var grupo = g.OrderBy(p => p.OrdenImportacion).ThenBy(p => p.Id).ToList();
                var subject = $"[REENVÍO] {EmailTemplate.Subject()} — {g.Key}";
                await _mail.SendAsync(destinatario, subject, EmailTemplate.Body(grupo, _options.MailboxAddress), cancellationToken);
                personas += grupo.Count;
            }
        }
        catch (Exception ex) when (ex is EwsAuthException or EwsSendException or HttpRequestException
                                       or System.Net.Sockets.SocketException or TaskCanceledException)
        {
            return new SendResult(EstadoPeticion.Error,
                $"Reenvío interrumpido tras {personas} persona(s): {ex.Message}", personas);
        }

        return new SendResult(EstadoPeticion.Enviada,
            $"Reenviado a {destinatario}: {grupos.Count} correo(s) con {personas} persona(s). Los estados no cambian.",
            personas);
    }

    private async Task<SendResult> SendGrupoAsync(
        string comuna, IReadOnlyList<Peticion> grupo, CancellationToken cancellationToken)
    {
        if (!_mail.IsConfigured)
        {
            return new SendResult(EstadoPeticion.Error,
                "El correo EWS no está configurado (Peticion:Ews en appsettings.Local.json).");
        }

        var destinatarios = _directory.EmailsFor(comuna);
        if (destinatarios.Count == 0)
        {
            foreach (var p in grupo)
            {
                _repository.UpdateEstado(p.Id, EstadoPeticion.SinCorreoComuna,
                    $"Sin correo registrado para la comuna \"{comuna}\".", null, null);
            }

            return new SendResult(EstadoPeticion.SinCorreoComuna,
                $"{comuna}: sin correo en el directorio. {grupo.Count} petición(es) en espera — " +
                "cargá el correo en la pantalla Comunas y volvé a enviar.");
        }

        var subject = EmailTemplate.Subject();
        var body = EmailTemplate.Body(grupo, _options.MailboxAddress);

        // MODO PRUEBA: nada sale hacia la municipalidad. Un solo correo a la casilla de prueba,
        // marcado, diciendo a dónde habría ido de verdad y a cuántas personas incluye.
        var modoPrueba = !string.IsNullOrWhiteSpace(_options.TestModeEmail);
        var envioA = destinatarios;
        if (modoPrueba)
        {
            subject = $"[PRUEBA] {subject}";
            var cabecera = new[]
            {
                "*** CORREO DE PRUEBA - NO SE ENVIO A LA MUNICIPALIDAD ***",
                $"Comuna destino real: {comuna}",
                $"Habria ido a: {string.Join(", ", destinatarios)}",
                $"Personas en este correo: {grupo.Count}",
                new string('-', 60),
                string.Empty,
                string.Empty,
            };
            body = string.Join("\n", cabecera) + body;
            envioA = new[] { _options.TestModeEmail!.Trim() };
        }

        try
        {
            foreach (var to in envioA)
            {
                await _mail.SendAsync(to, subject, body, cancellationToken);
            }
        }
        catch (EwsAuthException ex)
        {
            MarcarGrupo(grupo, EstadoPeticion.Error, ex.Message, null, null);
            return new SendResult(EstadoPeticion.Error, $"{comuna}: {ex.Message}");
        }
        catch (Exception ex) when (ex is EwsSendException or HttpRequestException
                                       or System.Net.Sockets.SocketException or TaskCanceledException)
        {
            MarcarGrupo(grupo, EstadoPeticion.Error, ex.Message, null, null);
            return new SendResult(EstadoPeticion.Error, $"{comuna}: falló el envío — {ex.Message}");
        }

        var joined = string.Join(", ", envioA);
        var ahora = DateTimeOffset.Now;
        var detalle = modoPrueba
            ? $"MODO PRUEBA — desviada desde {string.Join(", ", destinatarios)} · {grupo.Count} persona(s)"
            : $"1 correo con {grupo.Count} persona(s) de {comuna}";

        foreach (var p in grupo)
        {
            _repository.UpdateEstado(p.Id, EstadoPeticion.Enviada, detalle, ahora, joined);

            // Al enviar, la carpeta pasa de "CAMBIO DE DOMICILIO" a "... SOLICITADO" automaticamente.
            // Si el operador ya la habia movido a otro estado, no se pisa.
            if (p.EstadoCarpeta == EstadoCarpetaCatalog.CambioDeDomicilio)
            {
                _repository.SetEstadoCarpeta(p.Id, "CAMBIO DE DOMICILIO SOLICITADO");
            }
        }

        return new SendResult(EstadoPeticion.Enviada,
            modoPrueba
                ? $"[PRUEBA] {comuna}: {grupo.Count} persona(s) en 1 correo a {joined} (habría ido a la comuna)."
                : $"{comuna}: {grupo.Count} persona(s) enviadas en 1 correo a {joined}.",
            grupo.Count);
    }

    private void MarcarGrupo(
        IReadOnlyList<Peticion> grupo, EstadoPeticion estado, string? detalle,
        DateTimeOffset? enviadaEn, string? destinatarios)
    {
        foreach (var p in grupo)
        {
            _repository.UpdateEstado(p.Id, estado, detalle, enviadaEn, destinatarios);
        }
    }
}
