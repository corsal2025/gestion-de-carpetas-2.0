using Sgl.Web.Peticiones;
using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Application.Estadisticas;
using Sgl.Domain;
using Sgl.Infrastructure.Estadisticas;
using Sgl.Infrastructure;
using Sgl.Infrastructure.Persistence;
using Sgl.Web.Components;
using Sgl.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

// Machine-specific overrides (connection strings, provider). Never committed.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Secrets (Peticion:Ews:Password) via `dotnet user-secrets`, in every environment; they override the JSON files.
builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: false);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSglInfrastructure(builder.Configuration);

// Módulo Peticiones de cambio de domicilio (proyecto PeticionCambioDomicilio integrado tal cual).
builder.AddPeticiones();
builder.Services.AddSingleton(builder.Configuration.GetSection(BandejasOptions.SectionName).Get<BandejasOptions>() ?? new BandejasOptions());
builder.Services.AddScoped<AutorState>();
builder.Services.AddScoped<DuplicadosNotifier>();
builder.Services.AddScoped<ModulosState>();
builder.Services.AddHostedService<IntegridadHostedService>();

var app = builder.Build();

// Migrations are generated for SQLite. For SQL Server, generate a provider-specific
// migration set and apply it explicitly (see README).
if (string.Equals(builder.Configuration[DependencyInjection.ProviderKey], DatabaseProvider.Sqlite, StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SglDbContext>();

    // A schema change never touches the real database without a consistent copy next to it.
    if (db.Database.GetPendingMigrations().Any() && db.Database.GetAppliedMigrations().Any())
    {
        var archivo = db.Database.GetDbConnection().DataSource;
        if (!string.IsNullOrEmpty(archivo) && File.Exists(archivo))
        {
            var copia = $"{archivo}.backup-{DateTime.Now:yyyyMMdd-HHmmss}-premigracion";
            db.Database.ExecuteSqlRaw("VACUUM INTO {0}", copia);
            app.Logger.LogInformation("Copia de seguridad previa a la migración: {Copia}", Path.GetFileName(copia));
        }
    }

    db.Database.Migrate();

    // Phase 3 dates for carpetas that existed before the columns (idempotent; audit-trail facts only).
    var etapas = await EtapaBackfill.EjecutarAsync(db, DateTime.UtcNow);
    if (etapas > 0)
    {
        app.Logger.LogInformation("Fechas de etapa y cierre completadas desde el historial: {Count} carpeta(s).", etapas);
    }

    // Legacy Excel artefact: comuna names stored in "última carpeta". Backup first, idempotent, counts only in the log.
    var pendientes = await LegacyComunaRepair.PendientesAsync(db);
    if (pendientes > 0)
    {
        var file = db.Database.GetDbConnection().DataSource;
        if (!string.IsNullOrEmpty(file) && File.Exists(file))
        {
            var copia = $"{file}.backup-{DateTime.Now:yyyyMMdd-HHmmss}-prereparacion";
            db.Database.ExecuteSqlRaw("VACUUM INTO {0}", copia);
        }

        var reparadas = await LegacyComunaRepair.RepararYRegistrarAsync(db, DateTime.UtcNow);
        app.Logger.LogInformation("Reparación de datos heredados: {Count} carpeta(s) con comuna movida desde 'última carpeta'.", reparadas);
    }

    // Names are always uppercase; legacy rows also lose the "Vecino" surname placeholder. Backup first, idempotent.
    if (await NombresMayusculaRepair.PendientesAsync(db) > 0)
    {
        var file = db.Database.GetDbConnection().DataSource;
        if (!string.IsNullOrEmpty(file) && File.Exists(file))
        {
            var copia = $"{file}.backup-{DateTime.Now:yyyyMMdd-HHmmss}-prenombres";
            db.Database.ExecuteSqlRaw("VACUUM INTO {0}", copia);
        }

        var nombres = await NombresMayusculaRepair.RepararAsync(db);
        app.Logger.LogInformation("Nombres pasados a mayúscula: {Count} ciudadano(s).", nombres);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/certificados/{id:guid}/documento", async (Guid id, HttpContext context, Sgl.Application.Solicitudes.DocumentoCertificadoService documentos, CancellationToken ct) =>
{
    // Restricción de seguridad: no permitir llamadas cruzadas sin origen válido
    if (context.Request.Headers.TryGetValue("Origin", out var origin) && !string.IsNullOrEmpty(origin) && !origin.ToString().Contains("localhost", StringComparison.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    try
    {
        var documento = await documentos.GenerarAsync(id, ct);
        return Results.File(
            documento.Contenido,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            documento.NombreArchivo);
    }
    catch (Sgl.Application.NotFoundException)
    {
        return Results.NotFound();
    }
    catch (Sgl.Domain.DomainException ex)
    {
        // The page only offers the link when the data is complete; this is the fallback for a stale page.
        return Results.Text(ex.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status422UnprocessableEntity);
    }
});
app.MapGet("/estadisticas/exportar", async (HttpContext context, EstadisticasService estadisticas, string? sede, DateOnly? desde, DateOnly? hasta, int? dias, CancellationToken ct) =>
{
    if (context.Request.Headers.TryGetValue("Origin", out var origin) && !string.IsNullOrEmpty(origin) && !origin.ToString().Contains("localhost", StringComparison.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var filtro = new FiltroEstadisticas(
        Enum.TryParse<Sede>(sede, ignoreCase: true, out var s) && Enum.IsDefined(s) ? s : null,
        desde,
        hasta,
        dias ?? 30);
    var datos = await estadisticas.ObtenerAsync(filtro, ct);
    return Results.File(
        EstadisticasExcel.Generar(datos),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"estadisticas-{Sgl.Domain.HoraChile.ALocal(DateTime.UtcNow):yyyyMMdd-HHmm}.xlsx");
});

// Páginas del módulo Peticiones: antes que Blazor para que no las capture el fallback de "no encontrado".
app.MapRazorPages();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
