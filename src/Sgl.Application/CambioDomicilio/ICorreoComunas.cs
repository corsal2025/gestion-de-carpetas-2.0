using Sgl.Domain;

namespace Sgl.Application.CambioDomicilio;

/// <summary>Settings section «CambioDomicilio». The real send is OFF by default: nothing reaches a comuna until it is switched on.</summary>
public sealed class CambioDomicilioOptions
{
    public const string SectionName = "CambioDomicilio";

    /// <summary>Starting value of the switch; the value saved in the database (Configuracion) wins once someone changes it.</summary>
    public bool EnvioReal { get; init; }

    /// <summary>Business days the other comuna has to upload the carpeta of one of our people (counted from the citation).</summary>
    public int PlazoSalienteDias { get; init; } = 15;

    /// <summary>Estimated business days for an incoming request (counted from the day the email arrived).</summary>
    public int PlazoEntranteDias { get; init; } = 12;

    /// <summary>Folder for the pasted screenshots, outside wwwroot.</summary>
    public string CarpetaPantallazos { get; init; } = "../../datos/pantallazos";

    public int PantallazoMaxKB { get; init; } = 5120;
}

/// <summary>How the next send will behave: real, test mailbox, or simulated (test without a mailbox).</summary>
public sealed record ModoEnvio(bool EnvioReal, bool CasillaDePrueba)
{
    /// <summary>True when nothing goes to a comuna: the switch is off or a test mailbox is configured.</summary>
    public bool EsPrueba => !EnvioReal || CasillaDePrueba;
}

public enum ResultadoEnvio
{
    Enviado = 1,
    Fallido = 2,

    /// <summary>The comuna has no email in the directory: nothing was sent or recorded; add it and retry.</summary>
    SinCorreo = 3,

    /// <summary>The carpeta has no comuna to write to.</summary>
    SinComuna = 4,

    /// <summary>Every carpeta of the group already has a mail of that type (sent or being sent): nothing new went out.</summary>
    YaEnviado = 5,

    /// <summary>The mail transport is not configured: nothing was sent or recorded.</summary>
    CorreoNoConfigurado = 6,
}

/// <summary>Outcome for one comuna. <see cref="Mensaje"/> never contains names, RUT or the body.</summary>
public sealed record ResultadoEnvioComuna(
    string Comuna,
    ResultadoEnvio Resultado,
    Guid? EnvioId,
    IReadOnlyList<Guid> CarpetaIds,
    bool EsPrueba,
    bool Simulado,
    string Mensaje);

/// <summary>Request to mail one of the department's templates about one carpeta (to the comuna that wrote to us).</summary>
/// <param name="Fecha">Value of {fecha} (last-carpeta date).</param>
/// <param name="ComunaSgl">Value of {comuna_sgl}; the whole line disappears when empty.</param>
/// <param name="Asunto">Edited subject; null = the stored template.</param>
/// <param name="Cuerpo">Edited text, already filled in; null = the stored template rendered with the data.</param>
public sealed record SolicitudCorreoPlantilla(
    TipoEnvioCorreoComuna Tipo,
    Guid CarpetaId,
    string Autor,
    string? Fecha = null,
    string? ComunaSgl = null,
    string? Asunto = null,
    string? Cuerpo = null);

public sealed record PlantillaCorreoDto(string Clave, string Asunto, string Cuerpo);

public sealed record TextoCorreo(string Asunto, string Cuerpo);

/// <summary>
/// Mails to the comunas (port; adapter in Infrastructure). One mail per comuna; a failed mail does not stop the others and
/// only that one is retried. Every attempt is recorded (<see cref="EnvioCorreoComuna"/>).
/// </summary>
public interface ICorreoComunas
{
    /// <summary>Mails the request of the carpetas (<see cref="TipoEnvioCorreoComuna.Pedido"/> or <see cref="TipoEnvioCorreoComuna.Reenvio"/>), one mail per comuna.</summary>
    Task<IReadOnlyList<ResultadoEnvioComuna>> EnviarAsync(
        TipoEnvioCorreoComuna tipo, IReadOnlyCollection<Guid> carpetaIds, string autor, CancellationToken ct = default);

    /// <summary>Mails a template (B «sin registro», C «carpeta digital» or an explicit text) about one carpeta.</summary>
    Task<ResultadoEnvioComuna> EnviarPlantillaAsync(SolicitudCorreoPlantilla solicitud, CancellationToken ct = default);

    /// <summary>Stored template, or the approved default text when nobody edited it.</summary>
    Task<PlantillaCorreoDto> ObtenerPlantillaAsync(string clave, CancellationToken ct = default);

    Task GuardarPlantillaAsync(string clave, string asunto, string cuerpo, CancellationToken ct = default);

    /// <summary>The template filled in with the person's data (no mail is sent).</summary>
    Task<TextoCorreo> VistaPreviaAsync(
        string clave, string nombre, string rut, string? fecha, string? comunaSgl, CancellationToken ct = default);

    Task<ModoEnvio> ObtenerModoAsync(CancellationToken ct = default);

    Task EstablecerEnvioRealAsync(bool activo, CancellationToken ct = default);
}
