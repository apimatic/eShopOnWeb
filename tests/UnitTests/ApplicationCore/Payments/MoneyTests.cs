using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Payments;

public class MoneyTests
{
    [Theory]
    [InlineData(51.00, "USD", 5100)]
    [InlineData(10.50, "usd", 1050)]
    [InlineData(0.01, "EUR", 1)]
    [InlineData(1500, "JPY", 1500)]
    [InlineData(1.234, "KWD", 1234)]
    public void ConvertsToExactMinorUnits(decimal amount, string currency, long expected)
    {
        Assert.Equal(expected, Money.ToMinorUnits(amount, currency));
    }

    [Theory]
    [InlineData(0.001, "USD")]
    [InlineData(1.5, "JPY")]
    public void RefusesAmountsThatAreNotWholeMinorUnits(decimal amount, string currency)
    {
        var ex = Assert.Throws<OrderPaymentException>(() => Money.ToMinorUnits(amount, currency));
        Assert.Equal(OrderPaymentError.AmountNotRepresentable, ex.Error);
    }

    [Fact]
    public void RoundTripsMinorUnits()
    {
        Assert.Equal(40.50m, Money.FromMinorUnits(4050, "USD"));
        Assert.Equal(1500m, Money.FromMinorUnits(1500, "JPY"));
    }

    [Theory]
    [InlineData("USD", true)]
    [InlineData("eur", true)]
    [InlineData("XXX", false)]
    [InlineData("US", false)]
    [InlineData(null, false)]
    public void KnowsSupportedCurrencies(string? currency, bool supported)
    {
        Assert.Equal(supported, Money.IsSupportedCurrency(currency));
    }
}
