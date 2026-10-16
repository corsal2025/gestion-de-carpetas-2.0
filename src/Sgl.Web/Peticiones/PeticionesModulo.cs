using PeticionCambioDomicilio;
using PeticionCambioDomicilio.Comunas;
using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;
using PeticionCambioDomicilio.Ews;
using PeticionCambioDomicilio.Excel;

namespace Sgl.Web.Peticiones;

/// <summary>
/// Módulo "Peticiones de cambio de domicilio": el proyecto PeticionCambioDomicilio integrado tal cual.
/// Mismos servicios, mismas páginas (bajo /peticiones) y misma lógica; la base de datos es sgl.db.
/// </summary>
public static class PeticionesModulo
{
    public static void AddPeticiones(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();
        builder.Services.AddSingleton(options);

        var contentRoot = builder.Environment.ContentRootPath;

        var comunaCsv = !string.IsNullOrWhiteSpace(options.ComunaDirectoryCsvPath)
            ? Path.GetFullPath(Path.Combine(contentRoot, options.ComunaDirectoryCsvPath))
            : Path.Combine(contentRoot, "data", "comunas.csv");

        // Si el CSV no existe (o quedó vacío), se escribe desde el recurso embebido del módulo.
        if (!File.Exists(comunaCsv) || new FileInfo(comunaCsv).Length == 0)
        {
            var asm = typeof(ComunaDirectory).Assembly;
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("comunas.seed.csv", StringComparison.OrdinalIgnoreCase));
            if (resName is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(comunaCsv)!);
                using var res = asm.GetManifestResourceStream(resName)!;
                using var outFile = File.Create(comunaCsv);
                res.CopyTo(outFile);
            }
        }

        builder.Services.AddSingleton(new ComunaDirectory(comunaCsv));

        // La base de datos del módulo es la base principal del sistema (sgl.db).
        var dbPath = Path.Combine(contentRoot, "sgl.db");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var backupDirectory = !string.IsNullOrWhiteSpace(options.BackupDirectory)
            ? options.BackupDirectory
            : Path.Combine(userProfile, "Documents", "RespaldoPeticionCambioDomicilio");
        builder.Services.AddSingleton(new PeticionRepository(dbPath, backupDirectory));
        builder.Services.AddHostedService<PeticionBackupService>();

        builder.Services.AddSingleton<IMailSender, EwsMailSender>();
        builder.Services.AddSingleton<ExcelPeticionImporter>();
        builder.Services.AddSingleton<PeticionSender>();
        builder.Services.AddSingleton<Sgl.Peticiones.IMenuSistema, MenuSistemaCatalogo>();

        // Las páginas originales viven bajo /peticiones.
        builder.Services.AddRazorPages()
            .AddApplicationPart(typeof(PeticionCambioDomicilio.Pages.IndexModel).Assembly);
    }
}
