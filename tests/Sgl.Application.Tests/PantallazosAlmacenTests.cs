using Sgl.Application.CambioDomicilio;
using Sgl.Domain;
using Sgl.Infrastructure.CambioDomicilio;

namespace Sgl.Application.Tests;

/// <summary>E11: the disk store of the pasted screenshots (signature, size, name, location, safe writing).</summary>
public sealed class PantallazosAlmacenTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), $"sgl-pantallazos-{Guid.NewGuid():N}");

    public PantallazosAlmacenTests() => Directory.CreateDirectory(_raiz);

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch { /* archivo aún tomado por el proceso */ }
    }

    private string Carpeta => Path.Combine(_raiz, "pantallazos");

    private AlmacenPantallazos Almacen(int maxKb = 5120, string? carpeta = null) =>
        new(new CambioDomicilioOptions { CarpetaPantallazos = carpeta ?? Carpeta, PantallazoMaxKB = maxKb }, _raiz);

    private static byte[] Png(int relleno = 32) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[relleno]];

    private static byte[] Jpeg(int relleno = 32) => [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[relleno]];

    private bool HayArchivos() => Directory.Exists(Carpeta) && Directory.EnumerateFileSystemEntries(Carpeta).Any();

    [Fact]
    public async Task Guarda_un_PNG_como_envioId_png_y_un_JPEG_como_envioId_jpg_con_su_contenido()
    {
        var png = Guid.NewGuid();
        var jpg = Guid.NewGuid();
        var almacen = Almacen();

        Assert.Equal($"{png}.png", await almacen.GuardarAsync(png, new MemoryStream(Png())));
        Assert.Equal($"{jpg}.jpg", await almacen.GuardarAsync(jpg, new MemoryStream(Jpeg())));

        Assert.Equal(Png(), await File.ReadAllBytesAsync(Path.Combine(Carpeta, $"{png}.png")));
        Assert.Equal(Jpeg(), await File.ReadAllBytesAsync(Path.Combine(Carpeta, $"{jpg}.jpg")));
        Assert.Equal(2, Directory.GetFileSystemEntries(Carpeta).Length);
    }

    // Criterio 3: un .gif o .exe renombrado a .png se rechaza; no queda nada en disco.
    [Theory]
    [InlineData("GIF89a\u0001\u0000\u0001\u0000")]
    [InlineData("MZ\u0090\u0000\u0003\u0000\u0000\u0000")]
    [InlineData("<html><script>alert(1)</script>")]
    [InlineData("PNG-pero-no-la-firma")]
    public async Task Un_archivo_que_no_es_PNG_ni_JPEG_se_rechaza_aunque_se_llame_png_y_no_deja_nada_en_disco(string contenido)
    {
        var bytes = System.Text.Encoding.Latin1.GetBytes(contenido);

        await Assert.ThrowsAsync<DomainException>(() => Almacen().GuardarAsync(Guid.NewGuid(), new MemoryStream(bytes)));

        Assert.False(HayArchivos());
    }

    [Fact]
    public async Task Un_archivo_vacio_o_apenas_mas_corto_que_la_firma_se_rechaza()
    {
        await Assert.ThrowsAsync<DomainException>(() => Almacen().GuardarAsync(Guid.NewGuid(), new MemoryStream([])));
        await Assert.ThrowsAsync<DomainException>(() => Almacen().GuardarAsync(Guid.NewGuid(), new MemoryStream([0x89, 0x50])));

        Assert.False(HayArchivos());
    }

    [Fact]
    public async Task Una_imagen_sobre_el_maximo_configurado_se_rechaza_y_el_limite_exacto_pasa()
    {
        var almacen = Almacen(maxKb: 1);
        Assert.Equal(1024, almacen.MaxBytes);

        await Assert.ThrowsAsync<DomainException>(() => almacen.GuardarAsync(Guid.NewGuid(), new MemoryStream(Png(1024 - 8 + 1))));
        Assert.False(HayArchivos());

        var justo = Guid.NewGuid();
        Assert.Equal($"{justo}.png", await almacen.GuardarAsync(justo, new MemoryStream(Png(1024 - 8))));
    }

    [Fact]
    public async Task Un_flujo_sin_fin_se_corta_al_pasar_el_maximo_sin_leerlo_entero()
    {
        var infinito = new FlujoInfinito(Png(0));

        await Assert.ThrowsAsync<DomainException>(() => Almacen(maxKb: 1).GuardarAsync(Guid.NewGuid(), infinito));

        Assert.True(infinito.Leidos <= 1024 + 8192, $"leyó {infinito.Leidos} bytes");
        Assert.False(HayArchivos());
    }

    [Fact]
    public async Task Guardar_dos_veces_el_mismo_envio_reemplaza_el_archivo_sin_dejar_temporales()
    {
        var id = Guid.NewGuid();
        var almacen = Almacen();
        await almacen.GuardarAsync(id, new MemoryStream(Png(10)));

        await almacen.GuardarAsync(id, new MemoryStream(Png(20)));

        Assert.Equal(Png(20), await File.ReadAllBytesAsync(Path.Combine(Carpeta, $"{id}.png")));
        Assert.Single(Directory.GetFileSystemEntries(Carpeta));
    }

    // Criterio 4: el archivo queda en CarpetaPantallazos, nunca bajo wwwroot.
    [Fact]
    public async Task La_carpeta_relativa_se_resuelve_desde_la_raiz_de_contenido_y_queda_fuera_de_wwwroot()
    {
        var web = Path.Combine(_raiz, "src", "Sgl.Web");
        Directory.CreateDirectory(Path.Combine(web, "wwwroot"));
        var almacen = new AlmacenPantallazos(new CambioDomicilioOptions(), web);
        var id = Guid.NewGuid();

        await almacen.GuardarAsync(id, new MemoryStream(Png()));

        var esperado = Path.Combine(_raiz, "datos", "pantallazos", $"{id}.png");
        Assert.True(File.Exists(esperado));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(web, "wwwroot"), "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("wwwroot/pantallazos")]
    [InlineData("wwwroot")]
    [InlineData("wwwroot/../wwwroot/x")]
    public void Una_carpeta_dentro_de_wwwroot_se_rechaza_al_configurar(string carpeta)
    {
        var web = Path.Combine(_raiz, "src", "Sgl.Web");

        Assert.Throws<InvalidOperationException>(() =>
            new AlmacenPantallazos(new CambioDomicilioOptions { CarpetaPantallazos = carpeta }, web));
    }

    [Fact]
    public async Task Abrir_devuelve_el_archivo_y_nada_para_un_nombre_que_no_es_el_del_envio()
    {
        var id = Guid.NewGuid();
        var otro = Guid.NewGuid();
        var almacen = Almacen();
        await almacen.GuardarAsync(otro, new MemoryStream(Png(5)));
        var nombre = await almacen.GuardarAsync(id, new MemoryStream(Png(7)));

        await using (var abierto = await almacen.AbrirAsync(id, nombre))
        {
            Assert.NotNull(abierto);
            using var copia = new MemoryStream();
            await abierto!.CopyToAsync(copia);
            Assert.Equal(Png(7), copia.ToArray());
        }

        Assert.Null(await almacen.AbrirAsync(id, $"{otro}.png"));
        Assert.Null(await almacen.AbrirAsync(id, @"..\..\secreto.png"));
        Assert.Null(await almacen.AbrirAsync(id, "../pantallazos/" + nombre));
        Assert.Null(await almacen.AbrirAsync(id, Path.Combine(Carpeta, nombre)));
        Assert.Null(await almacen.AbrirAsync(id, $"{id}.gif"));
        Assert.Null(await almacen.AbrirAsync(Guid.NewGuid(), $"{id}.jpg"));
    }

    [Fact]
    public async Task Eliminar_borra_solo_el_archivo_del_envio_y_un_nombre_ajeno_no_borra_nada()
    {
        var id = Guid.NewGuid();
        var otro = Guid.NewGuid();
        var almacen = Almacen();
        var nombre = await almacen.GuardarAsync(id, new MemoryStream(Png()));
        await almacen.GuardarAsync(otro, new MemoryStream(Png()));

        almacen.Eliminar(id, $"{otro}.png");
        Assert.Equal(2, Directory.GetFileSystemEntries(Carpeta).Length);
        almacen.Eliminar(id, nombre);
        almacen.Eliminar(id, nombre);

        Assert.Equal([$"{otro}.png"], Directory.GetFileSystemEntries(Carpeta).Select(Path.GetFileName));
    }

    // Criterio 4: el .gitignore del repositorio ignora datos/pantallazos (los pantallazos pueden traer datos personales).
    [Fact]
    public void El_gitignore_ignora_la_carpeta_de_pantallazos_por_defecto()
    {
        var gitignore = File.ReadAllLines(Path.Combine(RaizRepositorio.Buscar()!, ".gitignore")).Select(l => l.Trim());

        Assert.Contains(gitignore, l => l is "/datos/pantallazos/" or "datos/pantallazos/" or "/datos/pantallazos");
    }

    private sealed class FlujoInfinito(byte[] inicio) : Stream
    {
        private long _posicion;

        public long Leidos => _posicion;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _posicion; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                buffer[offset + i] = _posicion + i < inicio.Length ? inicio[_posicion + i] : (byte)0;
            }

            _posicion += count;
            return count;
        }
    }
}
