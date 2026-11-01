using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Asistencias;

/// <summary>
/// "Asistencia del día": after 13:00 the paper with the people who came is typed here by RUT. Search reuses the global
/// search; marking is permanent (undoing only annuls). Every read and write goes through the circuit's gate.
/// </summary>
public sealed class AsistenciaService(
    ICarpetaRepository carpetas,
    IAsistenciaRepository asistencias,
    CarpetaService busqueda,
    TimeProvider clock,
    UnidadDeTrabajoGate? gate = null)
{
    private readonly UnidadDeTrabajoGate _gate = gate ?? new UnidadDeTrabajoGate();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Searches one RUT: appointment of today, other carpetas of the person (any tray) and today's marks.</summary>
    public async Task<BusquedaAsistenciaDto> BuscarAsync(string? texto, CancellationToken ct = default)
    {
        var rut = Parsear(texto);
        // The global search takes the gate by itself, so it runs before this service takes it.
        var globales = await busqueda.BuscarGlobalAsync(rut.Formatted, ct);
        var (contexto, abierta) = await _gate.LeerAsync(async () =>
        {
            var c = await asistencias.GetContextoAsync(rut.Value, clock.HoyChile(), ct);
            var ciudadano = await carpetas.FindCiudadanoAsync(rut, ct);
            var tiene = ciudadano is not null && (await carpetas.GetCarpetasAbiertasSinArchivarAsync(ciudadano.Id, ct)).Count > 0;
            return (c, tiene);
        }, ct);
        var delRut = globales.Where(g => g.Rut == rut.Formatted).ToList();
        return new BusquedaAsistenciaDto(
            rut.Formatted,
            contexto.Existe,
            contexto.Nombre ?? delRut.FirstOrDefault()?.NombreCompleto,
            contexto.CitaHoy,
            delRut.Where(g => g.Id != contexto.CitaHoy?.CarpetaId).ToList(),
            contexto.Marcada,
            contexto.PorAgregar)
        {
            TieneCarpetaAbierta = abierta,
        };
    }

    /// <summary>"Vino": marks the person's appointment of today. A pending "por agregar" row of the RUT is resolved.</summary>
    public Task<AsistenciaDto> MarcarVinoAsync(string? rutTexto, string autor, CancellationToken ct = default) =>
        EscribirAsync(async () =>
        {
            var rut = Parsear(rutTexto);
            var usuario = Autores.Requerir(autor);
            var hoy = clock.HoyChile();
            var contexto = await asistencias.GetContextoAsync(rut.Value, hoy, ct);
            if (contexto.Marcada is not null)
            {
                throw new DomainException("Esta persona ya fue marcada hoy.");
            }

            var cita = contexto.CitaHoy ?? throw new DomainException("No tiene cita agendada hoy: agréguela como extra.");
            var carpeta = await carpetas.GetAsync(cita.CarpetaId, ct) ?? throw new NotFoundException("Carpeta no encontrada.");
            var fila = Asistencia.Vino(carpeta.Id, rut, carpeta.Sede, hoy, usuario, Now);
            await ResolverPorAgregarAsync(rut, usuario, ct);
            await asistencias.AddAsync(fila, ct);
            carpeta.RegistrarEvento("Asistencia", null, "Vino", usuario, Now);
            await carpetas.SaveChangesAsync(ct);
            return Dto(fila, contexto.Nombre);
        }, ct);

    /// <summary>"Agregar como extra": creates the carpeta in Por clasificar (and the citizen when the RUT is new) and marks it.</summary>
    public Task<AsistenciaDto> AgregarExtraAsync(AgregarExtraCommand cmd, CancellationToken ct = default) =>
        EscribirAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(cmd);
            var rut = Parsear(cmd.Rut);
            var usuario = Autores.Requerir(cmd.Autor);
            var hoy = clock.HoyChile();
            var contexto = await asistencias.GetContextoAsync(rut.Value, hoy, ct);
            if (contexto.Marcada is not null)
            {
                throw new DomainException("Esta persona ya fue marcada hoy.");
            }
            if (contexto.CitaHoy is not null)
            {
                throw new DomainException("Esta persona tiene cita hoy: márquela con «Vino».");
            }

            var ciudadano = await carpetas.FindCiudadanoAsync(rut, ct);
            if (ciudadano is null)
            {
                if (string.IsNullOrWhiteSpace(cmd.Nombre))
                {
                    throw new DomainException("Escriba el nombre completo de la persona (como está en el papel).");
                }

                var (nombre, apellido) = Ciudadano.Separar(cmd.Nombre);
                ciudadano = new Ciudadano(rut, nombre, apellido);
            }
            else if ((await carpetas.GetCarpetasAbiertasSinArchivarAsync(ciudadano.Id, ct)).Count > 0)
            {
                // A second open carpeta would land in Revisión · Duplicadas: the existing one is the one to work.
                throw new DomainException("Esta persona ya tiene una carpeta abierta: no se crea otra. Ábrala desde «Otras carpetas de esta persona».");
            }

            var carpeta = Carpeta.CrearExtra(ciudadano, cmd.Sede, hoy, cmd.Tramite, TramitesExtra.AgendaDe(cmd.Tramite), usuario, Now);
            var fila = Asistencia.Extra(carpeta.Id, rut, cmd.Sede, hoy, usuario, Now);
            await ResolverPorAgregarAsync(rut, usuario, ct);
            await carpetas.AddAsync(carpeta, ct);
            await asistencias.AddAsync(fila, ct);
            await carpetas.SaveChangesAsync(ct);
            return Dto(fila, ciudadano.NombreCompleto);
        }, ct);

    /// <summary>"Dejar para después" (the X of the paper): the person goes to the "por agregar" list.</summary>
    public Task<AsistenciaDto> DejarParaDespuesAsync(string? rutTexto, Sede sede, string autor, CancellationToken ct = default) =>
        EscribirAsync(async () =>
        {
            var rut = Parsear(rutTexto);
            var usuario = Autores.Requerir(autor);
            var hoy = clock.HoyChile();
            var contexto = await asistencias.GetContextoAsync(rut.Value, hoy, ct);
            if (contexto.Marcada is not null)
            {
                throw new DomainException("Esta persona ya fue marcada hoy.");
            }
            if (contexto.PorAgregar is not null)
            {
                throw new DomainException("Esta persona ya está en la lista «Por agregar».");
            }

            var fila = Asistencia.PorAgregar(rut, sede, hoy, usuario, Now);
            await asistencias.AddAsync(fila, ct);
            await carpetas.SaveChangesAsync(ct);
            return Dto(fila, contexto.Nombre);
        }, ct);

    /// <summary>"Deshacer": annuls the row (never deletes). An extra also cancels its carpeta while it is still unclassified.</summary>
    public Task DeshacerAsync(Guid asistenciaId, string autor, CancellationToken ct = default) =>
        EscribirAsync<object?>(async () =>
        {
            var usuario = Autores.Requerir(autor);
            var fila = await asistencias.GetAsync(asistenciaId, ct) ?? throw new NotFoundException("Asistencia no encontrada.");
            if (fila.Anulada)
            {
                throw new DomainException("La asistencia ya estaba deshecha.");
            }

            if (fila.Tipo != TipoAsistencia.PorAgregar && fila.CarpetaId is { } carpetaId)
            {
                var carpeta = await carpetas.GetAsync(carpetaId, ct) ?? throw new NotFoundException("Carpeta no encontrada.");
                if (fila.Tipo == TipoAsistencia.Extra)
                {
                    if (carpeta.Estado != EstadoCarpeta.PorClasificar)
                    {
                        throw new DomainException("La carpeta de esta extra ya fue clasificada: no se puede deshacer desde aquí.");
                    }
                    if (!carpeta.Anulada)
                    {
                        carpeta.Anular("Extra deshecha", usuario, Now);
                    }
                }
                else
                {
                    carpeta.RegistrarEvento("Asistencia", "Vino", "Deshecho", usuario, Now);
                }
            }

            fila.Anular(usuario, Now);
            await carpetas.SaveChangesAsync(ct);
            return null;
        }, ct);

    /// <summary>Counters, today's marks (newest first) and the "por agregar" list.</summary>
    public Task<ResumenAsistenciaDto> ObtenerResumenAsync(CancellationToken ct = default) =>
        _gate.LeerAsync(() => asistencias.GetResumenAsync(clock.HoyChile(), ct), ct);

    private static Rut Parsear(string? texto) =>
        Rut.Validar(texto, out var rut) is { } error ? throw new DomainException(error) : rut!;

    private async Task ResolverPorAgregarAsync(Rut rut, string usuario, CancellationToken ct)
    {
        foreach (var pendiente in await asistencias.GetPorAgregarVigentesAsync(rut.Value, ct))
        {
            pendiente.Anular(usuario, Now);
        }
    }

    private AsistenciaDto Dto(Asistencia a, string? nombre) =>
        new(a.Id, a.Tipo, Rut.Parse(a.Rut).Formatted, nombre, a.Sede, a.Fecha, HoraChile.ALocal(a.RegistradaEn), a.Autor, a.CarpetaId);

    private async Task<T> EscribirAsync<T>(Func<Task<T>> operacion, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await operacion();
        }
        catch
        {
            // A failed operation must not leave tracked changes for a later save in the same circuit.
            carpetas.DescartarCambios();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
