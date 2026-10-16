using System.IO.Compression;
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
            {
                document = XDocument.Load(reader);
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
