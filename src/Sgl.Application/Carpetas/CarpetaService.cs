using Sgl.Application.Solicitudes;
using Sgl.Domain;

namespace Sgl.Application.Carpetas;

/// <summary>Use cases for carpetas: RegistrarCarpeta, CambiarEstado, EditarCarpeta, ListarCarpetas, ObtenerCarpeta.</summary>
public sealed class CarpetaService(
    ICarpetaRepository repository,
    TimeProvider clock,
    LicenciasOptions? options = null,
    INotificadorPeticion? notificador = null,
    ISolicitudF8Repository? solicitudesF8 = null,
    ICertificadoRepository? certificados = null,
    UnidadDeTrabajoGate? gate = null)
{
    private readonly LicenciasOptions _options = options ?? new LicenciasOptions();

    // One circuit shares this service and its DbContext with the other services: every read and write goes through
    // the circuit's gate, so a ChangeTracker.Clear() can never run in the middle of another operation's SaveChanges.
    private readonly UnidadDeTrabajoGate _gate = gate ?? new UnidadDeTrabajoGate();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Registers a new carpeta. Enforces the mandatory license class (when configured) and the duplicate
    /// rules: an open carpeta of the same citizen sharing a class blocks; any other open carpeta requires
    /// <see cref="RegistrarCarpetaCommand.ConfirmarDuplicado"/>.
    /// </summary>
    public async Task<Guid> RegistrarAsync(RegistrarCarpetaCommand cmd, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
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
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Changes the state through the state machine. Returns the point the change can be undone to.</summary>
    public async Task<PuntoRestauracionEstado> CambiarEstadoAsync(CambiarEstadoCommand cmd, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ArgumentNullException.ThrowIfNull(cmd);
            var carpeta = await LoadActivaAsync(cmd.CarpetaId, ct);
            var punto = carpeta.PuntoRestauracion();
            var estadoAnterior = carpeta.Estado;
            carpeta.CambiarEstado(cmd.NuevoEstado, cmd.Autor, Now);
            await SolicitarF8AutomaticoAsync(carpeta, estadoAnterior, cmd.Autor, ct);
            await repository.SaveChangesAsync(ct);
            return punto;
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Classifies "Por clasificar" carpetas; all or nothing (a single save). A class is mandatory, and an open
    /// carpeta of the same RUT sharing a class blocks (also between rows of the same batch).
    /// </summary>
    public async Task<ClasificarResultado> ClasificarAsync(ClasificarCommand cmd, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ArgumentNullException.ThrowIfNull(cmd);
            if (cmd.CarpetaIds.Count == 0)
            {
                return new ClasificarResultado([]);
            }
            if (cmd.Clases == ClaseLicencia.Ninguna)
            {
                throw new DomainException("Debe marcar al menos una clase de licencia para clasificar.");
            }

            var now = Now;
            var asignadasEnLote = new Dictionary<Guid, List<CarpetaAbiertaDto>>();
            var validadas = new List<(Carpeta Carpeta, EstadoCarpeta Destino)>();
            foreach (var id in cmd.CarpetaIds.Distinct())
            {
                var carpeta = await LoadActivaAsync(id, ct);
                var abiertas = (await repository.GetCarpetasAbiertasAsync(carpeta.CiudadanoId, ct))
                    .Where(a => a.Id != carpeta.Id)
                    .Concat(asignadasEnLote.GetValueOrDefault(carpeta.CiudadanoId) ?? [])
                    .ToList();
                var verificacion = VerificacionDuplicadoDto.Clasificar(abiertas, cmd.Clases);
                if (verificacion.Bloquea)
                {
                    throw new CarpetaDuplicadaException(verificacion);
                }

                var destino = Carpeta.ResolverDestinoClasificacion(cmd.Destino, cmd.FechaUltimaCarpeta, carpeta.FechaUltimaCarpeta);
                carpeta.ValidarClasificacion(destino, cmd.Clases, cmd.Comuna, cmd.Autor);
                validadas.Add((carpeta, destino));
                if (!asignadasEnLote.TryGetValue(carpeta.CiudadanoId, out var lista))
                {
                    lista = [];
                    asignadasEnLote[carpeta.CiudadanoId] = lista;
                }
                lista.Add(new CarpetaAbiertaDto(carpeta.Id, carpeta.Sede, carpeta.FechaCitacion, destino, cmd.Clases));
            }
            // Second phase: every row passed validation, only now is anything changed.
            foreach (var (carpeta, destino) in validadas)
            {
                carpeta.Clasificar(destino, cmd.Clases, cmd.FechaUltimaCarpeta, cmd.Comuna, cmd.Autor, now);
            }
            await repository.SaveChangesAsync(ct);
            return new ClasificarResultado(validadas.Select(v => new ClasificacionAplicada(v.Carpeta.Id, v.Destino)).ToList());
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Soft-deletes a carpeta (never a physical delete). It disappears from modules, counts and statistics.</summary>
    public async Task AnularAsync(Guid id, string motivo, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            carpeta.Anular(motivo, autor, Now);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Checks what restoring would reopen: an open, non-archived carpeta of the same RUT sharing a class blocks;
    /// one without class overlap only warns (the UI asks for confirmation).
    /// </summary>
    public async Task<VerificacionDuplicadoDto> VerificarRestauracionAsync(Guid id, CancellationToken ct = default)
    {
        return await _gate.LeerAsync(async () => await VerificarRestauracionAsync(await LoadAsync(id, ct), ct), ct);
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
        await _gate.WaitAsync(ct);
        try
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
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Duplicate resolution "Reagendar": the kept carpeta (<paramref name="destinoId"/>) takes the appointment date and
    /// sede of <paramref name="origenId"/>, which is cancelled with reason "Reagendamiento". Single save.
    /// </summary>
    public async Task ReagendarAsync(Guid origenId, Guid destinoId, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (origenId == destinoId)
            {
                throw new DomainException("Debe elegir dos carpetas distintas para reagendar.");
            }

            var origen = await LoadActivaAsync(origenId, ct);
            var destino = await LoadActivaAsync(destinoId, ct);
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
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<CarpetaAnuladaDto>> ListarAnuladasAsync(string? texto = null, CancellationToken ct = default)
    {
        var (rutFragment, nameFragment) = TextoBusqueda.Parse(string.IsNullOrWhiteSpace(texto) ? null : texto.Trim());
        return _gate.LeerAsync(() => repository.GetAnuladasAsync(rutFragment, nameFragment, ct), ct);
    }

    public async Task EditarAsync(EditarCarpetaCommand cmd, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ArgumentNullException.ThrowIfNull(cmd);
            var carpeta = await LoadActivaAsync(cmd.CarpetaId, ct);
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
            if (cmd.CambiarCaja)
            {
                carpeta.AsignarCaja(cmd.Caja, cmd.Autor, Now);
            }
            if (cmd.Clases is { } clases)
            {
                carpeta.AsignarClases(clases, cmd.Autor, Now);
            }
            // Single save: the detail form is applied completely or not at all.
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ModificarRutAsync(Guid id, string nuevoRutTexto, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            var nuevoRut = Rut.Parse(nuevoRutTexto);
            if (carpeta.Ciudadano.Rut == nuevoRut)
            {
                return;
            }

            var existente = await repository.FindCiudadanoAsync(nuevoRut, ct);
            carpeta.ModificarRut(nuevoRut, existente, autor, Now);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ModificarNombreAsync(Guid id, string nombreCompleto, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            carpeta.ModificarNombre(nombreCompleto, autor, Now);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AsignarCajaAsync(AsignarCajaCommand cmd, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ArgumentNullException.ThrowIfNull(cmd);
            var carpeta = await LoadActivaAsync(cmd.CarpetaId, ct);
            carpeta.AsignarCaja(cmd.NuevaCaja, cmd.Autor, Now);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AsignarCajaLoteAsync(AsignarCajaLoteCommand cmd, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ArgumentNullException.ThrowIfNull(cmd);
            foreach (var id in cmd.CarpetaIds)
            {
                var carpeta = await LoadActivaAsync(id, ct);
                carpeta.AsignarCaja(cmd.NuevaCaja, cmd.Autor, Now);
            }
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Changes the state without transition checks (manual correction in the list). Returns the point the change can
    /// be undone to, captured inside the same write gate.
    /// </summary>
    public async Task<PuntoRestauracionEstado> ForzarEstadoAsync(Guid id, EstadoCarpeta nuevoEstado, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            var punto = carpeta.PuntoRestauracion();
            var estadoAnterior = carpeta.Estado;
            carpeta.ForzarEstado(nuevoEstado, autor, Now);
            await SolicitarF8AutomaticoAsync(carpeta, estadoAnterior, autor, ct);
            await repository.SaveChangesAsync(ct);
            return punto;
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Undo of a state change (R11): audited; refused when the state changed again meanwhile.</summary>
    public async Task DeshacerEstadoAsync(Guid id, PuntoRestauracionEstado punto, EstadoCarpeta estadoEsperado, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            var f8Automatica = await F8AutomaticaDelMovimientoAsync(carpeta, punto, estadoEsperado, ct);
            var now = Now;
            carpeta.RestaurarEstado(punto, estadoEsperado, autor, now);
            f8Automatica?.Cancelar(autor, now, "Se deshizo el movimiento que la originó.");
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// "Pedir" (Crear certificado / oficio): hands the carpeta over to Solicitudes · Certificado / Oficio, where the
    /// certificate process ends. In ONE save it audits the request date (the carpeta leaves the tray) and makes sure an open
    /// SinCarpeta certificate exists (the one opened by the F8 flow is reused). Then it hands the request to the mail port;
    /// the request is kept even when no mail is configured or the mail fails.
    /// Asking again for a carpeta already handed over changes nothing and sends nothing.
    /// </summary>
    /// <returns>True when the request mail was sent.</returns>
    public async Task<bool> PedirAsync(Guid id, string autor, CancellationToken ct = default)
    {
        if (certificados is null)
        {
            throw new InvalidOperationException("Pedir necesita el repositorio de certificados.");
        }

        PeticionCertificado peticion;
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            var now = Now;
            var abierto = await certificados.GetAbiertoAsync(carpeta.Id, TipoCertificado.SinCarpeta, ct);
            if (carpeta.FechaPeticion is not null && abierto is not null)
            {
                return false;
            }

            carpeta.RegistrarPeticion(autor, now);
            if (abierto is null)
            {
                await certificados.AddAsync(SolicitudCertificado.CrearDesdePedir(carpeta.Id, autor, now), ct);
            }

            await repository.SaveChangesAsync(ct);
            peticion = new PeticionCertificado(
                carpeta.Id,
                carpeta.Ciudadano.Rut.Formatted,
                carpeta.Ciudadano.NombreCompleto,
                carpeta.Sede,
                carpeta.FechaCitacion,
                autor.Trim(),
                now);
        }
        catch
        {
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }

        if (notificador is null)
        {
            return false;
        }

        try
        {
            return await notificador.EnviarAsync(peticion, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The request is already stored and audited; a failing mail adapter only means "not sent" (it logs the cause).
            return false;
        }
    }

    /// <summary>Marks the given carpetas as printed in a search listing; all or nothing.</summary>
    public async Task MarcarImpresasAsync(IReadOnlyList<Guid> ids, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ArgumentNullException.ThrowIfNull(ids);
            var now = Now;
            foreach (var id in ids.Distinct())
            {
                (await LoadActivaAsync(id, ct)).MarcarImpresa(autor, now);
            }
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Printable search listing of a physical-search tray (Archivos / Of. 43): the whole module under the current
    /// filters, or only <paramref name="ids"/> when given. Ordered by last-folder date, emitted in Chile time.
    /// </summary>
    public async Task<ListadoBusquedaDto> ObtenerListadoBusquedaAsync(
        Bandeja bandeja, ListarCarpetasQuery filtros, IReadOnlyCollection<Guid>? ids = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtros);
        if (bandeja is not (Bandeja.SeEncuentraEnArchivos or Bandeja.SeEncuentraEnOf43))
        {
            throw new DomainException("El listado para búsqueda solo existe para «Se encuentra en Archivos» y «Se encuentra en Of. 43».");
        }

        var consulta = filtros.Normalized() with { Bandeja = bandeja, Skip = 0, Take = null, Ids = null };
        var (items, _) = await _gate.LeerAsync(() => repository.SearchAsync(ToSearch(consulta), ct), ct);
        var elegidas = ids is { Count: > 0 } ? FiltrarPorIds(items, ids) : items;
        return ListadoBusquedaBuilder.Construir(bandeja, consulta.Sede, elegidas, Now);
    }

    public async Task MarcarPendienteBusquedaAsync(Guid id, bool valor, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            carpeta.MarcarPendienteBusqueda(valor, autor, Now);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <returns>The values before the decision, to undo it with <see cref="DeshacerEstadoAsync"/>.</returns>
    /// <summary>"Dejar para revisión": a carpeta from Por clasificar without resolution goes to Para revisión (Citada).</summary>
    public async Task DejarParaRevisionAsync(Guid id, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            carpeta.DejarParaRevision(autor, Now);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PuntoRestauracionEstado> CambiarDecisionAsync(Guid id, Decision nuevaDecision, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            var punto = carpeta.PuntoRestauracion();
            carpeta.CambiarDecision(nuevaDecision, autor, Now);
            await repository.SaveChangesAsync(ct);
            return punto;
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task GuardarObservacionAsync(Guid id, string? observacion, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            carpeta.ModificarObservacion(observacion, autor, Now);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task GuardarComunaAsync(Guid id, string? comuna, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            if (!Carpeta.EsCambioDomicilio(carpeta.Estado))
            {
                throw new DomainException("Solo se puede editar la comuna de una carpeta en cambio de domicilio.");
            }
            carpeta.AsignarComuna(comuna, autor, Now);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Changes the citation and/or upload date of one carpeta from a table cell (same audited rule as the detail edit).</summary>
    public async Task GuardarFechasAsync(Guid id, DateOnly fechaCitacion, DateOnly? fechaSubida, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            carpeta.Editar(carpeta.Sede, fechaCitacion, fechaSubida, carpeta.IdoneidadMoral, autor, Now);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task GuardarFechaUltimaCarpetaAsync(Guid id, string? fecha, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            carpeta.ModificarFechaUltimaCarpeta(fecha, autor, Now);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task GuardarCodigoF8Async(Guid id, string? codigo, string autor, CancellationToken ct = default) =>
        EditarActivaAsync(id, c => c.AsignarCodigoF8(codigo, autor, Now), ct);

    public Task GuardarFechaPenultimaCarpetaAsync(Guid id, DateOnly? fecha, string autor, CancellationToken ct = default) =>
        EditarActivaAsync(id, c => c.AsignarFechaPenultimaCarpeta(fecha, autor, Now), ct);

    private async Task EditarActivaAsync(Guid id, Action<Carpeta> edicion, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(id, ct);
            edicion(carpeta);
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReabrirCajaAsync(string caja, string autor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var carpetas = await repository.GetCarpetasByCajaAsync(caja, ct);
            foreach (var c in carpetas)
            {
                var carpeta = await LoadActivaAsync(c.Id, ct);
                carpeta.AsignarCaja("Cola de Caja", autor, Now);
            }
            await repository.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            repository.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CarpetaDetalleDto> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _gate.LeerAsync(() => LoadAsync(id, ct), ct);
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
            c.MotivoAnulacion)
        {
            FechaEstadoActual = c.FechaEstadoActual,
            FechaCierre = c.FechaCierre,
            FechaImpresion = c.FechaImpresion,
            FechaPeticion = c.FechaPeticion,
        };
    }

    public async Task<ListResult<CarpetaResumenDto>> ListarAsync(ListarCarpetasQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _gate.LeerAsync(() => ListarSinGateAsync(query, ct), ct);
    }

    private async Task<ListResult<CarpetaResumenDto>> ListarSinGateAsync(ListarCarpetasQuery query, CancellationToken ct)
    {
        var search = ToSearch(query.Normalized());
        var (items, total) = await repository.SearchAsync(search, ct);
        if ((search.Take is null or 0 && search.Ids is null) || items.Count == 0)
        {
            return new ListResult<CarpetaResumenDto>(items, total);
        }

        // Indicators are computed only for the requested (visible) range.
        var movimientos = (await repository.GetMovimientosAsync(items.Select(i => i.Id).ToList(), ct))
            .ToLookup(m => m.CarpetaId);
        var hoy = clock.HoyChile();
        // Duplicate alerts: agenda carpetas plus Citadas (both can land in Revisión · Duplicadas).
        var porClasificar = items.Where(i => i.Estado is EstadoCarpeta.PorClasificar or EstadoCarpeta.Citada).Select(i => i.Id).ToList();
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

    /// <summary>Top-bar search by RUT or name across every carpeta (any tray, cancelled too), at most <see cref="BusquedaGlobal.Limite"/>.</summary>
    public async Task<IReadOnlyList<ResultadoBusquedaGlobalDto>> BuscarGlobalAsync(string? texto, CancellationToken ct = default)
    {
        var (rut, nombre) = BusquedaGlobal.Parse(texto);
        if (rut is null && nombre is null)
        {
            return [];
        }

        return await _gate.LeerAsync(() => repository.BuscarGlobalAsync(rut, nombre, BusquedaGlobal.Limite, clock.HoyChile(), ct), ct);
    }

    /// <summary>Ids of all matches in list order, to page big trays by primary key (no OFFSET scans).</summary>
    public Task<IReadOnlyList<Guid>> ListarIdsAsync(ListarCarpetasQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _gate.LeerAsync(() => repository.SearchIdsAsync(ToSearch(query.Normalized() with { Skip = 0, Take = null, Ids = null }), ct), ct);
    }

    /// <summary>Cheap data-version stamp used to validate cached orderings across circuits.</summary>
    public Task<long> ObtenerVersionDatosAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => repository.ObtenerVersionDatosAsync(ct), ct);

    /// <summary>Counts per tray honoring every filter except the tray itself.</summary>
    public Task<IReadOnlyDictionary<Bandeja, int>> ContarPorBandejaAsync(ListarCarpetasQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _gate.LeerAsync(() => repository.CountByBandejaAsync(ToSearch(query.Normalized() with { Bandeja = null }), ct), ct);
    }

    private static IReadOnlyList<CarpetaResumenDto> FiltrarPorIds(IReadOnlyList<CarpetaResumenDto> items, IReadOnlyCollection<Guid> ids)
    {
        var conjunto = ids.ToHashSet();
        return items.Where(i => conjunto.Contains(i.Id)).ToList();
    }

    private CarpetaSearch ToSearch(ListarCarpetasQuery q)
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
            q.Hasta,
            q.Ids,
            clock.HoyChile());
    }

    public Task<EstadisticasGlobalesDto> ObtenerEstadisticasAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => repository.GetEstadisticasAsync(ct), ct);

    public Task<IReadOnlyList<CajaResumenDto>> ObtenerCajasAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => repository.GetCajasAsync(ct), ct);

    public Task<IReadOnlyList<CarpetaResumenDto>> ObtenerCarpetasDeCajaAsync(string caja, CancellationToken ct = default) =>
        _gate.LeerAsync(() => repository.GetCarpetasByCajaAsync(caja, ct), ct);

    public Task<IReadOnlyList<string>> ObtenerTiposTramiteAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => repository.GetTiposTramiteAsync(ct), ct);

    /// <summary>
    /// The one place of the automatic F8 request: a carpeta searched in Archivos / Of. 43 that turns out not to exist
    /// (moved to No existe carpeta) gets an F8 request, in the same save as the state change. The request records the
    /// search tray it came from; if one is already open nothing is added. Runs inside the caller's write gate.
    /// </summary>
    private async Task SolicitarF8AutomaticoAsync(Carpeta carpeta, EstadoCarpeta estadoAnterior, string autor, CancellationToken ct)
    {
        if (solicitudesF8 is null
            || carpeta.Estado != EstadoCarpeta.NoExisteCarpeta
            || !Carpeta.PermiteBusquedaPendiente(estadoAnterior))
        {
            return;
        }

        await SolicitudF8Creacion.SolicitarAsync(solicitudesF8, carpeta.Id, estadoAnterior, autor, Now, ct);
    }

    /// <summary>
    /// The open F8 request created by <see cref="SolicitarF8AutomaticoAsync"/> in the very move being undone (it was
    /// requested after the carpeta entered "No existe carpeta"); a request older than the move is the operator's own and stays.
    /// </summary>
    private async Task<SolicitudF8?> F8AutomaticaDelMovimientoAsync(
        Carpeta carpeta, PuntoRestauracionEstado punto, EstadoCarpeta estadoEsperado, CancellationToken ct)
    {
        if (solicitudesF8 is null
            || estadoEsperado != EstadoCarpeta.NoExisteCarpeta
            || carpeta.Estado != EstadoCarpeta.NoExisteCarpeta
            || !Carpeta.PermiteBusquedaPendiente(punto.Estado))
        {
            return null;
        }

        var abierta = await solicitudesF8.GetAbiertaPorCarpetaAsync(carpeta.Id, ct);
        return abierta is not null && abierta.EstadoOrigen == punto.Estado && abierta.SolicitadoEn >= carpeta.FechaEstadoActual
            ? abierta
            : null;
    }

    /// <summary>Loads a carpeta that may be mutated: cancelled ones are rejected (only restore/inspect may see them).</summary>
    private async Task<Carpeta> LoadActivaAsync(Guid id, CancellationToken ct)
    {
        var carpeta = await LoadAsync(id, ct);
        return carpeta.Anulada
            ? throw new DomainException("La carpeta está anulada: restáurela desde «Anuladas» antes de modificarla.")
            : carpeta;
    }

    private async Task<Carpeta> LoadAsync(Guid id, CancellationToken ct) =>
        await repository.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");
}
