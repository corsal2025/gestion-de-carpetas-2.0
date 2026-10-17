using System.Globalization;
using System.Text.Json;

namespace Sgl.Domain;

public enum TipoCertificado
{
    /// <summary>Certificate that the carpeta does not exist; opened automatically when an F8 was not generated.</summary>
    SinCarpeta = 1,

    /// <summary>Certificate requested from the Registro Civil (manual).</summary>
    RegistroCivil = 2,

    /// <summary>Oficio (manual).</summary>
    Oficio = 3,
}

public enum OrigenCertificado
{
    DesdeF8 = 1,
    Manual = 2,
}

public enum EstadoCertificado
{
    PorFirmar = 1,
    Firmado = 2,
}

/// <summary>
/// A certificate / oficio request (module "Certificado / Oficio"). There is no "cannot issue" state: it is either
/// waiting for the signature or signed. At most one open request per carpeta and tipo (service + unique filtered index).
/// </summary>
public sealed class SolicitudCertificado
{
    public const int MaxParametros = 10;
    public const int MaxClaveParametro = 40;
    public const int MaxValorParametro = 200;
    public const int MaxNumeroOficioLength = 50;
    public const int MaxObservacionLength = 500;

    public const string ClaveAnioRenovacion = "AnioRenovacion";

    private const int AnioMinimo = 1900;
    private const int AnioMaximo = 2100;

    public Guid Id { get; private set; }

    public Guid CarpetaId { get; private set; }

    public TipoCertificado Tipo { get; private set; }

    public OrigenCertificado Origen { get; private set; }

    /// <summary>The F8 request this certificate was opened from (only when <see cref="Origen"/> is DesdeF8).</summary>
    public Guid? SolicitudF8Id { get; private set; }

    public string SolicitadoPor { get; private set; } = string.Empty;

    /// <summary>UTC instant of the request; shown in Chile time.</summary>
    public DateTime SolicitadoEn { get; private set; }

    public EstadoCertificado Estado { get; private set; }

    public string? FirmadoPor { get; private set; }

    public DateTime? FirmadoEn { get; private set; }

    public string? NumeroOficio { get; private set; }

    /// <summary>Small key/value bag stored as JSON (e.g. AnioRenovacion; Clase, Ley, FechaOtorgamiento; Clase, Motivo).</summary>
    public string ParametrosJson { get; private set; } = "{}";

    /// <summary>Set only when a mail adapter really sent the notification to the director.</summary>
    public DateTime? EnviadoADirectorEn { get; private set; }

    public string? Observacion { get; private set; }

    public bool EstaAbierta => Estado == EstadoCertificado.PorFirmar;

    public IReadOnlyDictionary<string, string> Parametros =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(ParametrosJson) ?? [];

    private SolicitudCertificado()
    {
    }

    /// <summary>Opens the SinCarpeta certificate that follows an F8 that could not be generated.</summary>
    public static SolicitudCertificado CrearDesdeF8(Guid carpetaId, Guid solicitudF8Id, string autor, DateTime now)
    {
        if (solicitudF8Id == Guid.Empty)
        {
            throw new DomainException("El certificado «sin carpeta» nace de una solicitud de F8.");
        }

        return Nuevo(carpetaId, TipoCertificado.SinCarpeta, OrigenCertificado.DesdeF8, solicitudF8Id, null, null, autor, now);
    }

    /// <summary>Manual request (Registro Civil or Oficio).</summary>
    public static SolicitudCertificado CrearManual(
        Guid carpetaId,
        TipoCertificado tipo,
        IReadOnlyDictionary<string, string>? parametros,
        string? numeroOficio,
        string autor,
        DateTime now)
    {
        if (tipo == TipoCertificado.SinCarpeta)
        {
            throw new DomainException("El certificado «sin carpeta» solo se genera desde un F8 que no se generó.");
        }

        return Nuevo(carpetaId, tipo, OrigenCertificado.Manual, null, parametros, numeroOficio, autor, now);
    }

    /// <summary>Edits the data of a certificate that is still waiting for the signature.</summary>
    public void ActualizarDatos(IReadOnlyDictionary<string, string>? parametros, string? numeroOficio, string? observacion)
    {
        RequireAbierta();
        var nota = NormalizarObservacion(observacion);
        var json = SerializarParametros(parametros);
        var numero = NormalizarNumeroOficio(Tipo, numeroOficio);

        ParametrosJson = json;
        NumeroOficio = numero ?? NumeroOficio;
        Observacion = nota;
    }

    public void MarcarFirmado(string autor, DateTime now, string? numeroOficio = null)
    {
        RequireAbierta();
        var usuario = Autores.Requerir(autor);
        var numero = NormalizarNumeroOficio(Tipo, numeroOficio);

        Estado = EstadoCertificado.Firmado;
        FirmadoPor = usuario;
        FirmadoEn = now;
        NumeroOficio = numero ?? NumeroOficio;
    }

    /// <summary>Records that the notification reached the director. Idempotent: the first send wins.</summary>
    public void RegistrarEnvioADirector(DateTime now) => EnviadoADirectorEn ??= now;

    private static SolicitudCertificado Nuevo(
        Guid carpetaId,
        TipoCertificado tipo,
        OrigenCertificado origen,
        Guid? solicitudF8Id,
        IReadOnlyDictionary<string, string>? parametros,
        string? numeroOficio,
        string autor,
        DateTime now)
    {
        if (carpetaId == Guid.Empty)
        {
            throw new DomainException("La carpeta es obligatoria.");
        }
        if (!Enum.IsDefined(tipo))
        {
            throw new DomainException("Tipo de certificado inválido.");
        }

        return new SolicitudCertificado
        {
            Id = Guid.NewGuid(),
            CarpetaId = carpetaId,
            Tipo = tipo,
            Origen = origen,
            SolicitudF8Id = solicitudF8Id,
            SolicitadoPor = Autores.Requerir(autor),
            SolicitadoEn = now,
            Estado = EstadoCertificado.PorFirmar,
            ParametrosJson = SerializarParametros(parametros),
            NumeroOficio = NormalizarNumeroOficio(tipo, numeroOficio),
        };
    }

    private void RequireAbierta()
    {
        if (!EstaAbierta)
        {
            throw new DomainException("El certificado ya fue firmado.");
        }
    }

    private static string? NormalizarNumeroOficio(TipoCertificado tipo, string? numero)
    {
        if (string.IsNullOrWhiteSpace(numero))
        {
            return null;
        }
        if (tipo != TipoCertificado.Oficio)
        {
            throw new DomainException("El número de oficio solo corresponde a un oficio.");
        }

        var limpio = numero.Trim();
        return limpio.Length > MaxNumeroOficioLength
            ? throw new DomainException($"El número de oficio excede {MaxNumeroOficioLength} caracteres.")
            : limpio;
    }

    private static string? NormalizarObservacion(string? observacion)
    {
        var nota = string.IsNullOrWhiteSpace(observacion) ? null : observacion.Trim();
        return nota?.Length > MaxObservacionLength
            ? throw new DomainException($"La observación excede {MaxObservacionLength} caracteres.")
            : nota;
    }

    private static string SerializarParametros(IReadOnlyDictionary<string, string>? parametros)
    {
        var limpios = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (clave, valor) in parametros ?? new Dictionary<string, string>())
        {
            var k = clave?.Trim() ?? string.Empty;
            if (k.Length == 0 || k.Length > MaxClaveParametro)
            {
                throw new DomainException($"La clave de un parámetro es obligatoria y no excede {MaxClaveParametro} caracteres.");
            }
            var v = valor?.Trim() ?? string.Empty;
            if (v.Length == 0)
            {
                continue;
            }
            if (v.Length > MaxValorParametro)
            {
                throw new DomainException($"El valor de «{k}» excede {MaxValorParametro} caracteres.");
            }
            limpios[k] = v;
        }

        if (limpios.Count > MaxParametros)
        {
            throw new DomainException($"Un certificado admite como máximo {MaxParametros} parámetros.");
        }
        if (limpios.TryGetValue(ClaveAnioRenovacion, out var anio)
            && !(int.TryParse(anio, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= AnioMinimo and <= AnioMaximo))
        {
            throw new DomainException($"El año de renovación debe estar entre {AnioMinimo} y {AnioMaximo}.");
        }

        return JsonSerializer.Serialize(limpios);
    }
}
