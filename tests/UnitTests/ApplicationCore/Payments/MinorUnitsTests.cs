using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Payments;

public class MinorUnitsTests
{
    [Theory]
    [InlineData("19.50", "USD", 1950)]
    [InlineData("0.01", "EUR", 1)]
    [InlineData("8.5", "USD", 850)]
    [InlineData("1500", "JPY", 1500)]
    [InlineData("1.234", "KWD", 1234)]
    [InlineData("123456.78", "USD", 12345678)]
    public void Converts_exactly(string amount, string currency, long expected)
    {
        Assert.True(MinorUnits.TryConvert(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency, out var minor));
        Assert.Equal(expected, minor);
    }

    [Theory]
    [InlineData("19.505", "USD")]
    [InlineData("10.5", "JPY")]
    [InlineData("1.00", "us")]
    public void Refuses_amounts_that_are_not_exact_in_minor_units(string amount, string currency)
    {
        Assert.False(MinorUnits.TryConvert(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency, out _));
    }
}
