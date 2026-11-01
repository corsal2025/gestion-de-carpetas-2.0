namespace Sgl.Domain;

/// <summary>What a mail to a comuna is about.</summary>
public enum TipoEnvioCorreoComuna
{
    /// <summary>First request of the carpetas to the other comuna (fixed subject and text).</summary>
    Pedido = 1,

    /// <summary>Same mail repeated as a reminder (manual «volver a pedir»).</summary>
    Reenvio = 2,

    /// <summary>Answer to an incoming request: the person has no record in our municipality (template B).</summary>
    SinRegistro = 3,

    /// <summary>Answer to an incoming request: the carpeta is digital (template C).</summary>
    Digital = 4,

    /// <summary>Confirmation to the comuna that the carpeta it asked for was uploaded.</summary>
    ConfirmacionSubida = 5,
}

public enum EstadoEnvioCorreoComuna
{
    /// <summary>Reserved by a send in progress: a second click or operator finds it and does not mail again.</summary>
    Enviando = 1,

    Enviado = 2,

    Fallido = 3,
}

/// <summary>
/// One mail to one comuna and the carpetas it covers (table <c>EnviosCorreoComuna</c> + link table). It is the record of what
/// was asked, when and to whom; the screenshot proving the mail is attached later. A test send (<see cref="EsPrueba"/>) is
/// recorded like a real one: it went to the test mailbox, never to the comuna.
/// </summary>
public sealed class EnvioCorreoComuna
{
    public const int MaxComunaLength = 100;
    public const int MaxDestinatariosLength = 1000;
    public const int MaxAsuntoLength = 200;
    public const int MaxCuerpoLength = 8000;
    public const int MaxAutorLength = Carpeta.MaxUsuarioLength;
    public const int MaxErrorLength = 200;
    public const int MaxNotaLength = 200;
    public const int MaxPantallazoLength = 100;

    /// <summary>Start of the <see cref="Nota"/> of a send that never left the system (test without a mailbox): there is no mail to capture.</summary>
    public const string PrefijoSimulado = "simulado";

    private readonly List<EnvioCorreoComunaCarpeta> _carpetas = [];

    public Guid Id { get; private set; }

    public TipoEnvioCorreoComuna Tipo { get; private set; }

    public string Comuna { get; private set; } = string.Empty;

    /// <summary>Addresses of the comuna the mail is meant for (also for a test send, where it did NOT go there).</summary>
    public string Destinatarios { get; private set; } = string.Empty;

    /// <summary>Subject as written for the comuna (without the [PRUEBA] prefix a test send adds).</summary>
    public string Asunto { get; private set; } = string.Empty;

    public string Cuerpo { get; private set; } = string.Empty;

    public string Autor { get; private set; } = string.Empty;

    /// <summary>UTC. Reservation time while <see cref="EstadoEnvioCorreoComuna.Enviando"/>; moment of the result afterwards.</summary>
    public DateTime EnviadoEn { get; private set; }

    public bool EsPrueba { get; private set; }

    public EstadoEnvioCorreoComuna Estado { get; private set; }

    /// <summary>Generic text of the failure (never the exception message: it could carry addresses).</summary>
    public string? Error { get; private set; }

    /// <summary>Remark about a non-failed send, e.g. «simulado» when there was no test mailbox and nothing left the system.</summary>
    public string? Nota { get; private set; }

    /// <summary>File name of the pasted screenshot of the sent mail (set in a later stage).</summary>
    public string? PantallazoArchivo { get; private set; }

    public DateTime? PantallazoEn { get; private set; }

    public IReadOnlyList<EnvioCorreoComunaCarpeta> Carpetas => _carpetas;

    private EnvioCorreoComuna()
    {
    }

    /// <summary>Reserves the send (state «Enviando») for the given carpetas.</summary>
    public static EnvioCorreoComuna Reservar(
        TipoEnvioCorreoComuna tipo, string comuna, string destinatarios, string asunto, string cuerpo,
        string autor, bool esPrueba, IEnumerable<Guid> carpetaIds, DateTime ahoraUtc)
    {
        var ids = carpetaIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new DomainException("El envío necesita al menos una carpeta.");
        }
        if (string.IsNullOrWhiteSpace(comuna))
        {
            throw new DomainException("La comuna del envío es obligatoria.");
        }

        var envio = new EnvioCorreoComuna
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            Comuna = Recortar(comuna.Trim(), MaxComunaLength),
            Destinatarios = Recortar(destinatarios, MaxDestinatariosLength),
            Asunto = Recortar(asunto, MaxAsuntoLength),
            Cuerpo = Recortar(cuerpo, MaxCuerpoLength),
            Autor = Recortar(string.IsNullOrWhiteSpace(autor) ? "Sistema" : autor.Trim(), MaxAutorLength),
            EnviadoEn = ahoraUtc,
            EsPrueba = esPrueba,
            Estado = EstadoEnvioCorreoComuna.Enviando,
        };
        envio._carpetas.AddRange(ids.Select(c => new EnvioCorreoComunaCarpeta(envio.Id, c)));
        return envio;
    }

    public void MarcarEnviado(DateTime ahoraUtc, string? nota = null)
    {
        EnviadoEn = ahoraUtc;
        Estado = EstadoEnvioCorreoComuna.Enviado;
        Error = null;
        Nota = nota is null ? null : Recortar(nota, MaxNotaLength);
    }

    public void MarcarFallido(string error, DateTime ahoraUtc)
    {
        EnviadoEn = ahoraUtc;
        Estado = EstadoEnvioCorreoComuna.Fallido;
        Error = Recortar(error, MaxErrorLength);
    }

    /// <summary>
    /// Attaches the screenshot (an image of the mail the user captured in Outlook) to this mail, and so to every carpeta it covers.
    /// A second screenshot replaces the first. Only a mail that actually went out has something to capture.
    /// </summary>
    public void AdjuntarPantallazo(string archivo, DateTime ahoraUtc)
    {
        if (Estado != EstadoEnvioCorreoComuna.Enviado)
        {
            throw new DomainException("Solo se adjunta el pantallazo de un correo que ya se envió.");
        }
        if (string.IsNullOrWhiteSpace(archivo) || archivo.Length > MaxPantallazoLength)
        {
            throw new DomainException("El nombre del archivo del pantallazo no es válido.");
        }

        PantallazoArchivo = archivo;
        PantallazoEn = ahoraUtc;
    }

    private static string Recortar(string texto, int max) => texto.Length > max ? texto[..max] : texto;
}

/// <summary>A carpeta covered by a mail (composite key: one row per carpeta and mail).</summary>
public sealed class EnvioCorreoComunaCarpeta
{
    public Guid EnvioId { get; private set; }

    public Guid CarpetaId { get; private set; }

    private EnvioCorreoComunaCarpeta()
    {
    }

    internal EnvioCorreoComunaCarpeta(Guid envioId, Guid carpetaId)
    {
        EnvioId = envioId;
        CarpetaId = carpetaId;
    }
}

/// <summary>Editable text of a mail written by the department (template B «sin registro», template C «carpeta digital»).</summary>
public sealed class PlantillaCorreo
{
    public const int MaxClaveLength = 50;
    public const int MaxAsuntoLength = 200;
    public const int MaxCuerpoLength = 4000;

    public string Clave { get; private set; } = string.Empty;

    public string Asunto { get; private set; } = string.Empty;

    public string Cuerpo { get; private set; } = string.Empty;

    private PlantillaCorreo()
    {
    }

    public PlantillaCorreo(string clave, string asunto, string cuerpo)
    {
        Clave = clave;
        Actualizar(asunto, cuerpo);
    }

    public void Actualizar(string asunto, string cuerpo)
    {
        asunto = (asunto ?? string.Empty).Trim();
        cuerpo = (cuerpo ?? string.Empty).Trim();
        if (asunto.Length == 0 || cuerpo.Length == 0)
        {
            throw new DomainException("La plantilla necesita asunto y texto.");
        }
        if (asunto.Length > MaxAsuntoLength || cuerpo.Length > MaxCuerpoLength)
        {
            throw new DomainException($"La plantilla admite hasta {MaxAsuntoLength} caracteres de asunto y {MaxCuerpoLength} de texto.");
        }

        Asunto = asunto;
        Cuerpo = cuerpo;
    }
}
