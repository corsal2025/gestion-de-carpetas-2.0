using ClosedXML.Excel;

namespace PeticionCambioDomicilio.Excel;

/// <summary>
/// Abre un .xlsx SIEMPRE desde una copia temporal fresca:
///  1. El libro real vive en Google Drive y suele estar abierto en Excel — abrirlo directo puede
///     dar un bloqueo o, peor, una version en cache. La copia se hace con acceso compartido de
///     lectura y se lee esa, asi cada importacion ve el estado actual del archivo en disco.
///  2. El libro trae listas desplegables > 255 caracteres que ClosedXML rechaza; si eso pasa, a
///     la copia se le quitan los nodos dataValidation (WorkbookSanitizer) y se reintenta.
/// Devolvé el resultado dentro de un <c>using</c>.
/// </summary>
public sealed class LoadedWorkbook : IDisposable
{
    public XLWorkbook Workbook { get; }
    private readonly string _tempPath;

    private LoadedWorkbook(XLWorkbook workbook, string tempPath)
    {
        Workbook = workbook;
        _tempPath = tempPath;
    }

    public static LoadedWorkbook Open(string path)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"peticion-lectura-{Guid.NewGuid():N}.xlsx");

        using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var dest = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(dest);
        }

        try
        {
            return new LoadedWorkbook(new XLWorkbook(temp), temp);
        }
        catch (ArgumentOutOfRangeException)
        {
            WorkbookSanitizer.StripDataValidationsInPlace(temp);
            return new LoadedWorkbook(new XLWorkbook(temp), temp);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* archivo temporal */ }
            throw;
        }
    }

    public void Dispose()
    {
        Workbook.Dispose();
        try { File.Delete(_tempPath); } catch { /* archivo temporal */ }
    }
}
