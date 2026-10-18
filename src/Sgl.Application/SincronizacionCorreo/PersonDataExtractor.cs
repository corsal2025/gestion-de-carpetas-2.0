using System.Text.RegularExpressions;
using System.Linq;

using PeticionCambioDomicilio.Domain;

namespace Sgl.Application.SincronizacionCorreo;

public sealed record ExtractedPersonData(string? FullName, string? Rut);

/// <summary>
/// Ported from CambioDeDomicilio/Extraction/PersonDataExtractor.cs (RUT validation reuses the domain RutValidator).
/// Calibrated against real comuna emails (38 production messages, 2026-07-03):
/// the RUT arrives prefixed (RUT/RUN/R.U.T.), bare with dots, or bare without dots;
/// the contributor's name has no reliable marker of its own, but reliably sits
/// adjacent to the RUT. Matching "first capitalized words anywhere" is NOT viable —
/// it grabs the Exchange "CORREO EXTERNO" banner or forwarded-header sender names.
/// </summary>
public static partial class PersonDataExtractor
{
    // Exchange prepends this warning to every external email; strip it before extracting.
    [GeneratedRegex(@"CORREO EXTERNO\s*:?.*?(seguro\.|adjuntos?\.)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ExternalBannerPattern();

    // Priority 1: prefixed — RUT: / R.U.T. / RUN / R.U.N., dots and spacing optional.
    [GeneratedRegex(@"R\.?\s?U\.?\s?[TN]\.?\s*:?\s*(\d{1,2}(?:\.?\d{3}){2}\s?-\s?[\dkK])", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixedRutPattern();

    // Priority 2: bare dotted (12.345.678-9) — unambiguous even without a prefix.
    [GeneratedRegex(@"\b(\d{1,2}\.\d{3}\.\d{3}\s?-\s?[\dkK])\b")]
    private static partial Regex BareDottedRutPattern();

    // Priority 3: bare undotted (12345678-9).
    [GeneratedRegex(@"\b(\d{7,8}\s?-\s?[\dkK])\b")]
    private static partial Regex BareUndottedRutPattern();

    // Priority 4: check digit separated only by whitespace (Viña del Mar's system pads with
    // runs of spaces/tabs: "19328001        3"). Safe because the check-digit validation
    // rejects random digit pairs.
    [GeneratedRegex(@"\b(\d{7,8}[ \t\r\n]{1,10}[\dkK])\b")]
    private static partial Regex SpaceSeparatedRutPattern();

    // 2-5 capitalized words (all-caps or title case), used only in the window adjacent to the RUT.
    // Words may be joined by a hyphen (e.g. "MARIA-ANTONIA") — a plain \s+ separator would drop
    // the first half of a hyphenated compound given name.
    [GeneratedRegex(@"([A-ZÁÉÍÓÚÑ][A-ZÁÉÍÓÚÑa-záéíóúñ]+(?:[\s-]+[A-ZÁÉÍÓÚÑ][A-ZÁÉÍÓÚÑa-záéíóúñ]+){1,4})")]
    private static partial Regex NameSequencePattern();

    // Honorifics, connectors and filler words stripped from the edges of a name candidate
    // ("Quien posee una licencia..." follows the name in Viña del Mar's template).
    private static readonly string[] EdgeStopWords =
        ["DON", "DOÑA", "SR", "SRA", "SEÑOR", "SEÑORA", "DE", "DEL", "RUT", "RUN", "CI", "CÉDULA", "QUIEN"];

    private const int NameWindowChars = 90;

    // Reply/forward prefixes Outlook prepends when the request itself sits in the subject line
    // instead of the body (e.g. "RV: SOLICITUD ANTECEDENTES <NOMBRE> RUN <RUT>").
    [GeneratedRegex(@"^(?:\s*(?:RV|RE|FW|FWD|RES|ENV)\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex ReplyForwardPrefixPattern();

    // Boilerplate request words that sit directly before the name with no lowercase separator —
    // left in place they'd get swallowed into the capped-length NameSequencePattern match.
    // Calibrated from one real sample (2026-07-03); add more terms here as new subject-only
    // cases surface.
    [GeneratedRegex(@"\b(SOLICITUD|ANTECEDENTES|PETICI[OÓ]N|CARPETA)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectBoilerplatePattern();

    /// <summary>
    /// Finds a RUT in the email subject, for cases where the request sits there instead of the
    /// body (e.g. a forwarded email). Only the RUT is trusted (it's check-digit validated) — a
    /// name is NEVER returned even if one is found nearby: unlike the body (calibrated against 38
    /// real samples with a consistent template), subject phrasing varies wildly per comuna and
    /// generic instructions ("SUBIR CARPETA PLATAFORMA CONASET") are indistinguishable from a real
    /// name using this heuristic — one was captured as a contributor's name in production. Callers
    /// always get NeedsReview=true for a subject-derived contributor so a person confirms the name.
    /// </summary>
    public static ExtractedPersonData ExtractFromSubject(string subject)
    {
        var text = ReplyForwardPrefixPattern().Replace(subject, "");
        text = SubjectBoilerplatePattern().Replace(text, " ");
        var extracted = Extract(text);
        return new ExtractedPersonData(FullName: null, extracted.Rut);
    }

    public static ExtractedPersonData Extract(string bodyText)
    {
        var text = ExternalBannerPattern().Replace(bodyText, " ");

        var rutMatch = FindRut(text);
        if (rutMatch is null)
        {
            // A name without an anchoring RUT is not trustworthy (banners, forwarded
            // sender names, salutations all match a generic capitalized-words pattern).
            return new ExtractedPersonData(null, null);
        }

        var (rut, matchIndex, matchLength, isSpaceSeparatedFormat) = rutMatch.Value;
        var (fullName, _) = FindNameNear(text, matchIndex, matchLength);
        if (fullName is not null && isSpaceSeparatedFormat)
        {
            fullName = ReorderGivenNamesFirst(fullName);
        }

        return new ExtractedPersonData(fullName, rut);
    }

    /// <summary>
    /// Extracts every contributor listed in the same email — some comunas request folders for
    /// several people in one message, each as its own "NOMBRE RUT" line. One entry per valid RUT
    /// found, in the order they appear; each entry's name search is bounded by its neighboring
    /// RUTs so one person's name never bleeds into another's.
    /// </summary>
    public static IReadOnlyList<ExtractedPersonData> ExtractAll(string bodyText)
    {
        var text = ExternalBannerPattern().Replace(bodyText, " ");
        var rutMatches = FindAllRuts(text);

        var results = new List<ExtractedPersonData>(rutMatches.Count);
        var cursor = 0; // end of text already claimed by a previous contributor's name
        for (var i = 0; i < rutMatches.Count; i++)
        {
            var (rut, index, length, isSpaceSeparatedFormat) = rutMatches[i];
            var upperBound = i < rutMatches.Count - 1 ? rutMatches[i + 1].Index : text.Length;
            var (fullName, consumedEnd) = FindNameNear(text, index, length, cursor, upperBound);
            if (fullName is not null && isSpaceSeparatedFormat)
            {
                fullName = ReorderGivenNamesFirst(fullName);
            }

            results.Add(new ExtractedPersonData(fullName, rut));
            cursor = Math.Max(index + length, consumedEnd);
        }

        return results;
    }

    private static List<(string Rut, int Index, int Length, bool IsSpaceSeparatedFormat)> FindAllRuts(string text)
    {
        var found = new List<(string Rut, int Index, int Length, bool IsSpaceSeparatedFormat)>();
        foreach (var (pattern, isSpaceSeparated) in RutPatternsInPriorityOrder())
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (found.Any(existing => match.Index < existing.Index + existing.Length && match.Index + match.Length > existing.Index))
                {
                    continue; // already matched by a higher-priority pattern
                }

                var normalized = RutValidator.NormalizeAndValidate(match.Groups[1].Value);
                if (normalized is not null)
                {
                    found.Add((normalized, match.Index, match.Length, isSpaceSeparated));
                }
            }
        }

        return found.OrderBy(m => m.Index).ToList();
    }

    private static (string Rut, int Index, int Length, bool IsSpaceSeparatedFormat)? FindRut(string text)
    {
        foreach (var (pattern, isSpaceSeparated) in RutPatternsInPriorityOrder())
        {
            foreach (Match match in pattern.Matches(text))
            {
                var normalized = RutValidator.NormalizeAndValidate(match.Groups[1].Value);
                if (normalized is not null)
                {
                    return (normalized, match.Index, match.Length, isSpaceSeparated);
                }
            }
        }

        return null;
    }

    private static (Regex Pattern, bool IsSpaceSeparated)[] RutPatternsInPriorityOrder() =>
    [
        (PrefixedRutPattern(), false),
        (BareDottedRutPattern(), false),
        (BareUndottedRutPattern(), false),
        (SpaceSeparatedRutPattern(), true)
    ];

    /// <summary>
    /// Viña del Mar's system (the only source matched via <see cref="SpaceSeparatedRutPattern"/>)
    /// exports APELLIDO(S) NOMBRE(S) — surnames first, then given names — so this swaps them to
    /// read given-names-first like everywhere else in the report. The 2/3/4-word shapes are
    /// unambiguous (one or two surnames, followed by one or two given names) and safe to swap;
    /// any other word count can't be split reliably, so the caller drops the name entirely
    /// (returns null) rather than risk silently storing it in the wrong order.
    /// </summary>
    private static string? ReorderGivenNamesFirst(string fullName)
    {
        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            2 => $"{words[1]} {words[0]}",
            3 => $"{words[2]} {words[0]} {words[1]}",
            4 => $"{words[2]} {words[3]} {words[0]} {words[1]}",
            _ => null
        };
    }

    /// <summary>
    /// <paramref name="lowerBound"/>/<paramref name="upperBound"/> keep the search from crossing
    /// into a neighboring contributor's name/RUT when an email lists more than one (see
    /// <see cref="ExtractAll"/>); single-contributor callers use the whole text. Returns the
    /// absolute text index where the match ends, so a caller processing a chain of contributors
    /// ("RUT1 NOMBRE1 RUT2 NOMBRE2 ...") can exclude NOMBRE1 from RUT2's search — otherwise RUT2's
    /// "before" window would re-claim the previous person's name-after-their-RUT as its own.
    /// </summary>
    private static (string? Name, int ConsumedEnd) FindNameNear(string text, int rutIndex, int rutLength, int lowerBound = 0, int? upperBound = null)
    {
        var effectiveUpperBound = upperBound ?? text.Length;
        var rutEnd = rutIndex + rutLength;

        var windowStart = Math.Max(lowerBound, rutIndex - NameWindowChars);
        var before = text[windowStart..rutIndex];

        // The name usually ends right where the RUT begins → take the LAST sequence before it.
        var beforeMatches = NameSequencePattern().Matches(before);
        if (beforeMatches.Count > 0)
        {
            var candidate = CleanName(beforeMatches[^1].Groups[1].Value);
            if (candidate is not null)
            {
                return (candidate, rutEnd); // name sits before the RUT — nothing extra consumed after it
            }
        }

        var afterEnd = Math.Min(effectiveUpperBound, rutEnd + NameWindowChars);
        if (afterEnd <= rutEnd)
        {
            return (null, rutEnd);
        }

        var after = text[rutEnd..afterEnd];
        var afterMatch = NameSequencePattern().Match(after);
        if (!afterMatch.Success)
        {
            return (null, rutEnd);
        }

        var name = CleanName(afterMatch.Groups[1].Value);
        return name is null ? (null, rutEnd) : (name, rutEnd + afterMatch.Index + afterMatch.Length);
    }

    private static string? CleanName(string raw)
    {
        // Split on ANY whitespace: real bodies pad names with tab/space runs and line breaks.
        var words = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

        while (words.Count > 0 && EdgeStopWords.Contains(Normalize(words[0])))
        {
            words.RemoveAt(0);
        }
        while (words.Count > 0 && EdgeStopWords.Contains(Normalize(words[^1])))
        {
            words.RemoveAt(words.Count - 1);
        }

        // A real full name has at least two words (first + last name).
        return words.Count >= 2 ? string.Join(' ', words) : null;
    }

    private static string Normalize(string word) => word.TrimEnd('.', ',', ':').ToUpperInvariant();
}
