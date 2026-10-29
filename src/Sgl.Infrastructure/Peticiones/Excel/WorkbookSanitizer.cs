using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace PeticionCambioDomicilio.Excel;

/// <summary>
/// El libro real "DETALLE CARPETAS" trae listas desplegables (data validation) cuya fórmula supera
/// los 255 caracteres que acepta ClosedXML, y por eso se niega a abrir el archivo. La importación
/// no necesita las validaciones, así que esto genera una copia temporal sin ellas.
/// Portado tal cual de LicenciasCarpetas/Import/WorkbookSanitizer.cs.
/// </summary>
public static class WorkbookSanitizer
{
    /// <summary>Copia el libro a un archivo temporal y le quita todos los nodos de data-validation.
    /// El que llama es dueño del archivo devuelto y debe borrarlo.</summary>
    public static string CreateCopyWithoutDataValidations(string workbookPath)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"peticion-import-{Guid.NewGuid():N}.xlsx");

        // Stream compartido de lectura: el libro suele estar en una carpeta sincronizada y puede
        // estar abierto en Excel al mismo tiempo.
        using (var source = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(destination);
        }

        try
        {
            StripDataValidationsInPlace(tempPath);
            return tempPath;
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    /// <summary>Tope de lo que puede ocupar un .xlsx ya descomprimido (un libro real pesa unos pocos MB).</summary>
    public const long MaxBytesDescomprimidos = 200L * 1024 * 1024;

    /// <summary>
    /// Comprueba, antes de abrir el libro con ClosedXML, que sea un .xlsx (zip con firma "PK") y que no
    /// descomprima a más de <paramref name="maxBytesDescomprimidos"/> (zip bomb). Lanza <see cref="InvalidDataException"/>.
    /// </summary>
    public static void ValidarArchivo(string path, long maxBytesDescomprimidos = MaxBytesDescomprimidos)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        ValidarStream(stream, maxBytesDescomprimidos);
    }

    /// <summary>Igual que <see cref="ValidarArchivo"/> sobre un stream con búsqueda; lo deja en la posición 0.</summary>
    public static void ValidarStream(Stream stream, long maxBytesDescomprimidos = MaxBytesDescomprimidos)
    {
        stream.Position = 0;
        Span<byte> firma = stackalloc byte[2];
        if (stream.Read(firma) < 2 || firma[0] != (byte)'P' || firma[1] != (byte)'K')
        {
            throw new InvalidDataException("El archivo no es un Excel válido (.xlsx).");
        }

        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        long total = 0;
        foreach (var entrada in zip.Entries)
        {
            total += entrada.Length;
            if (total > maxBytesDescomprimidos)
            {
                throw new InvalidDataException("El archivo Excel ocupa demasiado al descomprimirse; no se abrirá.");
            }
        }

        stream.Position = 0;
    }

    /// <summary>Quita los nodos dataValidation de un .xlsx ya existente (in situ).</summary>
    public static void StripDataValidationsInPlace(string xlsxPath)
    {
        using var archive = ZipFile.Open(xlsxPath, ZipArchiveMode.Update);

        foreach (var entry in archive.Entries.ToList())
        {
            if (!entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            XDocument document;
            using (var reader = entry.Open())
            using (var xml = XmlReader.Create(reader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                document = XDocument.Load(xml);
            }

            var validations = document.Descendants()
                .Where(element => element.Name.LocalName is "dataValidations" or "dataValidation")
                .ToList();

            if (validations.Count == 0)
            {
                continue;
            }

            foreach (var validation in validations)
            {
                validation.Remove();
            }

            using var writer = entry.Open();
            writer.SetLength(0);
            document.Save(writer);
        }
    }
}
