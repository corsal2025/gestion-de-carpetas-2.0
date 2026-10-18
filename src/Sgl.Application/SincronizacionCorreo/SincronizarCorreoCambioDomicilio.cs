using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.SincronizacionCorreo;

/// <summary>An email that could not be turned into a carpeta (no body kept: subject, sender and date only).</summary>
public sealed record CorreoDescartado(string Asunto, string Remitente, DateTimeOffset RecibidoEn, string Motivo);

/// <summary>Summary of one sync run.</summary>
public sealed record ResultadoSincronizacionCorreo(
    bool Configurado,
    string? Error,
    DateTime Fecha,
    int Leidos,
    int Nuevas,
    int YaExistentes,
    int ParaRevision,
    int MovidasASubidas,
    IReadOnlyList<CorreoDescartado> Descartados)
{
    public const string MensajeNoConfigurado = "Correo no configurado";

    public int Descartadas => Descartados.Count;

    public static ResultadoSincronizacionCorreo NoConfigurado(DateTime fecha) =>
        new(false, MensajeNoConfigurado, fecha, 0, 0, 0, 0, 0, []);
}

/// <summary>
/// Use case "Sincronizar correo" of the change-of-address module. Reads the folder of new requests
/// (<see cref="CorreoCambioDomicilioOptions.CarpetaParaPedir"/>) and creates one carpeta in
/// <see cref="EstadoCarpeta.CambioDomicilio"/> per valid RUT found; then reads the folder of uploaded requests
/// (<see cref="CorreoCambioDomicilioOptions.CarpetaYaSubidas"/>) and moves the matching carpetas to
/// <see cref="EstadoCarpeta.CambioDomSubidoCorreo"/>. Idempotent per (Internet Message-Id, RUT). Read-only on the
/// mailbox: nothing is sent, moved or flagged there.
/// </summary>
public sealed class SincronizarCorreoCambioDomicilio(
    IEmailReader lector,
    IComunaRemitente comunas,
    ICarpetaRepository carpetas,
    ICorreoCambioDomicilioRepository correos,
    CorreoCambioDomicilioOptions opciones,
    TimeProvider clock)
{
    /// <summary>Author recorded in the audit trail of every automatic change.</summary>
    public const string Autor = "Sistema · correo";

    private const int MaxNombreCompleto = Ciudadano.MaxNameLength;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public bool EstaConfigurado => lector.EstaConfigurado;

    public CorreoCambioDomicilioOptions Opciones => opciones;

    public async Task<ResultadoSincronizacionCorreo> EjecutarAsync(CancellationToken ct = default)
    {
        if (!lector.EstaConfigurado)
        {
            return ResultadoSincronizacionCorreo.NoConfigurado(Now);
        }

        var conteo = new Conteo();
        try
        {
            var nuevos = await lector.LeerCarpetaAsync(opciones.CarpetaParaPedir, ct);
            conteo.Leidos += nuevos.Count;
            foreach (var correo in nuevos)
            {
                await ProcesarSolicitudAsync(correo, conteo, ct);
            }

            var subidos = await lector.LeerCarpetaAsync(opciones.CarpetaYaSubidas, ct);
            conteo.Leidos += subidos.Count;
            foreach (var correo in subidos)
            {
                await ProcesarSubidoAsync(correo, conteo, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not DomainException)
        {
            return conteo.Resultado(Now, $"No se pudo leer el correo: {ex.Message}");
        }

        return conteo.Resultado(Now, null);
    }

    private async Task ProcesarSolicitudAsync(CorreoEntrante correo, Conteo conteo, CancellationToken ct)
    {
        var personas = Personas(correo);
        if (personas.Count == 0)
        {
            conteo.Descartar(correo, "No se encontró un RUT válido");
            return;
        }

        var comuna = comunas.ComunaDe(correo.Remitente);
        foreach (var (rut, nombre) in personas)
        {
            try
            {
                await CrearSiNoExisteAsync(correo, rut, nombre, comuna, conteo, ct);
            }
            catch (DomainException ex)
            {
                carpetas.DescartarCambios();
                conteo.Descartar(correo, $"RUT {rut.Formatted}: {ex.Message}");
            }
        }
    }

    private async Task CrearSiNoExisteAsync(
        CorreoEntrante correo, Rut rut, string? nombre, string? comuna, Conteo conteo, CancellationToken ct)
    {
        if (await correos.ExisteAsync(correo.MessageId, rut, ct))
        {
            conteo.YaExistentes++;
            return;
        }

        var now = Now;
        var ciudadano = await carpetas.FindCiudadanoAsync(rut, ct);
        if (ciudadano is not null && (await correos.CarpetasEnCambioDomicilioAsync(ciudadano.Id, ct)) is [var existente, ..])
        {
            // Same person already waiting in the tray (e.g. the comuna re-sent the request): link, never duplicate.
            await correos.AddAsync(new CorreoCambioDomicilio(correo.MessageId, rut, existente, now), ct);
            await carpetas.SaveChangesAsync(ct);
            conteo.YaExistentes++;
            return;
        }

        var revisarNombre = false;
        if (ciudadano is null)
        {
            revisarNombre = nombre is null || nombre.Length > MaxNombreCompleto;
            var (n, a) = revisarNombre ? (Ciudadano.SinNombre, Ciudadano.SinApellido) : Ciudadano.Separar(nombre);
            ciudadano = new Ciudadano(rut, n, a);
        }

        var fecha = HoraChile.Fecha(correo.RecibidoEn.UtcDateTime);
        var carpeta = Carpeta.RegistrarDesdeCorreo(ciudadano, opciones.Sede, fecha, comuna, revisarNombre, Autor, now);
        await carpetas.AddAsync(carpeta, ct);
        await correos.AddAsync(new CorreoCambioDomicilio(correo.MessageId, rut, carpeta.Id, now), ct);
        await carpetas.SaveChangesAsync(ct);
        conteo.Nuevas++;
        if (revisarNombre)
        {
            conteo.ParaRevision++;
        }
    }

    private async Task ProcesarSubidoAsync(CorreoEntrante correo, Conteo conteo, CancellationToken ct)
    {
        var ids = new HashSet<Guid>(await correos.CarpetasDeMensajeAsync(correo.MessageId, ct));
        foreach (var (rut, _) in Personas(correo))
        {
            if (await carpetas.FindCiudadanoAsync(rut, ct) is { } ciudadano)
            {
                ids.UnionWith(await correos.CarpetasEnCambioDomicilioAsync(ciudadano.Id, ct));
            }
        }

        foreach (var id in ids)
        {
            var carpeta = await carpetas.GetAsync(id, ct);
            if (carpeta is not { Estado: EstadoCarpeta.CambioDomicilio, EstaAbierta: true })
            {
                continue;
            }

            try
            {
                carpeta.CambiarEstado(EstadoCarpeta.CambioDomSubidoCorreo, Autor, Now);
                await carpetas.SaveChangesAsync(ct);
                conteo.MovidasASubidas++;
            }
            catch (DomainException)
            {
                // Cancelled or otherwise locked carpeta: left as it is for a person to look at.
                carpetas.DescartarCambios();
            }
        }
    }

    /// <summary>Valid RUTs of the email, body first, then the subject (subject RUTs never carry a name).</summary>
    private static List<(Rut Rut, string? Nombre)> Personas(CorreoEntrante correo)
    {
        var encontrados = PersonDataExtractor.ExtractAll(correo.Cuerpo ?? string.Empty)
            .Append(PersonDataExtractor.ExtractFromSubject(correo.Asunto ?? string.Empty));
        var personas = new List<(Rut Rut, string? Nombre)>();
        foreach (var p in encontrados)
        {
            if (Rut.TryParse(p.Rut, out var rut) && rut is not null && personas.All(x => x.Rut != rut))
            {
                personas.Add((rut, string.IsNullOrWhiteSpace(p.FullName) ? null : p.FullName.Trim()));
            }
        }
        return personas;
    }

    private sealed class Conteo
    {
        private readonly List<CorreoDescartado> _descartados = [];

        public int Leidos { get; set; }
        public int Nuevas { get; set; }
        public int YaExistentes { get; set; }
        public int ParaRevision { get; set; }
        public int MovidasASubidas { get; set; }

        public void Descartar(CorreoEntrante c, string motivo) =>
            _descartados.Add(new CorreoDescartado(c.Asunto ?? string.Empty, c.Remitente ?? string.Empty, c.RecibidoEn, motivo));

        public ResultadoSincronizacionCorreo Resultado(DateTime fecha, string? error) =>
            new(true, error, fecha, Leidos, Nuevas, YaExistentes, ParaRevision, MovidasASubidas, _descartados.ToList());
    }
}
