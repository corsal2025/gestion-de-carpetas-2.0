using Sgl.Application.CambioDomicilio;
using Sgl.Domain;

namespace Sgl.Infrastructure.CambioDomicilio;

/// <summary>
/// Disk store of the pasted screenshots. The folder (<c>CambioDomicilio:CarpetaPantallazos</c>, relative to the content root) must be
/// outside wwwroot so nothing is served by the static files. The name is always the mail's id plus .png or .jpg (no personal data, nothing
/// the browser sent), so a write can only touch the file of that very mail. The image is validated in memory first (signature and size),
/// then written to a temporary file and moved into place: a rejected or interrupted upload leaves nothing behind.
/// </summary>
public sealed class AlmacenPantallazos : IAlmacenPantallazos
{
    private static readonly byte[] FirmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] FirmaJpeg = [0xFF, 0xD8, 0xFF];

    private readonly string _carpeta;

    public AlmacenPantallazos(CambioDomicilioOptions opciones, string raiz)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentException.ThrowIfNullOrWhiteSpace(raiz);
        MaxBytes = Math.Max(1, opciones.PantallazoMaxKB) * 1024L;
        _carpeta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(opciones.CarpetaPantallazos, raiz));
        var estatica = Path.TrimEndingDirectorySeparator(Path.GetFullPath("wwwroot", raiz));
        if (_carpeta.Equals(estatica, StringComparison.OrdinalIgnoreCase)
            || _carpeta.StartsWith(estatica + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "CambioDomicilio:CarpetaPantallazos no puede estar dentro de wwwroot: los pantallazos tienen datos personales y no se sirven como archivos públicos.");
        }
    }

    public long MaxBytes { get; }

    public async Task<string> GuardarAsync(Guid envioId, Stream contenido, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var bytes = await LeerAcotadoAsync(contenido, ct);
        var extension = ExtensionDe(bytes) ?? throw new DomainException("El archivo no es una imagen PNG o JPEG.");
        var nombre = $"{envioId}{extension}";
        var destino = Path.Combine(_carpeta, nombre);
        var temporal = Path.Combine(_carpeta, $"{envioId}.{Guid.NewGuid():N}.tmp");

        Directory.CreateDirectory(_carpeta);
        try
        {
            await File.WriteAllBytesAsync(temporal, bytes, ct);
            File.Move(temporal, destino, overwrite: true);
        }
        catch
        {
            TryDelete(temporal);
            throw;
        }

        return nombre;
    }

    public Task<Stream?> AbrirAsync(Guid envioId, string archivo, CancellationToken ct = default)
    {
        if (!EsNombreDelEnvio(envioId, archivo))
        {
            return Task.FromResult<Stream?>(null);
        }

        var ruta = Path.Combine(_carpeta, archivo);
        if (!File.Exists(ruta))
        {
            return Task.FromResult<Stream?>(null);
        }

        // FileShare.Delete: a new screenshot of the same mail can replace the file while someone is downloading it.
        Stream flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        return Task.FromResult<Stream?>(flujo);
    }

    public void Eliminar(Guid envioId, string archivo)
    {
        if (EsNombreDelEnvio(envioId, archivo))
        {
            TryDelete(Path.Combine(_carpeta, archivo));
        }
    }

    /// <summary>Reads at most <see cref="MaxBytes"/> bytes; one more means the image is too big and the rest is not read.</summary>
    private async Task<byte[]> LeerAcotadoAsync(Stream contenido, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var pedir = (int)Math.Min(buffer.Length, MaxBytes - memoria.Length + 1);
            var leidos = await contenido.ReadAsync(buffer.AsMemory(0, pedir), ct);
            if (leidos == 0)
            {
                return memoria.ToArray();
            }

            memoria.Write(buffer, 0, leidos);
            if (memoria.Length > MaxBytes)
            {
                throw new DomainException($"La imagen pesa más de {MaxBytes / 1024} KB: reduzca la captura y vuelva a pegarla.");
            }
        }
    }

    private static string? ExtensionDe(byte[] bytes) =>
        bytes.AsSpan().StartsWith(FirmaPng) ? ".png"
        : bytes.AsSpan().StartsWith(FirmaJpeg) ? ".jpg"
        : null;

    private static bool EsNombreDelEnvio(Guid envioId, string? archivo) =>
        archivo is not null && (archivo == $"{envioId}.png" || archivo == $"{envioId}.jpg");

    private static void TryDelete(string ruta)
    {
        try
        {
            File.Delete(ruta);
        }
        catch (IOException)
        {
            // Another process holds it: a later replacement or cleanup takes care of it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
