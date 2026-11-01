using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace Sgl.Domain;

/// <summary>
/// The three series of physical boxes. The series is never stored: it comes from the carpeta's estado and from the
/// suffix of the box label (<c>-CD</c>, <c>-PUC</c> or none).
/// </summary>
public enum SerieCaja
{
    /// <summary>One letter (or two) and a number, e.g. <c>A-12</c>.</summary>
    Normal = 0,

    /// <summary>Cambio de domicilio, label suffix <c>-CD</c>.</summary>
    CambioDomicilio = 1,

    /// <summary>Penúltima carpeta (uploaded with F8), label suffix <c>-PUC</c>.</summary>
    PenultimaCarpeta = 2,
}

/// <summary>Accepted label formats of a box. The number is always typed by a person; this only validates it.</summary>
public sealed class CajaFormatos
{
    public static readonly CajaFormatos Defecto = new(@"^[A-Z]{1,2}-\d{1,4}$", "-CD", "-PUC");

    private static readonly object ActualLock = new();
    private static CajaFormatos _actual = Defecto;
    private static bool _fijado;

    public CajaFormatos(string normal, string sufijoCambioDomicilio, string sufijoPenultima)
    {
        if (string.IsNullOrWhiteSpace(normal)) throw new ArgumentException("El formato de la serie normal es obligatorio.", nameof(normal));
        Normal = normal;
        SufijoCambioDomicilio = SufijoValido(sufijoCambioDomicilio, nameof(sufijoCambioDomicilio));
        SufijoPenultima = SufijoValido(sufijoPenultima, nameof(sufijoPenultima));
        if (SufijoCambioDomicilio == SufijoPenultima) throw new ArgumentException("Los sufijos de las series deben ser distintos.");
        Patron = new Regex(normal, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    }

    /// <summary>Regular expression of the base label of the normal series (without suffix).</summary>
    public string Normal { get; }

    public string SufijoCambioDomicilio { get; }

    public string SufijoPenultima { get; }

    internal Regex Patron { get; }

    /// <summary>Formats used by <see cref="Carpeta.AsignarCaja(string?, string, DateTime)"/>; fixed once at start-up.</summary>
    public static CajaFormatos Actual
    {
        get { lock (ActualLock) return _actual; }
    }

    /// <summary>Fixes the application-wide formats. Calling it again with different ones is a programming error.</summary>
    public static void Fijar(CajaFormatos formatos)
    {
        ArgumentNullException.ThrowIfNull(formatos);
        lock (ActualLock)
        {
            if (_fijado && !_actual.EsIgualA(formatos))
            {
                throw new InvalidOperationException("Los formatos de caja ya fueron fijados.");
            }

            _actual = formatos;
            _fijado = true;
        }
    }

    public string SufijoDe(SerieCaja serie) => serie switch
    {
        SerieCaja.CambioDomicilio => SufijoCambioDomicilio,
        SerieCaja.PenultimaCarpeta => SufijoPenultima,
        _ => "",
    };

    private static string SufijoValido(string sufijo, string nombre)
    {
        var s = sufijo?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(s) || s.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("El sufijo de una serie no puede estar vacío ni tener espacios.", nombre);
        }
        return s;
    }

    private bool EsIgualA(CajaFormatos? otro) =>
        otro is not null && Normal == otro.Normal && SufijoCambioDomicilio == otro.SufijoCambioDomicilio && SufijoPenultima == otro.SufijoPenultima;
}

public sealed partial class Carpeta
{
    private static readonly Regex Separadores = new(@"[\s_\-]+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex LimiteLetraNumero = new(@"(?<=\p{L})(?=\d)|(?<=\d)(?=\p{L})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex LetrasYNumero = new(@"^(\p{L}+)-(\d+)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    /// <summary>The series a carpeta must be boxed in: uploaded with F8 → PUC, cambio de domicilio → CD, everything else → normal.</summary>
    public static SerieCaja SerieDe(EstadoCarpeta estado) =>
        estado == EstadoCarpeta.SubidaConF8 ? SerieCaja.PenultimaCarpeta
        : EsCambioDomicilio(estado) ? SerieCaja.CambioDomicilio
        : SerieCaja.Normal;

    public static string NombreSerie(SerieCaja serie) => serie switch
    {
        SerieCaja.CambioDomicilio => "cambio de domicilio",
        SerieCaja.PenultimaCarpeta => "penúltima carpeta",
        _ => "normal",
    };

    /// <summary>The label in its canonical form (<c>a 12</c> → <c>A-12</c>), or null when it does not match any series format.</summary>
    public static string? NormalizarRotulo(string? texto, CajaFormatos? formatos = null) =>
        TryRotulo(texto, formatos ?? CajaFormatos.Actual, out var canonico, out _) ? canonico : null;

    /// <summary>Series a label belongs to (by its suffix), or null when the label does not match any format.</summary>
    public static SerieCaja? SerieDeRotulo(string? rotulo, CajaFormatos? formatos = null) =>
        TryRotulo(rotulo, formatos ?? CajaFormatos.Actual, out _, out var serie) ? serie : null;

    private static bool TryRotulo(string? texto, CajaFormatos f, out string canonico, out SerieCaja serie)
    {
        canonico = "";
        serie = SerieCaja.Normal;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        var t = Separadores.Replace(texto.Trim().ToUpperInvariant(), "-");
        t = LimiteLetraNumero.Replace(t, "-");
        var cuerpo = t;
        if (t.EndsWith(f.SufijoCambioDomicilio, StringComparison.Ordinal))
        {
            serie = SerieCaja.CambioDomicilio;
            cuerpo = t[..^f.SufijoCambioDomicilio.Length];
        }
        else if (t.EndsWith(f.SufijoPenultima, StringComparison.Ordinal))
        {
            serie = SerieCaja.PenultimaCarpeta;
            cuerpo = t[..^f.SufijoPenultima.Length];
        }

        if (!f.Patron.IsMatch(cuerpo)) return false;
        canonico = t;
        return true;
    }

    /// <summary>
    /// Next free number after the highest one used by <paramref name="letra"/> in <paramref name="serie"/>, as a label. It is only a
    /// hint: nobody is ever assigned a number automatically. Without a letter it continues with the greatest one in use (or «A»).
    /// Null when the next number no longer fits the format (the letter must advance).
    /// </summary>
    public static string? SugerirSiguienteRotulo(IEnumerable<string> rotulosExistentes, SerieCaja serie, string? letra, CajaFormatos? formatos = null)
    {
        var f = formatos ?? CajaFormatos.Actual;
        var usados = new List<(string Letras, long Numero)>();
        foreach (var rotulo in rotulosExistentes)
        {
            if (!TryRotulo(rotulo, f, out var canonico, out var s) || s != serie) continue;
            var cuerpo = canonico[..^f.SufijoDe(serie).Length];
            var m = LetrasYNumero.Match(cuerpo);
            if (m.Success && long.TryParse(m.Groups[2].Value, out var n)) usados.Add((m.Groups[1].Value, n));
        }

        string letras;
        if (string.IsNullOrWhiteSpace(letra))
        {
            letras = usados.Count == 0
                ? "A"
                : usados.Select(u => u.Letras).OrderBy(l => l.Length).ThenBy(l => l, StringComparer.Ordinal).Last();
        }
        else
        {
            letras = letra.Trim().ToUpperInvariant();
            if (!letras.All(char.IsLetter))
            {
                throw new DomainException("La letra de la caja debe contener solo letras.");
            }
        }

        var maximo = usados.Where(u => u.Letras == letras).Select(u => u.Numero).DefaultIfEmpty(0).Max();
        return NormalizarRotulo($"{letras}-{maximo + 1}{f.SufijoDe(serie)}", f);
    }

    /// <summary>
    /// SQL-translatable queue of a series: open-box carpetas (no real box or «Cola de Caja») that are not annulled, already
    /// have a final decision and are not digital (a last-carpeta date from the digital cut-off on: there is no paper to box).
    /// Series by estado, like <see cref="SerieDe"/>.
    /// </summary>
    public static Expression<Func<Carpeta, bool>> ColaCajaExpresion(SerieCaja serie) =>
        ColaCajaExpresion(serie, SectorFisico.CorteDigitalIso);

    /// <summary>
    /// Same queue with an explicit digital cut-off (<c>yyyy-MM-dd</c>). Only ISO dates are compared (as text, which orders like
    /// dates): carpetas without a date, with free text or with an old day-first date are never taken out of the queue.
    /// </summary>
    public static Expression<Func<Carpeta, bool>> ColaCajaExpresion(SerieCaja serie, string corteDigitalIso) => serie switch
    {
        SerieCaja.PenultimaCarpeta => c =>
            !c.Anulada
            && (c.Decision == Decision.Otorgado || c.Decision == Decision.Denegado)
            && (c.CajaArchivo == null || c.CajaArchivo.Trim() == "" || c.CajaArchivo == ColaDeCaja)
            && (c.FechaUltimaCarpeta == null || c.FechaUltimaCarpeta.Length != 10 || c.FechaUltimaCarpeta.Substring(4, 1) != "-"
                || string.Compare(c.FechaUltimaCarpeta, corteDigitalIso) < 0)
            && c.Estado == EstadoCarpeta.SubidaConF8,
        SerieCaja.CambioDomicilio => c =>
            !c.Anulada
            && (c.Decision == Decision.Otorgado || c.Decision == Decision.Denegado)
            && (c.CajaArchivo == null || c.CajaArchivo.Trim() == "" || c.CajaArchivo == ColaDeCaja)
            && (c.FechaUltimaCarpeta == null || c.FechaUltimaCarpeta.Length != 10 || c.FechaUltimaCarpeta.Substring(4, 1) != "-"
                || string.Compare(c.FechaUltimaCarpeta, corteDigitalIso) < 0)
            && (c.Estado == EstadoCarpeta.CambioDomicilio
                || c.Estado == EstadoCarpeta.CambioDomicilioSolicitado
                || c.Estado == EstadoCarpeta.CambioDomSubidoConaset
                || c.Estado == EstadoCarpeta.CambioDomSubidoCorreo),
        _ => c =>
            !c.Anulada
            && (c.Decision == Decision.Otorgado || c.Decision == Decision.Denegado)
            && (c.CajaArchivo == null || c.CajaArchivo.Trim() == "" || c.CajaArchivo == ColaDeCaja)
            && (c.FechaUltimaCarpeta == null || c.FechaUltimaCarpeta.Length != 10 || c.FechaUltimaCarpeta.Substring(4, 1) != "-"
                || string.Compare(c.FechaUltimaCarpeta, corteDigitalIso) < 0)
            && c.Estado != EstadoCarpeta.SubidaConF8
            && c.Estado != EstadoCarpeta.CambioDomicilio
            && c.Estado != EstadoCarpeta.CambioDomicilioSolicitado
            && c.Estado != EstadoCarpeta.CambioDomSubidoConaset
            && c.Estado != EstadoCarpeta.CambioDomSubidoCorreo,
    };

    /// <summary>
    /// Carpetas that are in «Cola de Caja» today without a final decision (legacy data). They are not part of any series queue
    /// and are never modified; they are only counted apart.
    /// </summary>
    public static readonly Expression<Func<Carpeta, bool>> ColaHeredadaSinDecisionExpresion = c =>
        !c.Anulada && c.Decision == Decision.Pendiente && c.CajaArchivo == ColaDeCaja;

    /// <summary>Puts the carpeta back in the queue (reopening a box). Unlike a new assignment it never validates: old boxes can be out of format.</summary>
    public void DevolverACola(string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (CajaArchivo != ColaDeCaja)
        {
            Audit(now, usuario, nameof(CajaArchivo), CajaArchivo, ColaDeCaja);
            CajaArchivo = ColaDeCaja;
        }
    }

    public void AsignarCaja(string? nuevaCaja, string autor, DateTime now) =>
        AsignarCaja(nuevaCaja, autor, now, CajaFormatos.Actual);

    /// <summary>
    /// A new assignment: a real label or «Cola de Caja» needs a final decision; a real label must match a format and the series of
    /// the carpeta. Removing the box (null) is always allowed. The duplicate check needs the other boxes: it lives in the service.
    /// </summary>
    public void AsignarCaja(string? nuevaCaja, string autor, DateTime now, CajaFormatos formatos)
    {
        ArgumentNullException.ThrowIfNull(formatos);
        RequireActiva();
        var usuario = RequireAutor(autor);
        var caja = ResolverCaja(nuevaCaja, formatos);
        if (caja != CajaArchivo)
        {
            Audit(now, usuario, nameof(CajaArchivo), CajaArchivo, caja);
            CajaArchivo = caja;
        }
    }

    private string? ResolverCaja(string? nuevaCaja, CajaFormatos formatos)
    {
        var texto = string.IsNullOrWhiteSpace(nuevaCaja) ? null : nuevaCaja.Trim();
        if (texto is null || texto == CajaArchivo) return texto;

        if (Decision is not (Decision.Otorgado or Decision.Denegado))
        {
            throw new DomainException("Solo una carpeta con decisión final (otorgada o denegada) puede guardarse en una caja.");
        }

        if (texto.Equals(ColaDeCaja, StringComparison.OrdinalIgnoreCase)) return ColaDeCaja;

        if (!TryRotulo(texto, formatos, out var canonico, out var serie))
        {
            throw new DomainException(
                $"El rótulo «{texto}» no tiene un formato válido. Use una letra y un número (por ejemplo A-12); " +
                $"agregue {formatos.SufijoCambioDomicilio} para cambio de domicilio o {formatos.SufijoPenultima} para penúltima carpeta.");
        }

        var esperada = SerieDe(Estado);
        if (serie != esperada)
        {
            throw new DomainException(
                $"Esta carpeta es de la serie {NombreSerie(esperada)} y la caja {canonico} es de la serie {NombreSerie(serie)}. No se mezclan series en una caja.");
        }

        if (canonico.Length > MaxCajaLength)
        {
            throw new DomainException($"El rótulo de caja excede {MaxCajaLength} caracteres.");
        }

        return canonico;
    }
}
