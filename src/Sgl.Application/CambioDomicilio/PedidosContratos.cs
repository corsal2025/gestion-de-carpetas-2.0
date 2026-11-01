using Sgl.Domain;

namespace Sgl.Application.CambioDomicilio;

/// <summary>One saliente waiting in «Por pedir» (change of address asked by a person of our agenda).</summary>
/// <param name="ClasificadaEn">Chile date on which it entered «Por pedir» (the day it was classified).</param>
/// <param name="DeDiaAnterior">It should have gone out the same day: it waited since an earlier day.</param>
/// <param name="Revisada">Someone checked it in Conaset and left it «Pendiente».</param>
/// <param name="FalloEnvio">Its last request mail failed: it must be retried.</param>
public sealed record PorPedirDto(
    Guid Id,
    string Rut,
    string NombreCompleto,
    string? Comuna,
    Sede Sede,
    DateOnly FechaCitacion,
    DateOnly ClasificadaEn,
    bool DeDiaAnterior,
    bool Revisada,
    bool FalloEnvio,
    string? FechaUltimaCarpeta);

public sealed record PersonaPedidoDto(Guid Id, string Rut, string NombreCompleto, bool Revisada, bool FalloEnvio);

/// <summary>Preview of one mail: the comuna, the addresses it would go to and the people it names.</summary>
public sealed record GrupoPedidoDto(string Comuna, IReadOnlyList<string> Correos, IReadOnlyList<PersonaPedidoDto> Personas)
{
    public bool SinComuna => Comuna.Length == 0;

    /// <summary>No address in the directory: the group stays out of the send until one is added.</summary>
    public bool SinCorreo => !SinComuna && Correos.Count == 0;

    public bool Enviable => !SinComuna && !SinCorreo;
}

/// <summary>A state change with the point it can be undone to.</summary>
public sealed record CambioRealizado(PuntoRestauracionEstado Punto, EstadoCarpeta EstadoNuevo);

/// <summary>Result of the mass send: one outcome per comuna and how many carpetas moved to «Solicitado».</summary>
public sealed record ResultadoPedidos(IReadOnlyList<ResultadoEnvioComuna> Envios, int CarpetasPedidas);

/// <summary>Read side of «Pedimos carpetas». Implemented in Infrastructure over the circuit's shared DbContext.</summary>
public interface IPedidosRepository
{
    /// <summary>Open, not annulled saliente carpetas in «cambio de domicilio», oldest classification first.</summary>
    Task<IReadOnlyList<PorPedirDto>> ListarPorPedirAsync(DateOnly hoy, CancellationToken ct = default);

    Task<int> ContarPorPedirAsync(CancellationToken ct = default);

    /// <summary>For each carpeta that has a sent request mail (<see cref="TipoEnvioCorreoComuna.Pedido"/>), the comuna of the latest one.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ComunasPedidasAsync(IReadOnlyCollection<Guid> carpetaIds, CancellationToken ct = default);
}

/// <summary>One row of the directory shown in «Comunas».</summary>
public sealed record ContactoComunaDto(string Comuna, string Correo, string Dominio);

/// <summary>The directory of comunas and their mailboxes (adapter over the Peticiones directory).</summary>
public interface IDirectorioComunas
{
    /// <summary>Addresses of a comuna in the directory; empty when none.</summary>
    IReadOnlyList<string> CorreosDe(string? comuna);

    /// <summary>The comuna as the directory names it (typos and accents resolved), or the trimmed text; empty if none.</summary>
    string NombreDe(string? comuna);

    /// <summary>Creates the comuna or adds an address to it, right now.</summary>
    (bool Ok, string Mensaje) AgregarCorreo(string comuna, string correo);

    /// <summary>Every comuna with its address, ordered by comuna.</summary>
    IReadOnlyList<ContactoComunaDto> Listar();

    /// <summary>Creates a comuna with its (only) address; refused when the comuna already has one: edit it instead.</summary>
    (bool Ok, string Mensaje) CrearComuna(string comuna, string correo);

    (bool Ok, string Mensaje) EditarCorreo(string comuna, string correoViejo, string correoNuevo);

    (bool Ok, string Mensaje) Eliminar(string comuna, string correo);

    /// <summary>Comunas of Chile that are not in the directory yet (to add their address by hand).</summary>
    IReadOnlyList<string> ComunasSinCorreo();
}
