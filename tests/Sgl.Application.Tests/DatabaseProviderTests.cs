using Microsoft.EntityFrameworkCore;
using Sgl.Infrastructure;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public class DatabaseProviderTests
{
    [Theory]
    [InlineData("Sqlite", "Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    public void Configure_selects_provider_by_name(string provider, string expectedProvider)
    {
        var builder = new DbContextOptionsBuilder<SglDbContext>();

        DatabaseProvider.Configure(builder, provider, "Data Source=x");

        using var ctx = new SglDbContext(builder.Options);
        Assert.Equal(expectedProvider, ctx.Database.ProviderName);
    }

    [Fact]
    public void Configure_rejects_unknown_provider()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseProvider.Configure(new DbContextOptionsBuilder<SglDbContext>(), "Oracle", "x"));
    }
}
