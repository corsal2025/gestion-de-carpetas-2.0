using System.Text;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Excel;

namespace PeticionCambioDomicilio.Comunas;

public sealed record ComunaContact(string Comuna, string Email, string Domain);

public sealed record ComunaImportResult(int Leidos, int Nuevos, int Actualizados, IReadOnlyList<string> Avisos);

/// <summary>
/// Directorio de correos municipales, respaldado por un CSV (Comuna,ContactEmail,Domain).
/// Editable en caliente: agregar un contacto o importar desde la hoja "CORREOS CAMBIO DE DOMICLIO"
/// del libro reescribe el CSV y reconstruye el índice. Singleton — todo acceso va con lock.
/// </summary>
public sealed class ComunaDirectory
{
    private readonly string _csvPath;
    private readonly object _gate = new();
    private List<ComunaContact> _contacts = new();
    private ILookup<string, ComunaContact> _byComuna = Enumerable.Empty<ComunaContact>().ToLookup(_ => "");

    public ComunaDirectory(string csvPath)
    {
        _csvPath = csvPath;
        Reload();
    }

    public int Count
    {
        get { lock (_gate) { return _contacts.Count; } }
    }

    public IReadOnlyList<ComunaContact> All()
    {
        lock (_gate) { return _contacts.ToList(); }
    }

    public IReadOnlyList<string> ComunaNames()
    {
        lock (_gate)
        {
            return _contacts.Select(c => c.Comuna).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.CurrentCulture).ToList();
        }
    }

    /// <summary>Correos registrados para una comuna, sin repetir. Vacío si no está en el directorio.</summary>
    public IReadOnlyList<string> EmailsFor(string? comuna)
    {
        lock (_gate)
        {
            return _byComuna[TextNormalization.Fold(comuna)]
                .Select(c => c.Email)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// Calza un texto sucio del Excel (tipeos: "VIÑA DELA MAR", "QUILPUE") contra el nombre oficial
    /// de una comuna del directorio. Devuelve el nombre canónico o null si no hay match razonable.
    /// </summary>
    public string? ResolveComunaName(string? raw)
    {
        var folded = TextNormalization.Fold(raw);
        if (folded.Length < 3 || folded.Any(char.IsDigit) || folded is "rut invalido" or "rut ivalido"
            or "rur invalido" or "rur invalido" or "pendiente f8" or "1 licencia")
        {
            return null;
        }

        lock (_gate)
        {
            var exact = _byComuna[folded].FirstOrDefault();
            if (exact is not null)
            {
                return exact.Comuna;
            }

            string? best = null;
            var bestDistance = int.MaxValue;
            foreach (var name in _byComuna.Select(g => g.Key).Distinct())
            {
                var d = Levenshtein(folded, name, 2);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = _byComuna[name].First().Comuna;
                }
            }

            return bestDistance <= 2 ? best : null;
        }
    }

    /// <summary>Agrega (o reemplaza el correo de) una comuna y persiste el CSV. El dominio se
    /// deriva del correo si no se pasa.</summary>
    public (bool Ok, string Mensaje) AddOrUpdate(string comuna, string email, string? domain = null)
    {
        comuna = comuna.Trim().ToUpperInvariant();
        email = email.Trim();
        if (comuna.Length < 2)
        {
            return (false, "Nombre de comuna inválido.");
        }

        if (!email.Contains('@') || email.Length < 5)
        {
            return (false, "Correo inválido.");
        }

        domain = string.IsNullOrWhiteSpace(domain) ? email[(email.IndexOf('@') + 1)..].Trim() : domain.Trim();

        lock (_gate)
        {
            var exists = _contacts.Any(c =>
                string.Equals(c.Comuna, comuna, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                return (false, $"{comuna} ya tiene registrado {email}.");
            }

            _contacts.Add(new ComunaContact(comuna, email, domain));
            Persist();
            RebuildIndex();
            return (true, $"Agregado: {comuna} → {email}");
        }
    }

    /// <summary>Cambia el correo (y su dominio) de un contacto existente, identificado por
    /// comuna + correo viejo. Persiste el CSV.</summary>
    public (bool Ok, string Mensaje) EditarCorreo(string comuna, string correoViejo, string correoNuevo)
    {
        correoNuevo = correoNuevo.Trim();
        if (!correoNuevo.Contains('@') || correoNuevo.Length < 5)
        {
            return (false, "Correo inválido.");
        }

        lock (_gate)
        {
            var idx = _contacts.FindIndex(c =>
                string.Equals(c.Comuna, comuna, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Email, correoViejo, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                return (false, "No se encontró ese contacto.");
            }

            var dominio = correoNuevo[(correoNuevo.IndexOf('@') + 1)..].Trim();
            _contacts[idx] = _contacts[idx] with { Email = correoNuevo, Domain = dominio };
            Persist();
            RebuildIndex();
            return (true, $"{_contacts[idx].Comuna}: correo actualizado a {correoNuevo}");
        }
    }

    /// <summary>Elimina un contacto (comuna + correo). Persiste el CSV.</summary>
    public (bool Ok, string Mensaje) Eliminar(string comuna, string correo)
    {
        lock (_gate)
        {
            var quitados = _contacts.RemoveAll(c =>
                string.Equals(c.Comuna, comuna, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Email, correo, StringComparison.OrdinalIgnoreCase));
            if (quitados == 0)
            {
                return (false, "No se encontró ese contacto.");
            }

            Persist();
            RebuildIndex();
            return (true, $"{comuna}: eliminado {correo}");
        }
    }

    /// <summary>
    /// Importa el directorio desde la hoja del libro cuyo nombre contiene "correos cambio de dom"
    /// (columnas Municipio / Correo; el municipio viene con prefijo "MUNICIP/"). Upsert por
    /// (comuna, correo). No borra los que ya estaban.
    /// </summary>
    public ComunaImportResult ImportFromWorkbook(string workbookPath)
    {
        var avisos = new List<string>();
        using var loaded = LoadedWorkbook.Open(workbookPath);

        var sheet = loaded.Workbook.Worksheets.FirstOrDefault(w =>
            TextNormalization.Fold(w.Name).Contains("correos cambio de dom"));
        if (sheet is null)
        {
            return new ComunaImportResult(0, 0, 0, new[] { "No se encontró la hoja de correos de comunas en el libro." });
        }

        var used = sheet.RangeUsed();
        if (used is null)
        {
            return new ComunaImportResult(0, 0, 0, new[] { "La hoja de correos está vacía." });
        }

        int leidos = 0, nuevos = 0, actualizados = 0;

        lock (_gate)
        {
            foreach (var row in used.Rows())
            {
                var muni = row.Cell(1).GetString().Trim();
                var mail = row.Cell(2).GetString().Trim();
                if (muni.Length == 0 || mail.Length == 0)
                {
                    continue;
                }

                var folded = TextNormalization.Fold(muni);
                if (folded is "municipio" or "" || !mail.Contains('@'))
                {
                    continue;
                }

                var comuna = StripMuniPrefix(muni);
                var domain = mail[(mail.IndexOf('@') + 1)..].Trim();
                leidos++;

                var already = _contacts.Any(c =>
                    string.Equals(c.Comuna, comuna, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Email, mail, StringComparison.OrdinalIgnoreCase));
                if (already)
                {
                    actualizados++;
                    continue;
                }

                _contacts.Add(new ComunaContact(comuna, mail, domain));
                nuevos++;
            }

            if (nuevos > 0)
            {
                Persist();
                RebuildIndex();
            }
        }

        return new ComunaImportResult(leidos, nuevos, actualizados, avisos);
    }

    private static string StripMuniPrefix(string raw)
    {
        var value = raw.Trim();
        var slash = value.IndexOf('/');
        if (slash >= 0 && slash < 12)
        {
            value = value[(slash + 1)..];
        }

        return value.Trim().ToUpperInvariant();
    }

    private void Reload()
    {
        lock (_gate)
        {
            _contacts = File.Exists(_csvPath) ? Parse(_csvPath) : new List<ComunaContact>();
            RebuildIndex();
        }
    }

    private void RebuildIndex() => _byComuna = _contacts.ToLookup(c => TextNormalization.Fold(c.Comuna));

    private void Persist()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Comuna,ContactEmail,Domain");
        foreach (var c in _contacts.OrderBy(c => c.Comuna, StringComparer.CurrentCulture).ThenBy(c => c.Email))
        {
            sb.AppendLine($"\"{Escape(c.Comuna)}\",\"{Escape(c.Email)}\",\"{Escape(c.Domain)}\"");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_csvPath)!);
        File.WriteAllText(_csvPath, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Escape(string v) => v.Replace("\"", "\"\"");

    private static List<ComunaContact> Parse(string csvPath)
    {
        var result = new List<ComunaContact>();
        var lines = File.ReadAllLines(csvPath);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i == 0 && line.Contains("Comuna", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = SplitCsv(line);
            if (fields.Count < 2 || !fields[1].Contains('@'))
            {
                continue;
            }

            var comuna = fields[0].Trim();
            var email = fields[1].Trim();
            var domain = fields.Count >= 3 && fields[2].Trim().Length > 0
                ? fields[2].Trim()
                : email[(email.IndexOf('@') + 1)..];
            result.Add(new ComunaContact(comuna, email, domain));
        }

        return result;
    }

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var c in line)
        {
            switch (c)
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ',' when !inQuotes:
                    fields.Add(sb.ToString());
                    sb.Clear();
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        fields.Add(sb.ToString());
        return fields;
    }

    private static int Levenshtein(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max)
        {
            return max + 1;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, curr[j]);
            }

            if (rowMin > max)
            {
                return max + 1;
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
