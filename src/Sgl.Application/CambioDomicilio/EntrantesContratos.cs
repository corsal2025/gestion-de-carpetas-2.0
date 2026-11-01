using Sgl.Domain;

namespace Sgl.Application.CambioDomicilio;

/// <summary>Tabs of «Nos piden carpetas»: the open requests by sector (the sector comes from the last-carpeta date) and the closed ones.</summary>
public enum PestanaEntrantes
{
    /// <summary>Open, no last-carpeta date typed yet.</summary>
    PorClasificar = 0,
    Archivo = 1,
    Oficina43 = 2,
    Digital = 3,
    Cerradas = 4,
}

/// <summary>The exits offered when the carpeta is not found. A digital carpeta only gets «carpeta digital».</summary>
[Flags]
public enum SalidasEntrante
{
    Ninguna = 0,

    /// <summary>A: the person is in our base but the physical carpeta does not show up (F8).</summary>
    NoEstaLaCarpeta = 1,

    /// <summary>B: no record of the person in our base (mail to the comuna).</summary>
    SinRegistro = 2,

    /// <summary>C: the last carpeta is digital (mail to the comuna).</summary>
    CarpetaDigital = 4,
}

/// <summary>An incoming request as the repository reads it (the deadline and the sector are added by <see cref="EntrantesService"/>).</summary>
/// <param name="FechaCorreo">Chile date the email arrived (stored as the carpeta's citation date).</param>
/// <param name="UltimoCorreo">Latest mail we sent to the comuna about it (sin registro, carpeta digital or confirmation).</param>
public sealed record EntranteFila(
    Guid Id,
    string Rut,
    string NombreCompleto,
    string? Comuna,
    Sede Sede,
    DateOnly FechaCorreo,
    string? FechaUltimaCarpeta,
    EstadoCarpeta Estado,
    bool EnBusqueda,
    bool Encontrada,
    DateOnly? FechaSubida,
    DateOnly? FechaCierre,
    bool RevisarNombre,
    TipoEnvioCorreoComuna? UltimoCorreo,
    DateTime? UltimoCorreoEn,
    bool UltimoCorreoEsPrueba);

/// <summary>One row of «Nos piden carpetas».</summary>
/// <param name="Sector">«ARCHIVO», «OFICINA 43», «DIGITAL» or «—» while the last-carpeta date is not typed.</param>
/// <param name="Vence">Day the email arrived plus the business days of the estimate: an ESTIMATE (the other comuna may have been late).</param>
/// <param name="DiasRestantes">Calendar days until <paramref name="Vence"/>; negative when past.</param>
public sealed record EntranteDto(
    Guid Id,
    string Rut,
    string NombreCompleto,
    string? Comuna,
    Sede Sede,
    DateOnly FechaCorreo,
    string? FechaUltimaCarpeta,
    string Sector,
    EstadoCarpeta Estado,
    bool EnBusqueda,
    bool Encontrada,
    DateOnly? FechaSubida,
    DateOnly? FechaCierre,
    bool RevisarNombre,
    DateOnly Vence,
    int DiasRestantes,
    PlazoColor Plazo,
    TipoEnvioCorreoComuna? UltimoCorreo,
    DateTime? UltimoCorreoEn,
    bool UltimoCorreoEsPrueba)
{
    public bool Abierta => Estado == EstadoCarpeta.CambioDomicilio;

    public bool Digital => Sector == SectorFisico.Digital;

    public bool Fisica => Sector is SectorFisico.Archivo or SectorFisico.Oficina43;

    /// <summary>The exits offered for this request: a digital one only gets C; a physical one A or B; an unclassified one only B.</summary>
    public SalidasEntrante Salidas => !Abierta ? SalidasEntrante.Ninguna
        : Digital ? SalidasEntrante.CarpetaDigital
        : Fisica ? SalidasEntrante.NoEstaLaCarpeta | SalidasEntrante.SinRegistro
        : SalidasEntrante.SinRegistro;

    /// <summary>Closed with the carpeta uploaded (here, by F8 or by oficio): the comuna can be told «ya se subió».</summary>
    public bool Confirmable => Estado is EstadoCarpeta.CambioDomSubidoConaset or EstadoCarpeta.SubidaConF8 or EstadoCarpeta.SubidaConOficio;

    public bool Confirmada => UltimoCorreo == TipoEnvioCorreoComuna.ConfirmacionSubida;

    public PestanaEntrantes Pestana => !Abierta ? PestanaEntrantes.Cerradas
        : Digital ? PestanaEntrantes.Digital
        : Sector == SectorFisico.Archivo ? PestanaEntrantes.Archivo
        : Sector == SectorFisico.Oficina43 ? PestanaEntrantes.Oficina43
        : PestanaEntrantes.PorClasificar;
}

public sealed record ResumenEntrantes(int PorClasificar, int Archivo, int Oficina43, int Digital, int Cerradas, int Descartados)
{
    public int De(PestanaEntrantes pestana) => pestana switch
    {
        PestanaEntrantes.PorClasificar => PorClasificar,
        PestanaEntrantes.Archivo => Archivo,
        PestanaEntrantes.Oficina43 => Oficina43,
        PestanaEntrantes.Digital => Digital,
        _ => Cerradas,
    };
}

/// <summary>An email of the folder that did not become a carpeta, with the reason (subject and sender are truncated; never the body).</summary>
public sealed record DescartadoDto(long Id, string Asunto, string Remitente, DateTime RecibidoEn, string Motivo, DateTime DescartadoEn);

/// <summary>Result of mailing a comuna about an incoming request; <paramref name="Cerrada"/> is true when the request moved on after the mail.</summary>
public sealed record ResultadoRespuesta(ResultadoEnvioComuna Envio, bool Cerrada);

/// <summary>Read side of «Nos piden carpetas». Implemented in Infrastructure over the circuit's shared DbContext.</summary>
public interface IEntrantesRepository
{
    /// <summary>Open, not annulled incoming requests (state «Cambio de domicilio», direction entrante), oldest email first.</summary>
    Task<IReadOnlyList<EntranteFila>> ListarAbiertasAsync(CancellationToken ct = default);

    /// <summary>The latest closed incoming requests (uploaded, sin registro, carpeta digital), most recent first.</summary>
    Task<IReadOnlyList<EntranteFila>> ListarCerradasAsync(int max, CancellationToken ct = default);

    Task<int> ContarCerradasAsync(CancellationToken ct = default);

    Task<EntranteFila?> ObtenerAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<DescartadoDto>> ListarDescartadosAsync(CancellationToken ct = default);

    Task<int> ContarDescartadosAsync(CancellationToken ct = default);

    /// <summary>Removes one entry from the list of left-out emails (it would come back if the email is still in the folder).</summary>
    Task<bool> QuitarDescartadoAsync(long id, CancellationToken ct = default);
}
