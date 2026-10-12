using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>Use cases for carpetas: RegistrarCarpeta, CambiarEstado, EditarCarpeta, ListarCarpetas, ObtenerCarpeta.</summary>
public sealed class CarpetaService(ICarpetaRepository repository, TimeProvider clock, LicenciasOptions? options = null)
{
    private readonly LicenciasOptions _options = options ?? new LicenciasOptions();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Registers a new carpeta. Enforces the mandatory license class (when configured) and the duplicate
    /// rules: an open carpeta of the same citizen sharing a class blocks; any other open carpeta requires
    /// <see cref="RegistrarCarpetaCommand.ConfirmarDuplicado"/>.
    /// </summary>
    public async Task<Guid> RegistrarAsync(RegistrarCarpetaCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var rut = Rut.Parse(cmd.Rut);
        if (cmd.Clases == ClaseLicencia.Ninguna && _options.ClaseEsObligatoria(clock.HoyChile()))
        {
            throw new DomainException("Debe indicar al menos una clase de licencia.");
        }

        var existente = await repository.FindCiudadanoAsync(rut, ct);
        if (existente is not null)
        {
            var verificacion = VerificacionDuplicadoDto.Clasificar(
                await repository.GetCarpetasAbiertasAsync(existente.Id, ct), cmd.Clases);
            if (verificacion.Bloquea || (verificacion.RequiereConfirmacion && !cmd.ConfirmarDuplicado))
            {
                throw new CarpetaDuplicadaException(verificacion);
            }
        }

        var ciudadano = existente ?? new Ciudadano(rut, cmd.Nombre, cmd.Apellido);
        var carpeta = Carpeta.Registrar(ciudadano, cmd.Sede, cmd.FechaCitacion, cmd.Autor, Now, cmd.Clases);
        await repository.AddAsync(carpeta, ct);
        await repository.SaveChangesAsync(ct);
        return carpeta.Id;
    }

    /// <summary>Checks a prospective registration against the citizen's open carpetas without saving.</summary>
    public async Task<VerificacionDuplicadoDto> VerificarDuplicadoAsync(string rutTexto, ClaseLicencia clases, CancellationToken ct = default)
    {
        var ciudadano = await repository.FindCiudadanoAsync(Rut.Parse(rutTexto), ct);
        return ciudadano is null
            ? VerificacionDuplicadoDto.Vacia
            : VerificacionDuplicadoDto.Clasificar(await repository.GetCarpetasAbiertasAsync(ciudadano.Id, ct), clases);
    }

    public async Task AsignarClasesAsync(Guid id, ClaseLicencia clases, string autor, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        carpeta.AsignarClases(clases, autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task CambiarEstadoAsync(CambiarEstadoCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var carpeta = await LoadAsync(cmd.CarpetaId, ct);
        carpeta.CambiarEstado(cmd.NuevoEstado, cmd.Autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Classifies "Por clasificar" carpetas; all or nothing (a single save). A class is mandatory, and an open
    /// carpeta of the same RUT sharing a class blocks (also between rows of the same batch).
    /// </summary>
    public async Task ClasificarAsync(ClasificarCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.CarpetaIds.Count == 0)
        {
            return;
        }
        if (cmd.Clases == ClaseLicencia.Ninguna)
        {
            throw new DomainException("Debe marcar al menos una clase de licencia para clasificar.");
        }

        var now = Now;
        var asignadasEnLote = new Dictionary<Guid, List<CarpetaAbiertaDto>>();
        foreach (var id in cmd.CarpetaIds.Distinct())
        {
            var carpeta = await LoadAsync(id, ct);
            var abiertas = (await repository.GetCarpetasAbiertasAsync(carpeta.CiudadanoId, ct))
                .Where(a => a.Id != carpeta.Id)
                .Concat(asignadasEnLote.GetValueOrDefault(carpeta.CiudadanoId) ?? [])
                .ToList();
            var verificacion = VerificacionDuplicadoDto.Clasificar(abiertas, cmd.Clases);
            if (verificacion.Bloquea)
            {
                throw new CarpetaDuplicadaException(verificacion);
            }

            carpeta.Clasificar(cmd.Destino, cmd.Clases, cmd.FechaUltimaCarpeta, cmd.Comuna, cmd.Autor, now);
            if (!asignadasEnLote.TryGetValue(carpeta.CiudadanoId, out var lista))
            {
                lista = [];
                asignadasEnLote[carpeta.CiudadanoId] = lista;
            }
            lista.Add(new CarpetaAbiertaDto(carpeta.Id, carpeta.Sede, carpeta.FechaCitacion, carpeta.Estado, carpeta.Clases));
        }
        await repository.SaveChangesAsync(ct);
    }

    /// <summary>Soft-deletes a carpeta (never a physical delete). It disappears from modules, counts and statistics.</summary>
    public async Task AnularAsync(Guid id, string motivo, string autor, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        carpeta.Anular(motivo, autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Checks what restoring would reopen: an open, non-archived carpeta of the same RUT sharing a class blocks;
    /// one without class overlap only warns (the UI asks for confirmation).
    /// </summary>
    public async Task<VerificacionDuplicadoDto> VerificarRestauracionAsync(Guid id, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        return await VerificarRestauracionAsync(carpeta, ct);
    }

    private async Task<VerificacionDuplicadoDto> VerificarRestauracionAsync(Carpeta carpeta, CancellationToken ct)
    {
        if (!carpeta.Anulada || !carpeta.EstaAbierta)
        {
            return VerificacionDuplicadoDto.Vacia;
        }
        var abiertas = (await repository.GetCarpetasAbiertasSinArchivarAsync(carpeta.CiudadanoId, ct))
            .Where(a => a.Id != carpeta.Id)
            .ToList();
        return VerificacionDuplicadoDto.Clasificar(abiertas, carpeta.Clases);
    }

    /// <summary>Restores a cancelled carpeta to the state it had before the cancellation (audited). Blocked by a class conflict.</summary>
    public async Task RestaurarAsync(Guid id, string autor, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        var verificacion = await VerificarRestauracionAsync(carpeta, ct);
        if (verificacion.Bloquea)
        {
            throw new CarpetaDuplicadaException(verificacion);
        }
        carpeta.Restaurar(autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Duplicate resolution "Reagendar": the kept carpeta (<paramref name="destinoId"/>) takes the appointment date and
    /// sede of <paramref name="origenId"/>, which is cancelled with reason "Reagendamiento". Single save.
    /// </summary>
    public async Task ReagendarAsync(Guid origenId, Guid destinoId, string autor, CancellationToken ct = default)
    {
        if (origenId == destinoId)
        {
            throw new DomainException("Debe elegir dos carpetas distintas para reagendar.");
        }

        var origen = await LoadAsync(origenId, ct);
        var destino = await LoadAsync(destinoId, ct);
        if (origen.CiudadanoId != destino.CiudadanoId)
        {
            throw new DomainException("Solo se puede reagendar entre carpetas del mismo RUT.");
        }
        if (!destino.EstaAbiertaSinArchivar)
        {
            throw new DomainException("Solo se puede reagendar hacia una carpeta abierta (sin decisión final, no anulada y sin caja de archivo).");
        }

        var now = Now;
        destino.Reagendar(origen.FechaCitacion, origen.Sede, autor, now);
        origen.Anular(MotivosAnulacion.Reagendamiento, autor, now);
        await repository.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<CarpetaAnuladaDto>> ListarAnuladasAsync(string? texto = null, CancellationToken ct = default)
    {
        var (rutFragment, nameFragment) = TextoBusqueda.Parse(string.IsNullOrWhiteSpace(texto) ? null : texto.Trim());
        return repository.GetAnuladasAsync(rutFragment, nameFragment, ct);
    }

    public async Task EditarAsync(EditarCarpetaCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var carpeta = await LoadAsync(cmd.CarpetaId, ct);
        carpeta.Editar(cmd.Sede, cmd.FechaCitacion, cmd.FechaSubida, cmd.IdoneidadMoral, cmd.Autor, Now);
        if (!string.IsNullOrWhiteSpace(cmd.Rut))
        {
            var nuevoRut = Rut.Parse(cmd.Rut);
            if (carpeta.Ciudadano.Rut != nuevoRut)
            {
                var existente = await repository.FindCiudadanoAsync(nuevoRut, ct);
                carpeta.ModificarRut(nuevoRut, existente, cmd.Autor, Now);
            }
        }
        await repository.SaveChangesAsync(ct);
    }

    public async Task ModificarRutAsync(Guid id, string nuevoRutTexto, string autor, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        var nuevoRut = Rut.Parse(nuevoRutTexto);
        if (carpeta.Ciudadano.Rut == nuevoRut)
        {
            return;
        }

        var existente = await repository.FindCiudadanoAsync(nuevoRut, ct);
        carpeta.ModificarRut(nuevoRut, existente, autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task AsignarCajaAsync(AsignarCajaCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var carpeta = await LoadAsync(cmd.CarpetaId, ct);
        carpeta.AsignarCaja(cmd.NuevaCaja, cmd.Autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task AsignarCajaLoteAsync(AsignarCajaLoteCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        foreach (var id in cmd.CarpetaIds)
        {
            var carpeta = await LoadAsync(id, ct);
            carpeta.AsignarCaja(cmd.NuevaCaja, cmd.Autor, Now);
        }
        await repository.SaveChangesAsync(ct);
    }

    public async Task ForzarEstadoAsync(Guid id, EstadoCarpeta nuevoEstado, string autor, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        carpeta.ForzarEstado(nuevoEstado, autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task MarcarPendienteBusquedaAsync(Guid id, bool valor, string autor, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        carpeta.MarcarPendienteBusqueda(valor, autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task CambiarDecisionAsync(Guid id, Decision nuevaDecision, string autor, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        carpeta.CambiarDecision(nuevaDecision, autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task GuardarObservacionAsync(Guid id, string? observacion, string autor, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        carpeta.ModificarObservacion(observacion, autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task GuardarComunaAsync(Guid id, string? comuna, string autor, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        if (!Carpeta.EsCambioDomicilio(carpeta.Estado))
        {
            throw new DomainException("Solo se puede editar la comuna de una carpeta en cambio de domicilio.");
        }
        carpeta.AsignarComuna(comuna, autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task GuardarFechaUltimaCarpetaAsync(Guid id, string? fecha, string autor, CancellationToken ct = default)
    {
        var carpeta = await LoadAsync(id, ct);
        carpeta.ModificarFechaUltimaCarpeta(fecha, autor, Now);
        await repository.SaveChangesAsync(ct);
    }

    public async Task ReabrirCajaAsync(string caja, string autor, CancellationToken ct = default)
    {
        var carpetas = await repository.GetCarpetasByCajaAsync(caja, ct);
        foreach (var c in carpetas)
        {
            var carpeta = await LoadAsync(c.Id, ct);
            carpeta.AsignarCaja("Cola de Caja", autor, Now);
        }
        await repository.SaveChangesAsync(ct);
    }

    public async Task<CarpetaDetalleDto> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var c = await LoadAsync(id, ct);
        var historial = c.Historial
            .OrderByDescending(h => h.Fecha)
            .ThenByDescending(h => h.Id)
            .Select(h => new HistorialDto(h.Fecha, h.Usuario, h.Campo, h.Anterior, h.Nuevo))
            .ToList();

        return new CarpetaDetalleDto(
            c.Id,
            c.Ciudadano.Rut.Formatted,
            c.Ciudadano.Nombre,
            c.Ciudadano.Apellido,
            c.Sede,
            c.FechaCitacion,
            c.FechaSubida,
            c.FechaUltimaCarpeta,
            c.Comuna,
            c.IdoneidadMoral,
            c.TipoTramite,
            c.CajaArchivo,
            c.Estado,
            c.Decision,
            EstadoTransitions.NextStates(c.Estado),
            historial,
            c.Clases,
            c.Anulada,
            c.MotivoAnulacion);
    }

    public async Task<ListResult<CarpetaResumenDto>> ListarAsync(ListarCarpetasQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var search = ToSearch(query.Normalized());
        var (items, total) = await repository.SearchAsync(search, ct);
        if (search.Take is null or 0 || items.Count == 0)
        {
            return new ListResult<CarpetaResumenDto>(items, total);
        }

        // Indicators are computed only for the requested (visible) range.
        var movimientos = (await repository.GetMovimientosAsync(items.Select(i => i.Id).ToList(), ct))
            .ToLookup(m => m.CarpetaId);
        var hoy = clock.HoyChile();
        var porClasificar = items.Where(i => i.Estado == EstadoCarpeta.PorClasificar).Select(i => i.Id).ToList();
        var previos = porClasificar.Count == 0
            ? new Dictionary<Guid, TramitePrevioDto>()
            : await repository.GetTramitesPreviosAsync(porClasificar, ct);
        var alertas = porClasificar.Count == 0
            ? new Dictionary<Guid, AlertaDuplicadoDto>()
            : await repository.GetAlertasDuplicadoAsync(porClasificar, ct);
        var enriquecidos = items
            .Select(i => i with
            {
                Indicadores = IndicadoresCalculator.Calcular(i, movimientos[i.Id], hoy),
                TramitePrevio = previos.GetValueOrDefault(i.Id),
                AlertaDuplicado = alertas.GetValueOrDefault(i.Id),
            })
            .ToList();
        return new ListResult<CarpetaResumenDto>(enriquecidos, total);
    }

    /// <summary>Counts per tray honoring every filter except the tray itself.</summary>
    public Task<IReadOnlyDictionary<Bandeja, int>> ContarPorBandejaAsync(ListarCarpetasQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return repository.CountByBandejaAsync(ToSearch(query.Normalized() with { Bandeja = null }), ct);
    }

    private static CarpetaSearch ToSearch(ListarCarpetasQuery q)
    {
        var (rutFragment, nameFragment) = TextoBusqueda.Parse(q.Texto);
        return new CarpetaSearch(
            rutFragment,
            nameFragment,
            q.Sede,
            q.Estado,
            q.TipoTramite,
            q.CajaArchivo,
            q.SinCaja,
            q.Comuna,
            q.Skip,
            q.Take,
            q.Bandeja,
            q.Desde,
            q.Hasta);
    }

    public Task<EstadisticasGlobalesDto> ObtenerEstadisticasAsync(CancellationToken ct = default) =>
        repository.GetEstadisticasAsync(ct);

    public Task<IReadOnlyList<CajaResumenDto>> ObtenerCajasAsync(CancellationToken ct = default) =>
        repository.GetCajasAsync(ct);

    public Task<IReadOnlyList<CarpetaResumenDto>> ObtenerCarpetasDeCajaAsync(string caja, CancellationToken ct = default) =>
        repository.GetCarpetasByCajaAsync(caja, ct);

    public Task<IReadOnlyList<string>> ObtenerTiposTramiteAsync(CancellationToken ct = default) =>
        repository.GetTiposTramiteAsync(ct);

    private async Task<Carpeta> LoadAsync(Guid id, CancellationToken ct) =>
        await repository.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");
}
