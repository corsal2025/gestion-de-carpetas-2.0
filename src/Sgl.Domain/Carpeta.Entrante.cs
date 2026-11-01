namespace Sgl.Domain;

/// <summary>
/// «Nos piden carpetas»: another comuna asked us (by email) to upload the carpeta of someone who lived here. The sector comes from the
/// last-carpeta date a person looks up in SGL / CAS Chile; when the carpeta is not found there are three exits (A, B, C).
/// </summary>
public sealed partial class Carpeta
{
    /// <summary>History field of the mail that was sent to the comuna that asked for the carpeta.</summary>
    public const string CampoCorreoComuna = "Correo a la comuna";

    /// <summary>History field of the closing of an incoming request.</summary>
    public const string CampoSalidaEntrante = "Salida de la solicitud";

    /// <summary>Sector derived from the last-carpeta date: «ARCHIVO», «OFICINA 43», «DIGITAL» or «—» without a readable date.</summary>
    public string SectorPorFecha => SectorFisico.Calcular(FechaUltimaCarpeta);

    /// <summary>
    /// A person looked up the last-carpeta date (SGL / CAS Chile) and typed it: it decides the sector (Archivo, Of. 43 or Digital).
    /// It can be corrected while the request is open. A missing, unreadable or future date is refused.
    /// </summary>
    public void ClasificarEntrante(string? fecha, string autor, DateTime now)
    {
        RequireEntranteAbierta();
        var usuario = RequireAutor(autor);
        var iso = SectorFisico.NormalizarIso(fecha)
            ?? throw new DomainException("Indique una fecha de última carpeta válida (dd-mm-aaaa) que no sea futura.");
        ModificarFechaUltimaCarpeta(iso, usuario, now);
    }

    /// <summary>One touch of the magnifier on an incoming request with a physical carpeta (Archivo / Of. 43). No mail, no request to anyone.</summary>
    public void AvanzarLupaEntrante(string autor, DateTime now)
    {
        RequireEntranteAbierta();
        RequireSectorFisico("La lupa solo aplica a una carpeta física (Archivo u Of. 43): una digital o sin fecha no tiene qué buscar.");
        AvanzarLupa(autor, now);
    }

    /// <summary>«Subida a Conaset» of an incoming request: «Cambio dom. subido a Conaset» (never «Subida a Conaset»), with today's date.</summary>
    public void MarcarSubidaEntrante(string autor, DateTime now)
    {
        RequireEntranteAbierta();
        RequireSectorFisico("Solo se sube una carpeta con fecha de última carpeta en Archivo u Of. 43: una digital no tiene carpeta física.");
        CambiarEstado(EstadoCarpeta.CambioDomSubidoConaset, autor, now);
    }

    /// <summary>
    /// Exit A: the person is in our base (it has a last-carpeta date) but the physical carpeta does not show up. The carpeta goes to
    /// «No existe carpeta»; the caller opens the F8 request in the same save.
    /// </summary>
    public void MarcarNoEncontradaEntrante(string autor, DateTime now)
    {
        RequireEntranteAbierta();
        RequireSectorFisico("«No está la carpeta» solo aplica a una carpeta física (Archivo u Of. 43) de una persona que figura en nuestra base.");
        var usuario = RequireAutor(autor);
        CambiarEstado(EstadoCarpeta.NoExisteCarpeta, usuario, now);
        Audit(now, usuario, CampoSalidaEntrante, null, "No está la carpeta física: se pide el F8");
    }

    /// <summary>
    /// Exit B: no record of the person in our base. Not offered for a digital carpeta (that one only receives the «carpeta digital» mail).
    /// <paramref name="comunaSgl"/> is the comuna where the person does appear when the user saw it in SGL; it only goes to the history.
    /// </summary>
    public void CerrarEntranteSinRegistro(string? comunaSgl, string autor, DateTime now)
    {
        RequireEntranteAbierta();
        if (SectorPorFecha == SectorFisico.Digital)
        {
            throw new DomainException("Con una última carpeta digital solo corresponde la respuesta «carpeta digital», no «sin registro».");
        }

        var usuario = RequireAutor(autor);
        CambiarEstado(EstadoCarpeta.CambioDomSinRegistro, usuario, now);
        var comuna = comunaSgl?.Trim();
        Audit(now, usuario, CampoSalidaEntrante, null,
            string.IsNullOrEmpty(comuna) ? "Sin registro en nuestra base" : $"Sin registro en nuestra base; figura en {comuna}");
    }

    /// <summary>Exit C: the last carpeta is on or after the digital cut-off, so there is no paper to look for.</summary>
    public void CerrarEntranteCarpetaDigital(string autor, DateTime now)
    {
        RequireEntranteAbierta();
        if (SectorPorFecha != SectorFisico.Digital)
        {
            throw new DomainException("La respuesta «carpeta digital» solo corresponde con una última carpeta desde el corte digital.");
        }

        var usuario = RequireAutor(autor);
        CambiarEstado(EstadoCarpeta.CambioDomCarpetaDigital, usuario, now);
        Audit(now, usuario, CampoSalidaEntrante, null, $"Carpeta digital: última carpeta {FechaUltimaCarpeta}");
    }

    private void RequireEntranteAbierta()
    {
        RequireActiva();
        if (Estado != EstadoCarpeta.CambioDomicilio || SentidoCambioDomicilio != Domain.SentidoCambioDomicilio.Entrante)
        {
            throw new DomainException("La solicitud ya no está abierta en «Nos piden carpetas»: actualice la lista.");
        }
        RequireReclasificada();
    }

    private void RequireSectorFisico(string mensaje)
    {
        if (SectorPorFecha is not (SectorFisico.Archivo or SectorFisico.Oficina43))
        {
            throw new DomainException(mensaje);
        }
    }
}
