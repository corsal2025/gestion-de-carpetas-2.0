using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sgl.Application.Carpetas;
using Sgl.Infrastructure;

namespace Sgl.Application.Tests;

public class LicenciasOptionsTests
{
    private static LicenciasOptions Resolve(string? fecha)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Sgl"] = "Data Source=:memory:",
            ["Licencias:ClaseObligatoriaDesde"] = fecha,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var provider = new ServiceCollection().AddSglInfrastructure(config).BuildServiceProvider();
        return provider.GetRequiredService<LicenciasOptions>();
    }

    [Fact]
    public void Missing_date_disables_mandatory_class()
    {
        var options = Resolve(null);

        Assert.Null(options.ClaseObligatoriaDesde);
        Assert.False(options.ClaseEsObligatoria(new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void Configured_date_applies_from_that_day()
    {
        var options = Resolve("2026-06-01");

        Assert.False(options.ClaseEsObligatoria(new DateOnly(2026, 5, 31)));
        Assert.True(options.ClaseEsObligatoria(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Invalid_date_fails_fast()
    {
        Assert.Throws<InvalidOperationException>(() => Resolve("mañana"));
    }
}
