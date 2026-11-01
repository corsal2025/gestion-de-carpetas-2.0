using PeticionCambioDomicilio.Domain;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.CambioDomicilio;

/// <summary>
/// «Pedimos carpetas · Pantallazos pendientes» and «Seguimiento». After the mail leaves, the user captures it in Outlook and pastes
/// the IMAGE once per mail; it covers every person of that mail and blocks nothing. The salientes in «Solicitado» wait for the other
/// comuna in order of deadline (business days from the citation, never restarted); the user checks them by hand in Conaset and marks
/// them uploaded, asks again by mail, or uploads the screenshot as proof. Writes go through the circuit's gate; the mail itself is
/// sent OUTSIDE it because the mail service takes the gate too (it is not reentrant).
/// </summary>
public sealed class SeguimientoService(
    ICarpetaRepository carpetas,
    ISeguimientoRepository consulta,
    IAlmacenPantallazos almacen,
    ICorreoComunas correo,
    CambioDomicilioOptions opciones,
    TimeProvider clock,
    UnidadDeTrabajoGate? gate = null)
{
    public const string MensajeNoEstaSolicitada = "La carpeta ya no está en seguimiento: actualice la lista.";
    public const string MensajeFaltaPantallazo =
        "Falta el pantallazo del correo: péguelo en «Pantallazos pendientes» antes de subir la prueba.";

    /// <summary>A deadline this many calendar days away (or fewer, down to the same day) is shown in yellow.</summary>
    public const int DiasAmarillo = 3;

    private readonly UnidadDeTrabajoGate _gate = gate ?? new UnidadDeTrabajoGate();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<SeguimientoDto>> ListarAsync(FiltroSeguimiento filtro = FiltroSeguimiento.Todas, CancellationToken ct = default)
    {
        var hoy = clock.HoyChile();
        var filas = await _gate.LeerAsync(() => consulta.ListarSolicitadasAsync(ct), ct);
        return filas
            .Select(f => ConPlazo(f, hoy))
            .Where(f => f.Cumple(filtro))
            .OrderBy(f => f.Vence)
            .ThenBy(f => f.NombreCompleto, StringComparer.CurrentCulture)
            .ThenBy(f => f.Id)
            .ToList();
    }

    public async Task<ResumenSeguimiento> ResumenAsync(CancellationToken ct = default)
    {
        var todas = await ListarAsync(ct: ct);
        return new ResumenSeguimiento(todas.Count, todas.Count(f => f.Vencida), await ContarPantallazosPendientesAsync(ct));
    }

    /// <summary>Carpetas whose requests were all TEST sends: nothing reached any comuna. Shown before switching to the real send.</summary>
    public async Task<int> ContarSoloPruebaAsync(CancellationToken ct = default) =>
        (await _gate.LeerAsync(() => consulta.ListarSolicitadasAsync(ct), ct)).Count(f => f.SoloPrueba);

    public Task<IReadOnlyList<PantallazoPendienteDto>> ListarPantallazosPendientesAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => consulta.ListarPantallazosPendientesAsync(ct), ct);

    public Task<int> ContarPantallazosPendientesAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => consulta.ContarPantallazosPendientesAsync(ct), ct);

    /// <summary>
    /// Stores the pasted image as the screenshot of that mail (and so of every carpeta it covers). A second image replaces the first.
    /// An image that is not PNG/JPEG or is too big is rejected before anything is written.
    /// </summary>
    public async Task AdjuntarPantallazoAsync(Guid envioId, Stream contenido, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        await _gate.WaitAsync(ct);
        try
        {
            var envio = await consulta.ObtenerEnvioAsync(envioId, ct) ?? throw new NotFoundException("El correo ya no existe.");
            if (envio.Estado != EstadoEnvioCorreoComuna.Enviado)
            {
                throw new DomainException("Solo se adjunta el pantallazo de un correo que ya se envió.");
            }

            var anterior = envio.PantallazoArchivo;
            var nuevo = await almacen.GuardarAsync(envioId, contenido, ct);
            try
            {
                envio.AdjuntarPantallazo(nuevo, Now);
                await consulta.SaveChangesAsync(ct);
            }
            catch
            {
                consulta.DescartarCambios();
                if (anterior != nuevo)
                {
                    almacen.Eliminar(envioId, nuevo);
                }

                throw;
            }

            if (anterior is not null && anterior != nuevo)
            {
                almacen.Eliminar(envioId, anterior);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The stored screenshot of a mail, or null when it has none or the file is gone. The caller disposes the stream.</summary>
    public async Task<PantallazoArchivoDto?> AbrirPantallazoAsync(Guid envioId, CancellationToken ct = default)
    {
        var archivo = await _gate.LeerAsync(() => consulta.ArchivoPantallazoAsync(envioId, ct), ct);
        if (archivo is null)
        {
            return null;
        }

        var contenido = await almacen.AbrirAsync(envioId, archivo, ct);
        return contenido is null ? null : new PantallazoArchivoDto(contenido, ContentTypeDe(archivo));
    }

    /// <summary>The other comuna already uploaded it to Conaset: «Cambio dom. subido a Conaset». Undo with <see cref="DeshacerAsync"/>.</summary>
    public Task<CambioRealizado> YaSubidaAsync(Guid id, string autor, CancellationToken ct = default) =>
        EscribirAsync(id, (c, now) => c.MarcarSubidaPorLaComuna(autor, now), ct);

    /// <summary>
    /// Mails the request again to the comuna (a reminder, by hand): one mail of type <see cref="TipoEnvioCorreoComuna.Reenvio"/> about
    /// this carpeta only. The history keeps the date; the deadline does not restart. A mail that failed leaves no trace in the carpeta.
    /// </summary>
    public async Task<ResultadoEnvioComuna> VolverAPedirAsync(Guid id, string autor, CancellationToken ct = default)
    {
        await _gate.LeerAsync(() => CargarSolicitadaAsync(id, ct), ct);

        // Outside the gate: the mail service takes it too.
        var envio = (await correo.EnviarAsync(TipoEnvioCorreoComuna.Reenvio, [id], autor, ct)).Single();
        if (envio.Resultado != ResultadoEnvio.Enviado)
        {
            return envio;
        }

        // Critical phase: the mail already left, so the note in the history is not cancelled halfway.
        await _gate.WaitAsync();
        try
        {
            var carpeta = await CargarAsync(id, CancellationToken.None);
            carpeta.RegistrarVueltaAPedir(envio.Comuna, autor, Now);
            await carpetas.SaveChangesAsync();
        }
        catch
        {
            carpetas.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }

        return envio;
    }

    /// <summary>
    /// The screenshot of the sent mail was uploaded to the platform as proof: «Cambio dom. subido con correo». It needs the screenshot
    /// of one of the mails of this carpeta. Undo with <see cref="DeshacerAsync"/>.
    /// </summary>
    public Task<CambioRealizado> SubirPruebaAsync(Guid id, string autor, CancellationToken ct = default) =>
        EscribirAsync(id, async (c, now) =>
        {
            if (await consulta.EnvioConPantallazoDeCarpetaAsync(id, ct) is null)
            {
                throw new DomainException(MensajeFaltaPantallazo);
            }

            c.MarcarSubidaConCorreo(autor, now);
        }, ct);

    /// <summary>Undoes <see cref="YaSubidaAsync"/> or <see cref="SubirPruebaAsync"/>; refused if the state changed again.</summary>
    public async Task DeshacerAsync(Guid id, CambioRealizado cambio, string autor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cambio);
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await CargarAsync(id, ct);
            carpeta.RestaurarEstado(cambio.Punto, cambio.EstadoNuevo, autor, Now);
            await carpetas.SaveChangesAsync(ct);
        }
        catch
        {
            carpetas.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private SeguimientoDto ConPlazo(SolicitadaDto f, DateOnly hoy)
    {
        var vence = DeadlineCalculator.AddBusinessDays(f.FechaCitacion, opciones.PlazoSalienteDias);
        var dias = vence.DayNumber - hoy.DayNumber;
        var color = dias < 0 ? PlazoColor.Rojo : dias <= DiasAmarillo ? PlazoColor.Amarillo : PlazoColor.Verde;
        return new SeguimientoDto(
            f.Id, f.Rut, f.NombreCompleto, f.Comuna, f.FechaCitacion, vence, dias, color, f.Envios, f.UltimoEnvioEn,
            f.SoloPrueba, f.EnvioPantallazoId, f.PantallazoDePrueba);
    }

    private async Task<Carpeta> CargarSolicitadaAsync(Guid id, CancellationToken ct)
    {
        var carpeta = await CargarAsync(id, ct);
        if (carpeta.Estado != EstadoCarpeta.CambioDomicilioSolicitado || carpeta.SentidoCambioDomicilio != SentidoCambioDomicilio.Saliente)
        {
            throw new DomainException(MensajeNoEstaSolicitada);
        }

        return carpeta;
    }

    private async Task<CambioRealizado> EscribirAsync(Guid id, Func<Carpeta, DateTime, Task> accion, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await CargarSolicitadaAsync(id, ct);
            var punto = carpeta.PuntoRestauracion();
            await accion(carpeta, Now);
            await carpetas.SaveChangesAsync(ct);
            return new CambioRealizado(punto, carpeta.Estado);
        }
        catch
        {
            carpetas.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<CambioRealizado> EscribirAsync(Guid id, Action<Carpeta, DateTime> accion, CancellationToken ct) =>
        EscribirAsync(id, (c, now) =>
        {
            accion(c, now);
            return Task.CompletedTask;
        }, ct);

    private async Task<Carpeta> CargarAsync(Guid id, CancellationToken ct) =>
        await carpetas.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");

    private static string ContentTypeDe(string archivo) =>
        archivo.EndsWith(".jpg", StringComparison.Ordinal) ? "image/jpeg" : "image/png";
}
