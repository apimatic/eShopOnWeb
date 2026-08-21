using Microsoft.eShopWeb.Infrastructure.PayPal;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.PayPal;

public class CurrencyFormatterTests
{
    [Theory]
    [InlineData(39.0, "USD", "39.00")]
    [InlineData(8.5, "USD", "8.50")]
    [InlineData(10.005, "USD", "10.01")]   // rounds to the cent
    [InlineData(1000, "JPY", "1000")]      // zero-decimal currency
    [InlineData(1.234, "BHD", "1.234")]    // three-decimal currency
    public void Format_UsesCurrencyMinorUnits(decimal amount, string currency, string expected)
    {
        Assert.Equal(expected, CurrencyFormatter.Format(amount, currency));
    }

    [Theory]
    [InlineData("39.00", 39.0)]
    [InlineData("37.50", 37.5)]
    [InlineData(null, 0.0)]
    public void Parse_ReadsMoneyStrings(string? value, decimal expected)
    {
        Assert.Equal(expected, CurrencyFormatter.Parse(value));
    }
}
