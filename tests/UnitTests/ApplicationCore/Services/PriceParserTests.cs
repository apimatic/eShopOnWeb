using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PriceParserTests
{
    [Theory]
    [InlineData("$189.99", 189.99)]
    [InlineData("349.00", 349.00)]
    [InlineData("USD 64.99", 64.99)]
    [InlineData("$1,299.00", 1299.00)]
    [InlineData("  $12.49 ", 12.49)]
    public void ParsesNumericPrices(string text, double expected)
    {
        var ok = PriceParser.TryParse(text, out var price);

        Assert.True(ok);
        Assert.Equal((decimal)expected, price);
    }

    [Theory]
    [InlineData("Contact for pricing")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Free")]
    [InlineData("$0.00")]
    public void RejectsNonNumericOrZeroPrices(string? text)
    {
        var ok = PriceParser.TryParse(text, out var price);

        Assert.False(ok);
        Assert.Equal(0m, price);
    }
}
