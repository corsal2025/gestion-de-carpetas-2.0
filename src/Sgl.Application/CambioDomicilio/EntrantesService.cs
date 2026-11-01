using System.Globalization;
using PeticionCambioDomicilio.Domain;
using Sgl.Application.Carpetas;
using Sgl.Application.Solicitudes;
using Sgl.Domain;

namespace Sgl.Application.CambioDomicilio;

/// <summary>
/// «Nos piden carpetas»: other comunas ask us (by email) for the carpeta of someone who lived here. A person looks up the last-carpeta
/// date in SGL / CAS Chile and types it: it decides the sector (Archivo, Of. 43 or Digital). From there the magnifier follows the search
/// (no mail) and «Subida a Conaset» closes it as «Cambio dom. subido a Conaset». When the carpeta is not found there are three exits:
/// A (the person is in our base but the paper is missing: F8), B (no record: mail «sin registro», after offering to correct the
/// classification) and C (digital: mail «carpeta digital»). «Ya se subió» is a manual confirmation mail. Every mail goes through
/// <see cref="ICorreoComunas"/> (test mode by default). Writes go through the circuit's gate; mails are sent OUTSIDE it because the
/// mail service takes it too (the gate is not reentrant).
/// </summary>
public sealed class EntrantesService(
    ICarpetaRepository carpetas,
    IEntrantesRepository consulta,
    ISolicitudF8Repository solicitudes,
    ICorreoComunas correo,
    CambioDomicilioOptions opciones,
    TimeProvider clock,
    UnidadDeTrabajoGate? gate = null)
{
    public const string MensajeNoEstaAbierta = "La solicitud ya no está abierta en «Nos piden carpetas»: actualice la lista.";

    /// <summary>How many closed requests the «Cerradas» tab shows (the latest ones).</summary>
    public const int MaxCerradas = 200;

    /// <summary>Classifications a person can correct an incoming request to (the other exits are the A / B / C buttons).</summary>
    public static IReadOnlyList<EstadoCarpeta> DestinosCorreccion => Carpeta.DestinosCorreccionEntrante;

    private readonly UnidadDeTrabajoGate _gate = gate ?? new UnidadDeTrabajoGate();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Template key of each mail we answer a comuna with (same keys the template editor stores).</summary>
    public static string ClaveDe(TipoEnvioCorreoComuna tipo) => tipo switch
    {
        TipoEnvioCorreoComuna.SinRegistro => "SinRegistro",
        TipoEnvioCorreoComuna.Digital => "Digital",
        TipoEnvioCorreoComuna.ConfirmacionSubida => "ConfirmacionSubida",
        _ => throw new DomainException("Este correo no es una respuesta a una solicitud de carpeta."),
    };

    // ---- Lectura ----

    public async Task<IReadOnlyList<EntranteDto>> ListarAsync(PestanaEntrantes pestana, CancellationToken ct = default)
    {
        var hoy = clock.HoyChile();
        var filas = pestana == PestanaEntrantes.Cerradas
            ? await _gate.LeerAsync(() => consulta.ListarCerradasAsync(MaxCerradas, ct), ct)
            : await _gate.LeerAsync(() => consulta.ListarAbiertasAsync(ct), ct);
        return filas.Select(f => ConPlazo(f, hoy)).Where(f => f.Pestana == pestana).ToList();
    }

    public async Task<ResumenEntrantes> ResumenAsync(CancellationToken ct = default)
    {
        var hoy = clock.HoyChile();
        var abiertas = (await _gate.LeerAsync(() => consulta.ListarAbiertasAsync(ct), ct)).Select(f => ConPlazo(f, hoy)).ToList();
        var cerradas = await _gate.LeerAsync(() => consulta.ContarCerradasAsync(ct), ct);
        var descartados = await _gate.LeerAsync(() => consulta.ContarDescartadosAsync(ct), ct);
        return new ResumenEntrantes(
            abiertas.Count(f => f.Pestana == PestanaEntrantes.PorClasificar),
            abiertas.Count(f => f.Pestana == PestanaEntrantes.Archivo),
            abiertas.Count(f => f.Pestana == PestanaEntrantes.Oficina43),
            abiertas.Count(f => f.Pestana == PestanaEntrantes.Digital),
            Math.Min(cerradas, MaxCerradas),
            descartados);
    }

    /// <summary>Open requests still without a last-carpeta date (menu counter).</summary>
    public async Task<int> ContarPorClasificarAsync(CancellationToken ct = default) =>
        (await ListarAsync(PestanaEntrantes.PorClasificar, ct)).Count;

    public async Task<EntranteDto> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var fila = await _gate.LeerAsync(() => consulta.ObtenerAsync(id, ct), ct)
            ?? throw new NotFoundException("La solicitud ya no existe: actualice la lista.");
        return ConPlazo(fila, clock.HoyChile());
    }

    public Task<IReadOnlyList<DescartadoDto>> ListarDescartadosAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => consulta.ListarDescartadosAsync(ct), ct);

    public async Task<bool> QuitarDescartadoAsync(long id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await consulta.QuitarDescartadoAsync(id, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- Clasificar y buscar ----

    /// <summary>Types (or corrects) the last-carpeta date; returns the resulting sector.</summary>
    public Task<string> ClasificarAsync(Guid id, string? fecha, string autor, CancellationToken ct = default) =>
        EscribirAsync(id, (c, now) =>
        {
            c.ClasificarEntrante(fecha, autor, now);
            return c.SectorPorFecha;
        }, ct);

    /// <summary>One touch of the magnifier (sin marcar → en búsqueda → encontrada → sin marcar). Never sends or creates anything.</summary>
    public Task AvanzarLupaAsync(Guid id, string autor, CancellationToken ct = default) =>
        EscribirAsync(id, (c, now) =>
        {
            c.AvanzarLupaEntrante(autor, now);
            return true;
        }, ct);

    /// <summary>«Subida a Conaset»: the carpeta was found and uploaded. State «Cambio dom. subido a Conaset» (never «Subida a Conaset»).</summary>
    public async Task<CambioRealizado> SubidaConasetAsync(Guid id, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await CargarAsync(id, ct);
            var punto = carpeta.PuntoRestauracion();
            carpeta.MarcarSubidaEntrante(autor, Now);
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

    /// <summary>
    /// Exit A: the person is in our base but the physical carpeta does not show up. The carpeta goes to «No existe carpeta» and the F8
    /// request is opened in the same save; from there the existing F8 → certificate flow applies. Returns the F8 request id.
    /// </summary>
    public async Task<Guid> NoEstaLaCarpetaAsync(Guid id, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await CargarAsync(id, ct);
            var now = Now;
            carpeta.MarcarNoEncontradaEntrante(autor, now);
            var f8 = await SolicitudF8Creacion.SolicitarAsync(solicitudes, carpeta.Id, carpeta.Estado, carpeta.SentidoCambioDomicilio, autor, now, ct);
            if (f8.Creada)
            {
                carpeta.RegistrarEvento("F8", null, "Solicitado", autor, now);
            }

            await carpetas.SaveChangesAsync(ct);
            return f8.SolicitudId;
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

    /// <summary>It was not an incoming change of address (e.g. a first license): the user picks the right classification and the carpeta moves there.</summary>
    public async Task<CambioRealizado> CorregirClasificacionAsync(
        Guid id, EstadoCarpeta destino, string motivo, string autor, CancellationToken ct = default)
    {
        if (!DestinosCorreccion.Contains(destino))
        {
            throw new DomainException("Elija una clasificación válida: 1° licencia o canje de licencia extranjera.");
        }

        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await CargarAsync(id, ct);
            var punto = carpeta.PuntoRestauracion();
            carpeta.CorregirClasificacionCambioDom(destino, motivo, null, autor, Now);
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

    /// <summary>Undoes <see cref="SubidaConasetAsync"/> or <see cref="CorregirClasificacionAsync"/>; refused if the state changed again.</summary>
    public async Task DeshacerAsync(Guid id, CambioRealizado cambio, string autor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cambio);
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await carpetas.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");
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

    // ---- Correos a la comuna ----

    /// <summary>The mail as it would go out (template filled in), to be edited before sending. <paramref name="comunaSgl"/> only applies to «sin registro».</summary>
    public async Task<TextoCorreo> VistaPreviaAsync(Guid id, TipoEnvioCorreoComuna tipo, string? comunaSgl, CancellationToken ct = default)
    {
        var dto = await ObtenerAsync(id, ct);
        RequireAplica(dto, tipo);
        return await correo.VistaPreviaAsync(ClaveDe(tipo), dto.NombreCompleto, dto.Rut, FechaTexto(dto), comunaSgl, ct);
    }

    /// <summary>
    /// Mails the comuna that asked: «sin registro» (B), «carpeta digital» (C) or «ya se subió» (confirmation). <paramref name="asunto"/>
    /// and <paramref name="cuerpo"/> are the text edited before sending (null = the stored template). After B or C went out (or had gone
    /// out) the request closes; a failed mail or a comuna without an address leaves it open to retry. The confirmation changes no state.
    /// </summary>
    public async Task<ResultadoRespuesta> ResponderAsync(
        Guid id, TipoEnvioCorreoComuna tipo, string? comunaSgl, string? asunto, string? cuerpo, string autor, CancellationToken ct = default)
    {
        ClaveDe(tipo);
        var dto = await ObtenerAsync(id, ct);
        RequireAplica(dto, tipo);

        // Outside the gate: the mail service takes it too.
        var envio = await correo.EnviarPlantillaAsync(
            new SolicitudCorreoPlantilla(tipo, id, autor, FechaTexto(dto), comunaSgl, asunto, cuerpo), ct);
        if (envio.Resultado is not (ResultadoEnvio.Enviado or ResultadoEnvio.YaEnviado))
        {
            return new ResultadoRespuesta(envio, false);
        }

        await RegistrarRespuestaAsync(id, tipo, comunaSgl, envio, autor);
        return new ResultadoRespuesta(envio, tipo != TipoEnvioCorreoComuna.ConfirmacionSubida);
    }

    /// <summary>Critical phase: the mail already left, so closing the request is not cancelled halfway.</summary>
    private async Task RegistrarRespuestaAsync(Guid id, TipoEnvioCorreoComuna tipo, string? comunaSgl, ResultadoEnvioComuna envio, string autor)
    {
        await _gate.WaitAsync();
        try
        {
            var carpeta = await CargarSinRequisitoAsync(id, CancellationToken.None);
            var now = Now;
            var marca = envio.Resultado == ResultadoEnvio.YaEnviado ? "ya enviado antes" : envio.EsPrueba ? "PRUEBA" : "enviado";
            switch (tipo)
            {
                case TipoEnvioCorreoComuna.SinRegistro:
                    carpeta.CerrarEntranteSinRegistro(comunaSgl, autor, now);
                    carpeta.RegistrarEvento(Carpeta.CampoCorreoComuna, null, $"Respuesta «sin registro» a {envio.Comuna} ({marca})", autor, now);
                    break;
                case TipoEnvioCorreoComuna.Digital:
                    carpeta.CerrarEntranteCarpetaDigital(autor, now);
                    carpeta.RegistrarEvento(Carpeta.CampoCorreoComuna, null, $"Respuesta «carpeta digital» a {envio.Comuna} ({marca})", autor, now);
                    break;
                default:
                    if (envio.Resultado == ResultadoEnvio.Enviado)
                    {
                        carpeta.RegistrarEvento(Carpeta.CampoCorreoComuna, null, $"Confirmación «ya se subió» a {envio.Comuna} ({marca})", autor, now);
                    }
                    break;
            }

            await carpetas.SaveChangesAsync(CancellationToken.None);
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

    // ---- Listado para imprimir ----

    /// <summary>
    /// The printable listing of one sector («ARCHIVO», «OFICINA 43» or «DIGITAL») with the open requests of that sector, ordered by
    /// last-carpeta date (the shelf walk). With <paramref name="soloEnBusqueda"/> only the ones whose magnifier is «en búsqueda».
    /// The document is the one of the search listing; it does not mark anything as printed.
    /// </summary>
    public async Task<ListadoBusquedaDto> ObtenerListadoAsync(string sector, bool soloEnBusqueda = false, CancellationToken ct = default)
    {
        var (nombre, prefijo, bandeja) = sector switch
        {
            SectorFisico.Archivo => ("Archivo", "Archivo", Bandeja.SeEncuentraEnArchivos),
            SectorFisico.Oficina43 => ("Oficina 43", "Oficina43", Bandeja.SeEncuentraEnOf43),
            SectorFisico.Digital => ("Digital", "Digital", Bandeja.CambioDomicilio),
            _ => throw new DomainException("El sector del listado debe ser Archivo, Oficina 43 o Digital."),
        };

        var hoy = clock.HoyChile();
        var elegidas = (await _gate.LeerAsync(() => consulta.ListarAbiertasAsync(ct), ct))
            .Select(f => ConPlazo(f, hoy))
            .Where(f => f.Sector == sector && (!soloEnBusqueda || f.EnBusqueda))
            .Select(f => (Dto: f, Fecha: SectorFisico.TryParseFecha(f.FechaUltimaCarpeta, out var d) ? (DateOnly?)d : null))
            .OrderBy(x => x.Fecha is null)
            .ThenBy(x => x.Fecha)
            .ThenBy(x => x.Dto.NombreCompleto, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Dto.Rut, StringComparer.Ordinal)
            .Select((x, i) => new ListadoBusquedaItem(
                i + 1, x.Dto.Id, x.Dto.Rut, x.Dto.NombreCompleto, x.Dto.Sede, x.Dto.Comuna,
                x.Fecha is { } f ? FechaImpresa.De(f) : "—", null))
            .ToList();

        var ahora = Now;
        return new ListadoBusquedaDto(
            $"NosPiden-{prefijo}-{HoraChile.ALocal(ahora):yyyyMMdd-HHmmss}",
            ahora,
            bandeja,
            nombre,
            sector == SectorFisico.Digital
                ? "Listado — Nos piden carpetas · Digital (sin carpeta física)"
                : $"Listado para búsqueda — Nos piden carpetas · {nombre}",
            null,
            elegidas.Count,
            elegidas);
    }

    // ---- Internos ----

    private EntranteDto ConPlazo(EntranteFila f, DateOnly hoy)
    {
        var vence = DeadlineCalculator.AddBusinessDays(f.FechaCorreo, Math.Max(0, opciones.PlazoEntranteDias));
        var dias = vence.DayNumber - hoy.DayNumber;
        var color = dias < 0 ? PlazoColor.Rojo : dias <= SeguimientoService.DiasAmarillo ? PlazoColor.Amarillo : PlazoColor.Verde;
        return new EntranteDto(
            f.Id, f.Rut, f.NombreCompleto, f.Comuna, f.Sede, f.FechaCorreo, f.FechaUltimaCarpeta,
            SectorFisico.Calcular(f.FechaUltimaCarpeta), f.Estado, f.EnBusqueda, f.Encontrada, f.FechaSubida, f.FechaCierre,
            f.RevisarNombre, vence, dias, color, f.UltimoCorreo, f.UltimoCorreoEn, f.UltimoCorreoEsPrueba);
    }

    /// <summary>Last-carpeta date as written in the mails (dd-MM-yyyy), or null when there is none.</summary>
    private static string? FechaTexto(EntranteDto dto) =>
        SectorFisico.TryParseFecha(dto.FechaUltimaCarpeta, out var fecha) ? fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) : null;

    private static void RequireAplica(EntranteDto dto, TipoEnvioCorreoComuna tipo)
    {
        switch (tipo)
        {
            case TipoEnvioCorreoComuna.SinRegistro when !dto.Salidas.HasFlag(SalidasEntrante.SinRegistro):
                throw new DomainException(dto.Digital
                    ? "Con una última carpeta digital solo corresponde la respuesta «carpeta digital»."
                    : MensajeNoEstaAbierta);
            case TipoEnvioCorreoComuna.Digital when !dto.Salidas.HasFlag(SalidasEntrante.CarpetaDigital):
                throw new DomainException(dto.Abierta
                    ? "La respuesta «carpeta digital» solo corresponde con una última carpeta desde el corte digital."
                    : MensajeNoEstaAbierta);
            case TipoEnvioCorreoComuna.ConfirmacionSubida when !dto.Confirmable:
                throw new DomainException("Solo se confirma a la comuna una carpeta que ya se subió.");
        }
    }

    private async Task<T> EscribirAsync<T>(Guid id, Func<Carpeta, DateTime, T> accion, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await CargarAsync(id, ct);
            var resultado = accion(carpeta, Now);
            await carpetas.SaveChangesAsync(ct);
            return resultado;
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

    private async Task<Carpeta> CargarAsync(Guid id, CancellationToken ct)
    {
        var carpeta = await CargarSinRequisitoAsync(id, ct);
        if (carpeta.SentidoCambioDomicilio != SentidoCambioDomicilio.Entrante || carpeta.Estado != EstadoCarpeta.CambioDomicilio
            || carpeta.PendienteReclasificacion)
        {
            throw new DomainException(MensajeNoEstaAbierta);
        }

        return carpeta;
    }

    private async Task<Carpeta> CargarSinRequisitoAsync(Guid id, CancellationToken ct) =>
        await carpetas.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");
}
