using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Payments;

public class MinorUnitsTests
{
    [Theory]
    [InlineData(51.00, "USD", 5100)]
    [InlineData(19.99, "EUR", 1999)]
    [InlineData(0.01, "USD", 1)]
    [InlineData(1500, "JPY", 1500)]
    [InlineData(1.234, "KWD", 1234)]
    public void ToMinor_IsExactToTheCent(decimal amount, string currency, long expected)
    {
        Assert.True(MinorUnits.TryToMinor(amount, currency, out var minor));
        Assert.Equal(expected, minor);
        Assert.Equal(amount, MinorUnits.FromMinor(minor, currency));
    }

    [Theory]
    [InlineData(19.999, "USD")]
    [InlineData(19.5, "JPY")]
    public void ToMinor_RefusesAmountsThatWouldNeedRounding(decimal amount, string currency)
    {
        Assert.False(MinorUnits.TryToMinor(amount, currency, out _));
    }
}
