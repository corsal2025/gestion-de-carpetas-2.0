using Microsoft.EntityFrameworkCore;
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
builder.Services.AddScoped<AutorState>();

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
