using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class RutTests
{
    [Theory]
    [InlineData("11.111.111-1", "11111111-1")]
    [InlineData("12345678-5", "12345678-5")]
    [InlineData("123456785", "12345678-5")]
    [InlineData("7.654.321-6", "7654321-6")]
    [InlineData("10.000.013-k", "10000013-K")]
    [InlineData("  10000013K ", "10000013-K")]
    public void Parse_valid_rut_returns_normalized_value(string input, string expected)
    {
        var rut = Rut.Parse(input);

        Assert.Equal(expected, rut.Value);
    }

    [Theory]
    [InlineData("12345678-9")]
    [InlineData("11.111.111-2")]
    [InlineData("10000013-1")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1-")]
    [InlineData("12.345.67A-5")]
    [InlineData("1234567890123-1")]
    public void Parse_invalid_rut_throws_domain_exception(string input)
    {
        Assert.Throws<DomainException>(() => Rut.Parse(input));
    }

    [Fact]
    public void TryParse_returns_false_for_invalid_and_true_for_valid()
    {
        Assert.False(Rut.TryParse("12345678-9", out _));
        Assert.True(Rut.TryParse("12345678-5", out var rut));
        Assert.Equal("12345678-5", rut!.Value);
    }

    [Theory]
    [InlineData("12345678", '5')]
    [InlineData("10000013", 'K')]
    [InlineData("11111111", '1')]
    [InlineData("7654321", '6')]
    public void ComputeCheckDigit_follows_modulo_11(string body, char expected)
    {
        Assert.Equal(expected, Rut.ComputeCheckDigit(body));
    }

    [Fact]
    public void Ruts_with_same_value_are_equal()
    {
        Assert.Equal(Rut.Parse("12.345.678-5"), Rut.Parse("123456785"));
    }

    [Fact]
    public void Formatted_uses_thousands_separators()
    {
        Assert.Equal("12.345.678-5", Rut.Parse("123456785").Formatted);
    }
}
