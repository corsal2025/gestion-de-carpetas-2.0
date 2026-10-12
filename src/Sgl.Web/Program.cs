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

// Machine-specific overrides (connection strings, provider). Never committed.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSglInfrastructure(builder.Configuration);
builder.Services.AddSingleton(builder.Configuration.GetSection(BandejasOptions.SectionName).Get<BandejasOptions>() ?? new BandejasOptions());
builder.Services.AddScoped<AutorState>();
builder.Services.AddScoped<DuplicadosNotifier>();
builder.Services.AddScoped<ModulosState>();

var app = builder.Build();

// Migrations are generated for SQLite. For SQL Server, generate a provider-specific
// migration set and apply it explicitly (see README).
if (string.Equals(builder.Configuration[DependencyInjection.ProviderKey], DatabaseProvider.Sqlite, StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<SglDbContext>().Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/estadisticas/exportar", async (EstadisticasService estadisticas, string? sede, DateOnly? desde, DateOnly? hasta, int? dias, CancellationToken ct) =>
{
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
