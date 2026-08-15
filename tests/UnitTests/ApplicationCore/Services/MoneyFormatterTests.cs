using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class MoneyFormatterTests
{
    [Theory]
    [InlineData("USD", 29, "29.00")]
    [InlineData("USD", 20.5, "20.50")]
    [InlineData("USD", 8.5, "8.50")]
    [InlineData("EUR", 12.345, "12.35")]   // rounds to 2 minor units
    [InlineData("JPY", 1000, "1000")]      // zero-decimal currency
    [InlineData("BHD", 1.2, "1.200")]      // three-decimal currency
    public void FormatsToCurrencyMinorUnits(string currency, decimal amount, string expected)
    {
        Assert.Equal(expected, MoneyFormatter.Format(amount, currency));
    }

    [Fact]
    public void FormatIsInvariantOfCulture()
    {
        // Always a '.' decimal separator regardless of the current culture.
        Assert.Equal("1234.56", MoneyFormatter.Format(1234.56m, "USD"));
    }

    [Theory]
    [InlineData("19.48", 19.48)]
    [InlineData("1000", 1000)]
    public void ParsesPayPalStrings(string value, decimal expected)
    {
        Assert.Equal(expected, MoneyFormatter.Parse(value));
    }

    [Fact]
    public void TryParseReturnsNullForInvalid()
    {
        Assert.Null(MoneyFormatter.TryParse(null));
        Assert.Null(MoneyFormatter.TryParse("not-a-number"));
        Assert.Equal(5.00m, MoneyFormatter.TryParse("5.00"));
    }
}
