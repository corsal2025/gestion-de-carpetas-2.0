using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Ews;
using Sgl.Application;
using Sgl.Application.CambioDomicilio;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Infrastructure.CambioDomicilio;

/// <summary>
/// Mails to the comunas over the circuit's DbContext. One mail per comuna; each attempt is recorded as an
/// <see cref="EnvioCorreoComuna"/> that is first reserved («Enviando») so a second click or operator does not mail again.
/// Safe by default: unless <c>CambioDomicilio:EnvioReal</c> is on (database value, then appsettings) the mail goes to the test
/// mailbox <c>Peticion:TestModeEmail</c> marked [PRUEBA], and with no mailbox nothing leaves the system («simulado»). A configured
/// test mailbox also overrides an active real send. Logs carry ids, counts and comuna names only: never RUT, names, bodies,
/// addresses or exception messages.
/// </summary>
public sealed class CorreoComunasService : ICorreoComunas
{
    public const string ClaveEnvioReal = "CambioDomicilio:EnvioReal";

    /// <summary>A reservation older than this was left by a crashed send: it stops blocking and is closed as failed.</summary>
    private static readonly TimeSpan VigenciaReserva = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Candados = new();

    private readonly SglDbContext _db;
    private readonly IMailSender _mail;
    private readonly ComunaDirectory _directorio;
    private readonly AppOptions _peticion;
    private readonly CambioDomicilioOptions _opciones;
    private readonly UnidadDeTrabajoGate _gate;
    private readonly TimeProvider _reloj;
    private readonly ILogger<CorreoComunasService> _logger;

    public CorreoComunasService(
        SglDbContext db, IMailSender mail, ComunaDirectory directorio, AppOptions peticion,
        CambioDomicilioOptions opciones, UnidadDeTrabajoGate gate, TimeProvider reloj, ILogger<CorreoComunasService> logger)
    {
        _db = db;
        _mail = mail;
        _directorio = directorio;
        _peticion = peticion;
        _opciones = opciones;
        _gate = gate;
        _reloj = reloj;
        _logger = logger;
    }

    private sealed record Persona(Guid Id, string? Comuna, string Nombre, string Rut, string? FechaUltimaCarpeta);

    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<ResultadoEnvioComuna>> EnviarAsync(
        TipoEnvioCorreoComuna tipo, IReadOnlyCollection<Guid> carpetaIds, string autor, CancellationToken ct = default)
    {
        if (tipo is not (TipoEnvioCorreoComuna.Pedido or TipoEnvioCorreoComuna.Reenvio))
        {
            throw new DomainException("Este envío solo admite el pedido de carpetas; las respuestas usan su plantilla.");
        }

        var ids = carpetaIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new DomainException("Elija al menos una carpeta para enviar.");
        }

        var personas = await CargarPersonasAsync(ids, ct);
        var resultados = new List<ResultadoEnvioComuna>();
        foreach (var grupo in personas
                     .GroupBy(p => NombreDeComuna(p.Comuna), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.CurrentCulture))
        {
            var lista = grupo.ToList();
            if (grupo.Key.Length == 0)
            {
                resultados.Add(Sin("(sin comuna)", ResultadoEnvio.SinComuna, lista, "Hay carpetas sin comuna: complete la comuna y vuelva a enviar."));
                continue;
            }

            resultados.Add(await EnviarGrupoAsync(tipo, grupo.Key, lista, autor, EmailTemplate.Subject(), CuerpoDelPedido, ct));
        }

        return resultados;
    }

    public async Task<ResultadoEnvioComuna> EnviarPlantillaAsync(SolicitudCorreoPlantilla solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var tipo = solicitud.Tipo;
        if (tipo is TipoEnvioCorreoComuna.Pedido or TipoEnvioCorreoComuna.Reenvio)
        {
            throw new DomainException("El pedido de carpetas no usa plantilla: se envía con el texto fijo.");
        }

        var persona = (await CargarPersonasAsync([solicitud.CarpetaId], ct)).Single();
        var comuna = NombreDeComuna(persona.Comuna);
        if (comuna.Length == 0)
        {
            return Sin("(sin comuna)", ResultadoEnvio.SinComuna, [persona], "La carpeta no tiene comuna: complétela y vuelva a enviar.");
        }

        var texto = await TextoDeLaPlantillaAsync(solicitud, persona, ct);
        return await EnviarGrupoAsync(tipo, comuna, [persona], solicitud.Autor, texto.Asunto, _ => texto.Cuerpo, ct);
    }

    public async Task<PlantillaCorreoDto> ObtenerPlantillaAsync(string clave, CancellationToken ct = default)
    {
        var porDefecto = PlantillasCambioDomicilio.PorDefecto(clave);
        var guardada = await _gate.LeerAsync(
            () => _db.PlantillasCorreo.AsNoTracking().SingleOrDefaultAsync(p => p.Clave == clave, ct), ct);
        return guardada is null ? porDefecto : new PlantillaCorreoDto(guardada.Clave, guardada.Asunto, guardada.Cuerpo);
    }

    public async Task GuardarPlantillaAsync(string clave, string asunto, string cuerpo, CancellationToken ct = default)
    {
        PlantillasCambioDomicilio.PorDefecto(clave);
        await _gate.WaitAsync(ct);
        try
        {
            var fila = await _db.PlantillasCorreo.SingleOrDefaultAsync(p => p.Clave == clave, ct);
            if (fila is null)
            {
                _db.PlantillasCorreo.Add(new PlantillaCorreo(clave, asunto, cuerpo));
            }
            else
            {
                fila.Actualizar(asunto, cuerpo);
            }

            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TextoCorreo> VistaPreviaAsync(
        string clave, string nombre, string rut, string? fecha, string? comunaSgl, CancellationToken ct = default) =>
        PlantillasCambioDomicilio.Renderizar(await ObtenerPlantillaAsync(clave, ct), nombre, rut, fecha, comunaSgl);

    public async Task<ModoEnvio> ObtenerModoAsync(CancellationToken ct = default)
    {
        var fila = await _gate.LeerAsync(
            () => _db.Configuracion.AsNoTracking().SingleOrDefaultAsync(i => i.Clave == ClaveEnvioReal, ct), ct);
        var real = fila is not null && bool.TryParse(fila.Valor, out var valor) ? valor : fila is null && _opciones.EnvioReal;
        return new ModoEnvio(real, !string.IsNullOrWhiteSpace(_peticion.TestModeEmail));
    }

    public async Task EstablecerEnvioRealAsync(bool activo, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var fila = await _db.Configuracion.SingleOrDefaultAsync(i => i.Clave == ClaveEnvioReal, ct);
            var valor = activo ? "true" : "false";
            if (fila is null)
            {
                _db.Configuracion.Add(new ConfiguracionItem { Clave = ClaveEnvioReal, Valor = valor });
            }
            else
            {
                fila.Valor = valor;
            }

            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            _gate.Release();
        }

        _logger.LogInformation("Envío real de correos a las comunas: {Estado}.", activo ? "activado" : "desactivado");
    }

    private async Task<TextoCorreo> TextoDeLaPlantillaAsync(SolicitudCorreoPlantilla solicitud, Persona persona, CancellationToken ct)
    {
        var clave = PlantillasCambioDomicilio.ClaveDe(solicitud.Tipo);
        if (clave is null)
        {
            if (string.IsNullOrWhiteSpace(solicitud.Asunto) || string.IsNullOrWhiteSpace(solicitud.Cuerpo))
            {
                throw new DomainException("Este correo necesita el asunto y el texto.");
            }

            return new TextoCorreo(solicitud.Asunto.Trim(), solicitud.Cuerpo.Trim());
        }

        var plantilla = await ObtenerPlantillaAsync(clave, ct);
        var fecha = string.IsNullOrWhiteSpace(solicitud.Fecha) ? FormatearFecha(persona.FechaUltimaCarpeta) : solicitud.Fecha;
        if (solicitud.Cuerpo is null && string.IsNullOrWhiteSpace(fecha) && plantilla.Cuerpo.Contains("{fecha}", StringComparison.Ordinal))
        {
            throw new DomainException("Falta la fecha de la última carpeta para armar el correo.");
        }

        var texto = PlantillasCambioDomicilio.Renderizar(plantilla, persona.Nombre, persona.Rut, fecha, solicitud.ComunaSgl);
        var asunto = string.IsNullOrWhiteSpace(solicitud.Asunto) ? texto.Asunto : solicitud.Asunto.Trim();
        var cuerpo = string.IsNullOrWhiteSpace(solicitud.Cuerpo) ? texto.Cuerpo : solicitud.Cuerpo.Trim();
        return new TextoCorreo(asunto, cuerpo);
    }

    private string CuerpoDelPedido(IReadOnlyList<Persona> personas) =>
        EmailTemplate.Body(
            personas.Select(p => new Peticion { NombreCompleto = p.Nombre, Rut = p.Rut, Comuna = NombreDeComuna(p.Comuna) }).ToList(),
            _peticion.MailboxAddress);

    private async Task<ResultadoEnvioComuna> EnviarGrupoAsync(
        TipoEnvioCorreoComuna tipo, string comuna, IReadOnlyList<Persona> personas, string autor,
        string asunto, Func<IReadOnlyList<Persona>, string> cuerpoPara, CancellationToken ct)
    {
        var destinatarios = _directorio.EmailsFor(comuna);
        if (destinatarios.Count == 0)
        {
            _logger.LogInformation("Comuna {Comuna}: sin correo en el directorio; {Carpetas} carpeta(s) sin enviar.", comuna, personas.Count);
            return Sin(comuna, ResultadoEnvio.SinCorreo, personas,
                $"{comuna}: sin correo en el directorio. Agréguelo en Comunas y vuelva a enviar.");
        }

        var modo = await ObtenerModoAsync(ct);
        var simulado = modo.EsPrueba && !modo.CasillaDePrueba;
        if (!simulado && !_mail.IsConfigured)
        {
            return Sin(comuna, ResultadoEnvio.CorreoNoConfigurado, personas,
                $"{comuna}: el correo no está configurado (Peticion:Ews); no se envió nada.");
        }

        var candado = Candados.GetOrAdd(TextNormalization.Fold(comuna), _ => new SemaphoreSlim(1, 1));
        await candado.WaitAsync(ct);
        try
        {
            var pendientes = await ReservarAsync(tipo, comuna, destinatarios, personas, autor, asunto, cuerpoPara, modo.EsPrueba, ct);
            if (pendientes is null)
            {
                _logger.LogInformation("Comuna {Comuna}: {Carpetas} carpeta(s) ya tenían su correo {Tipo}.", comuna, personas.Count, tipo);
                return Sin(comuna, ResultadoEnvio.YaEnviado, personas,
                    $"{comuna}: esas carpetas ya se enviaron o se están enviando.");
            }

            return await EnviarYRegistrarAsync(pendientes.Value.Envio, pendientes.Value.Personas, destinatarios, modo, simulado);
        }
        finally
        {
            candado.Release();
        }
    }

    /// <summary>
    /// Closes the reservations a crash left behind, drops the carpetas that already have a mail of this type and reserves the
    /// rest. Returns null when nothing is left to send. A test send blocks a new test send; a real one blocks everything.
    /// A reminder (<see cref="TipoEnvioCorreoComuna.Reenvio"/>) is only blocked by a send in progress.
    /// </summary>
    private async Task<(EnvioCorreoComuna Envio, IReadOnlyList<Persona> Personas)?> ReservarAsync(
        TipoEnvioCorreoComuna tipo, string comuna, IReadOnlyList<string> destinatarios, IReadOnlyList<Persona> personas,
        string autor, string asunto, Func<IReadOnlyList<Persona>, string> cuerpoPara, bool esPrueba, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var ahora = Ahora;
            var limite = ahora - VigenciaReserva;
            var vencidas = await _db.EnviosCorreoComuna
                .Where(e => e.Estado == EstadoEnvioCorreoComuna.Enviando && e.EnviadoEn < limite).ToListAsync(ct);
            foreach (var vencida in vencidas)
            {
                vencida.MarcarFallido("El envío quedó interrumpido; revise antes de reintentar.", ahora);
            }

            if (vencidas.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            var ids = personas.Select(p => p.Id).ToList();
            var previos = await (from l in _db.EnviosCorreoComunaCarpetas.AsNoTracking()
                                 join e in _db.EnviosCorreoComuna.AsNoTracking() on l.EnvioId equals e.Id
                                 where ids.Contains(l.CarpetaId) && e.Tipo == tipo
                                 select new { l.CarpetaId, e.Estado, e.EsPrueba }).ToListAsync(ct);
            var bloqueadas = previos
                .Where(p => p.Estado == EstadoEnvioCorreoComuna.Enviando
                            || (tipo != TipoEnvioCorreoComuna.Reenvio && p.Estado == EstadoEnvioCorreoComuna.Enviado && (!p.EsPrueba || esPrueba)))
                .Select(p => p.CarpetaId)
                .ToHashSet();
            var libres = personas.Where(p => !bloqueadas.Contains(p.Id)).ToList();
            if (libres.Count == 0)
            {
                return null;
            }

            var envio = EnvioCorreoComuna.Reservar(
                tipo, comuna, string.Join(", ", destinatarios), asunto, cuerpoPara(libres), autor, esPrueba, libres.Select(p => p.Id), ahora);
            _db.EnviosCorreoComuna.Add(envio);
            await _db.SaveChangesAsync(ct);
            return (envio, libres);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Critical phase: once reserved, the mail and the record of its result are not cancelled (<see cref="CancellationToken.None"/>).
    /// Cancelling between «the mail left» and «it was recorded» would leave the carpetas pending and the next pass would repeat it.
    /// </summary>
    private async Task<ResultadoEnvioComuna> EnviarYRegistrarAsync(
        EnvioCorreoComuna envio, IReadOnlyList<Persona> personas, IReadOnlyList<string> destinatarios, ModoEnvio modo, bool simulado)
    {
        string? error = null;
        if (!simulado)
        {
            try
            {
                if (modo.EsPrueba)
                {
                    await _mail.SendAsync(_peticion.TestModeEmail!.Trim(), $"[PRUEBA] {envio.Asunto}",
                        CabeceraDePrueba(envio.Comuna, destinatarios, personas.Count) + envio.Cuerpo, CancellationToken.None);
                }
                else
                {
                    await _mail.SendAsync(string.Join("; ", destinatarios), envio.Asunto, envio.Cuerpo, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                error = ex switch
                {
                    EwsAuthException => "Las credenciales del correo fueron rechazadas.",
                    OperationCanceledException => "El envío se interrumpió por tiempo de espera.",
                    _ => "No se pudo enviar el correo.",
                };
                _logger.LogWarning("Envío {EnvioId} a {Comuna} falló ({TipoError}).", envio.Id, envio.Comuna, ex.GetType().Name);
            }
        }

        await _gate.WaitAsync();
        try
        {
            if (error is null)
            {
                envio.MarcarEnviado(Ahora, simulado ? $"{EnvioCorreoComuna.PrefijoSimulado}: no hay casilla de prueba, no salió ningún correo" : null);
            }
            else
            {
                envio.MarcarFallido(error, Ahora);
            }

            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            _logger.LogError("Envío {EnvioId} a {Comuna}: no se pudo registrar el resultado.", envio.Id, envio.Comuna);
            _db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            _gate.Release();
        }

        var ids = personas.Select(p => p.Id).ToList();
        if (error is not null)
        {
            return new ResultadoEnvioComuna(envio.Comuna, ResultadoEnvio.Fallido, envio.Id, ids, modo.EsPrueba, false,
                $"{envio.Comuna}: falló el envío. {error} Se puede reintentar.");
        }

        _logger.LogInformation(
            "Envío {EnvioId} ({Tipo}) a {Comuna}: {Carpetas} carpeta(s), prueba={EsPrueba}, simulado={Simulado}.",
            envio.Id, envio.Tipo, envio.Comuna, ids.Count, modo.EsPrueba, simulado);
        var detalle = simulado ? " (simulado: no hay casilla de prueba, no salió ningún correo)"
            : modo.EsPrueba ? " (PRUEBA: fue a la casilla de prueba, no a la comuna)" : string.Empty;
        return new ResultadoEnvioComuna(envio.Comuna, ResultadoEnvio.Enviado, envio.Id, ids, modo.EsPrueba, simulado,
            $"{envio.Comuna}: {ids.Count} carpeta(s) en 1 correo{detalle}.");
    }

    private static string CabeceraDePrueba(string comuna, IReadOnlyList<string> destinatarios, int personas) =>
        string.Join("\n",
            "*** CORREO DE PRUEBA - NO SE ENVIO A LA MUNICIPALIDAD ***",
            $"Comuna destino real: {comuna}",
            $"Habria ido a: {string.Join(", ", destinatarios)}",
            $"Personas en este correo: {personas}",
            new string('-', 60),
            string.Empty,
            string.Empty);

    private async Task<List<Persona>> CargarPersonasAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var filas = await _gate.LeerAsync(() => _db.Carpetas.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Comuna, c.Ciudadano.Nombre, c.Ciudadano.Apellido, Rut = c.Ciudadano.Rut.Value, c.FechaUltimaCarpeta })
            .ToListAsync(ct), ct);
        if (filas.Count != ids.Distinct().Count())
        {
            throw new NotFoundException("Alguna de las carpetas ya no existe.");
        }

        var orden = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return filas
            .OrderBy(f => orden[f.Id])
            .Select(f => new Persona(
                f.Id, f.Comuna, $"{f.Nombre} {f.Apellido}".Trim(),
                Rut.TryParse(f.Rut, out var rut) ? rut!.Formatted : f.Rut,
                f.FechaUltimaCarpeta))
            .ToList();
    }

    /// <summary>Name of the comuna as the directory knows it (typos and accents resolved), or the trimmed text; empty if none.</summary>
    private string NombreDeComuna(string? comuna)
    {
        var texto = (comuna ?? string.Empty).Trim();
        return texto.Length == 0 ? string.Empty : _directorio.ResolveComunaName(texto) ?? texto;
    }

    private static string? FormatearFecha(string? fechaUltimaCarpeta) =>
        DateOnly.TryParseExact(fechaUltimaCarpeta, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)
            : fechaUltimaCarpeta;

    private static ResultadoEnvioComuna Sin(string comuna, ResultadoEnvio resultado, IEnumerable<Persona> personas, string mensaje) =>
        new(comuna, resultado, null, personas.Select(p => p.Id).ToList(), false, false, mensaje);
}
