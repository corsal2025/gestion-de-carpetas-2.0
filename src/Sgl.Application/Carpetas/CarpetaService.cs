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
        if (cmd.Clases == ClaseLicencia.Ninguna && _options.ClaseEsObligatoria(DateOnly.FromDateTime(Now)))
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
            c.Clases);
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
        var hoy = DateOnly.FromDateTime(Now);
        var enriquecidos = items
            .Select(i => i with { Indicadores = IndicadoresCalculator.Calcular(i, movimientos[i.Id], hoy) })
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
