using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Solicitudes;

/// <summary>
/// Use cases of the Certificado / Oficio module. SinCarpeta certificates are opened by
/// <see cref="SolicitudesF8Service.MarcarNoSeGeneroAsync"/>; Registro Civil and Oficio are created here, manually.
/// The repositories must share one unit of work (one scoped DbContext).
/// </summary>
public sealed class CertificadosService(
    ICarpetaRepository carpetas,
    ICertificadoRepository certificados,
    IFirmantesRepository firmantes,
    TimeProvider clock,
    INotificadorCertificado? notificador = null,
    UnidadDeTrabajoGate? gate = null,
    IGeneradorDocumentoCertificado? generador = null)
{
    private readonly UnidadDeTrabajoGate _gate = gate ?? new UnidadDeTrabajoGate();

    private readonly EnvioCertificadoDirector _envio = new(carpetas, certificados, firmantes, notificador, clock);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Creates a Registro Civil or Oficio request and notifies the director through the port.</summary>
    public async Task<CertificadoCreado> CrearManualAsync(CrearCertificadoManualCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        Guid id;
        await _gate.WaitAsync(ct);
        try
        {
            var carpeta = await LoadActivaAsync(cmd.CarpetaId, ct);
            if (await certificados.GetAbiertoAsync(carpeta.Id, cmd.Tipo, ct) is not null)
            {
                throw new DomainException("Ya existe una solicitud abierta de este tipo para la carpeta.");
            }

            var certificado = SolicitudCertificado.CrearManual(carpeta.Id, cmd.Tipo, cmd.Parametros, cmd.NumeroOficio, cmd.Autor, Now);
            await certificados.AddAsync(certificado, ct);
            carpeta.RegistrarEvento("Certificado", null, $"Solicitado: {EtiquetasSolicitudes.Of(cmd.Tipo)}", cmd.Autor, Now);
            await carpetas.SaveChangesAsync(ct);
            id = certificado.Id;
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

        var envio = await _envio.EnviarAsync(id, _gate, ct);
        return new CertificadoCreado(id, envio.Enviado, envio.Error);
    }

    /// <summary>Edits parameters / oficio number / note while the certificate is still waiting for the signature (audited on the carpeta).</summary>
    public async Task ActualizarDatosAsync(
        Guid id,
        IReadOnlyDictionary<string, string>? parametros,
        string? numeroOficio,
        string? observacion,
        string autor,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var usuario = Autores.Requerir(autor);
            var certificado = await LoadAsync(id, ct);
            var carpeta = await LoadActivaAsync(certificado.CarpetaId, ct);
            certificado.ActualizarDatos(parametros, numeroOficio, observacion);
            carpeta.RegistrarEvento("Certificado", null, $"Datos actualizados: {EtiquetasSolicitudes.Of(certificado.Tipo)}", usuario, Now);
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

    /// <summary>
    /// Marks the certificate as signed and sends the carpeta to SubidaConOficio (audited, with the actor). A SinCarpeta
    /// certificate whose carpeta is no longer in Crear certificado is signed but the carpeta is NOT moved. When a document
    /// generator is available the document must be ready (signers and parameters) before the signature is recorded.
    /// </summary>
    /// <returns>True when the carpeta was moved to Subida con oficio.</returns>
    public async Task<bool> MarcarFirmadoAsync(Guid id, string autor, string? numeroOficio = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var certificado = await LoadAsync(id, ct);
            var carpeta = await LoadActivaAsync(certificado.CarpetaId, ct);
            if (EstadoTransitions.IsFinal(carpeta.Estado))
            {
                throw new DomainException(
                    $"La carpeta ya está en un estado final ({carpeta.Estado}): no se puede enviar a «Subida con oficio».");
            }
            if (generador is not null)
            {
                var datos = new DatosCertificado(
                    certificado.Tipo,
                    carpeta.Ciudadano.Rut.Formatted,
                    carpeta.Ciudadano.NombreCompleto.ToUpperInvariant(),
                    string.IsNullOrWhiteSpace(numeroOficio) ? certificado.NumeroOficio : numeroOficio.Trim(),
                    certificado.Parametros);
                if (generador.MotivoNoGenerable(datos, await firmantes.ObtenerAsync(ct)) is { } motivo)
                {
                    throw new DomainException(motivo);
                }
            }

            var now = Now;
            var mueve = certificado.Tipo != TipoCertificado.SinCarpeta || carpeta.Estado == EstadoCarpeta.CrearCertificado;
            certificado.MarcarFirmado(autor, now, numeroOficio);
            if (mueve)
            {
                carpeta.ForzarEstado(EstadoCarpeta.SubidaConOficio, autor, now);
            }
            await carpetas.SaveChangesAsync(ct);
            return mueve;
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

    /// <summary>One certificate with its carpeta data (for the document download).</summary>
    public async Task<SolicitudCertificadoDto> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var (items, _) = await _gate.LeerAsync(
            () => certificados.ListarAsync(new FiltroCertificados(null, null, null, null, 0, 1, id), ct), ct);
        return items.Count == 1 ? items[0] : throw new NotFoundException($"Certificado {id} no encontrado.");
    }

    public async Task<ListResult<SolicitudCertificadoDto>> ListarAsync(ListarCertificadosQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (rut, nombre) = TextoBusqueda.Parse(query.Texto);
        var (skip, take) = ListadoSolicitudes.Acotar(query.Skip, query.Take);
        var (items, total) = await _gate.LeerAsync(
            () => certificados.ListarAsync(new FiltroCertificados(query.Estado, query.Tipo, rut, nombre, skip, take), ct), ct);
        return new ListResult<SolicitudCertificadoDto>(items, total);
    }

    /// <summary>Certificates per state for the menu badge (optionally of one tipo); every state is present.</summary>
    public async Task<IReadOnlyDictionary<EstadoCertificado, int>> ContarPorEstadoAsync(TipoCertificado? tipo = null, CancellationToken ct = default)
    {
        var cuentas = await _gate.LeerAsync(() => certificados.ContarPorEstadoAsync(tipo, ct), ct);
        return Enum.GetValues<EstadoCertificado>().ToDictionary(e => e, e => cuentas.GetValueOrDefault(e));
    }

    private async Task<SolicitudCertificado> LoadAsync(Guid id, CancellationToken ct) =>
        await certificados.GetAsync(id, ct) ?? throw new NotFoundException($"Certificado {id} no encontrado.");

    private async Task<Carpeta> LoadActivaAsync(Guid id, CancellationToken ct)
    {
        var carpeta = await carpetas.GetAsync(id, ct) ?? throw new NotFoundException($"Carpeta {id} no encontrada.");
        return carpeta.Anulada
            ? throw new DomainException("La carpeta está anulada: restáurela desde «Anuladas» antes de modificarla.")
            : carpeta;
    }
}

/// <summary>Builds the Word document of a certificate request with the configured signers.</summary>
public sealed class DocumentoCertificadoService(
    CertificadosService certificados,
    FirmantesService firmantes,
    IGeneradorDocumentoCertificado generador)
{
    /// <exception cref="NotFoundException">The request does not exist.</exception>
    /// <exception cref="DomainException">A required value or signer is missing (message meant for the user).</exception>
    public async Task<DocumentoGenerado> GenerarAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await certificados.ObtenerAsync(id, ct);
        return generador.Generar(dto, await firmantes.ObtenerAsync(ct));
    }
}

/// <summary>Settings use cases for the signers (director and secretaría).</summary>
public sealed class FirmantesService(IFirmantesRepository repository, TimeProvider clock)
{
    public Task<Firmantes> ObtenerAsync(CancellationToken ct = default) => repository.ObtenerAsync(ct);

    /// <summary>Validates, normalizes and stores the signers (recording who changed them); returns what was stored.</summary>
    public async Task<Firmantes> GuardarAsync(Firmantes firmantes, string autor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(firmantes);
        var usuario = Autores.Requerir(autor);
        var normalizados = firmantes.Normalizar();
        await repository.GuardarAsync(normalizados, usuario, clock.GetUtcNow().UtcDateTime, ct);
        return normalizados;
    }
}
