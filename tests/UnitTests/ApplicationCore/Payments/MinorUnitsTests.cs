using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Payments;

public class MinorUnitsTests
{
    [Theory]
    [InlineData("47.50", "USD", 4750)]
    [InlineData("19.5", "EUR", 1950)]
    [InlineData("0.01", "USD", 1)]
    [InlineData("1000", "JPY", 1000)]
    [InlineData("1.234", "KWD", 1234)]
    public void ConvertsExactly(string amount, string currency, long expected)
    {
        Assert.True(MinorUnits.TryToMinorUnits(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency, out var minor));
        Assert.Equal(expected, minor);
    }

    [Theory]
    [InlineData("10.005", "USD")]
    [InlineData("10.5", "JPY")]
    public void RefusesToRoundAnAmountTheCurrencyCannotRepresent(string amount, string currency)
    {
        Assert.False(MinorUnits.TryToMinorUnits(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency, out _));
    }

    [Fact]
    public void RoundTrips()
    {
        Assert.Equal(47.50m, MinorUnits.FromMinorUnits(4750, "USD"));
    }
}
