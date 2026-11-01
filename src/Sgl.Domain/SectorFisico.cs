using System.Globalization;

namespace Sgl.Domain;

/// <summary>
/// Physical location of a carpeta derived from its "última carpeta" date: before July 2023 it lives in ARCHIVO, from then on in
/// OFICINA 43, and from the digital cut-off (2026-08-01) there is no paper at all: the carpeta is DIGITAL.
/// </summary>
public static class SectorFisico
{
    public const string Archivo = "ARCHIVO";
    public const string Oficina43 = "OFICINA 43";
    public const string Digital = "DIGITAL";
    public const string Desconocido = "—";

    /// <summary>Cut-off agreed with the department: before it the carpeta is in Archivo, from it on in Oficina 43.</summary>
    public static readonly DateOnly CorteDefecto = new(2023, 7, 1);

    /// <summary>Cut-off agreed with the department: from it on the last carpeta was made digitally and has no paper to look for.</summary>
    public static readonly DateOnly CorteDigitalDefecto = new(2026, 8, 1);

    private static readonly object CorteLock = new();
    private static DateOnly _corte = CorteDefecto;
    private static bool _corteFijado;
    private static DateOnly _corteDigital = CorteDigitalDefecto;
    private static bool _corteDigitalFijado;

    /// <summary>
    /// Cut-off used by <see cref="Calcular(string?)"/>. It is fixed once at start-up (<c>Clasificacion:CorteSector</c>);
    /// code that needs another value passes it to <see cref="Calcular(string?, DateOnly)"/> instead of changing this one.
    /// </summary>
    public static DateOnly Corte
    {
        get { lock (CorteLock) return _corte; }
    }

    /// <summary>Fixes the application-wide cut-off. Calling it again with a different value is a programming error.</summary>
    public static void FijarCorte(DateOnly corte)
    {
        lock (CorteLock)
        {
            if (_corteFijado && _corte != corte)
            {
                throw new InvalidOperationException($"El corte de sector ya fue fijado en {_corte:yyyy-MM-dd}.");
            }

            _corte = corte;
            _corteFijado = true;
        }
    }

    /// <summary>Digital cut-off used by <see cref="Calcular(string?)"/>; fixed once at start-up (<c>Clasificacion:CorteDigital</c>).</summary>
    public static DateOnly CorteDigital
    {
        get { lock (CorteLock) return _corteDigital; }
    }

    /// <summary>The digital cut-off as <c>yyyy-MM-dd</c>: last-carpeta dates are stored as ISO text, so queues compare them as text.</summary>
    public static string CorteDigitalIso => CorteDigital.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Fixes the application-wide digital cut-off. Calling it again with a different value is a programming error.</summary>
    public static void FijarCorteDigital(DateOnly corte)
    {
        lock (CorteLock)
        {
            if (_corteDigitalFijado && _corteDigital != corte)
            {
                throw new InvalidOperationException($"El corte digital ya fue fijado en {_corteDigital:yyyy-MM-dd}.");
            }

            _corteDigital = corte;
            _corteDigitalFijado = true;
        }
    }

    private static readonly Dictionary<string, int> Meses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enero"] = 1, ["febrero"] = 2, ["marzo"] = 3, ["abril"] = 4,
        ["mayo"] = 5, ["junio"] = 6, ["julio"] = 7, ["agosto"] = 8,
        ["septiembre"] = 9, ["setiembre"] = 9, ["octubre"] = 10,
        ["noviembre"] = 11, ["diciembre"] = 12,
    };

    public static string Calcular(string? fechaUltimaCarpeta) => Calcular(fechaUltimaCarpeta, Corte);

    public static string Calcular(string? fechaUltimaCarpeta, DateOnly corte) => Calcular(fechaUltimaCarpeta, corte, CorteDigital);

    public static string Calcular(string? fechaUltimaCarpeta, DateOnly corte, DateOnly corteDigital) =>
        !TryParseFecha(fechaUltimaCarpeta, out var d) ? Desconocido
        : d >= corteDigital ? Digital
        : d < corte ? Archivo
        : Oficina43;

    /// <summary>
    /// Reads a date typed by an official or inherited from Excel. Day-first (Chile) always wins over month-first, so
    /// "02-07-2023" is 2 July (Oficina 43), never 7 February: ISO first, then dd-MM-yyyy / dd/MM/yyyy, then Spanish
    /// words. There is no month-first fallback, and a future date is never valid for a folder that already exists.
    /// </summary>
    public static bool TryParseFecha(string? input, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var texto = input.Trim();
        var reconocida = DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateOnly.TryParse(texto, CultureInfo.GetCultureInfo("es-CL"), DateTimeStyles.None, out date)
            || TryParseEspanol(texto, out date);
        if (reconocida && date > HoraChile.Fecha(DateTime.UtcNow))
        {
            date = default;
            return false;
        }

        return reconocida;
    }

    /// <summary>The recognized date as <c>yyyy-MM-dd</c>, or null when the text is not a date.</summary>
    public static string? NormalizarIso(string? input) =>
        TryParseFecha(input, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static bool TryParseEspanol(string input, out DateOnly date)
    {
        date = default;
        var words = input
            .Split([' ', '\t', ',', '/', '-', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.Equals("de", StringComparison.OrdinalIgnoreCase) && !w.Equals("del", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (words.Length != 3 || !int.TryParse(words[0], out var day)) return false;
        if (!Meses.TryGetValue(words[1], out var month) && (!int.TryParse(words[1], out month) || month < 1 || month > 12))
            return false;
        if (!int.TryParse(words[2], out var year)) return false;
        if (words[2].Length <= 2) year += year >= 50 ? 1900 : 2000;
        if (year < 1900 || year > 2100 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;

        date = new DateOnly(year, month, day);
        return true;
    }
}
