using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Payments;

public class MinorUnitsTests
{
    [Theory]
    [InlineData(47.50, "USD", 4750)]
    [InlineData(0.01, "EUR", 1)]
    [InlineData(1234, "JPY", 1234)]
    [InlineData(1.234, "KWD", 1234)]
    [InlineData(19.5, "usd", 1950)]
    public void ConvertsExactly(decimal amount, string currency, long expected)
    {
        Assert.True(MinorUnits.TryFromDecimal(amount, currency, out var minor));
        Assert.Equal(expected, minor);
        Assert.Equal(amount, MinorUnits.ToDecimal(minor, currency));
    }

    [Theory]
    [InlineData(0.005, "USD")]
    [InlineData(10.5, "JPY")]
    [InlineData(1.2345, "KWD")]
    public void RefusesToRound(decimal amount, string currency)
    {
        Assert.False(MinorUnits.TryFromDecimal(amount, currency, out _));
    }
}
