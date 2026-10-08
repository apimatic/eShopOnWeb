using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Payments;

public class CurrencyMinorUnitsTests
{
    [Theory]
    [InlineData("51.00", "USD", 5100)]
    [InlineData("8.5", "USD", 850)]
    [InlineData("19.99", "eur", 1999)]
    [InlineData("1500", "JPY", 1500)]
    [InlineData("1.234", "KWD", 1234)]
    public void ConvertsExactly(string amount, string currency, long expected)
    {
        Assert.Equal(expected, CurrencyMinorUnits.ToMinor(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency));
    }

    [Theory]
    [InlineData("12.345", "USD")]
    [InlineData("10.5", "JPY")]
    public void RefusesAmountsThatWouldNeedRounding(string amount, string currency)
    {
        Assert.Throws<PaymentValidationException>(() =>
            CurrencyMinorUnits.ToMinor(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency));
    }

    [Fact]
    public void RoundTrips()
    {
        Assert.Equal(38.5m, CurrencyMinorUnits.FromMinor(3850, "USD"));
    }
}
