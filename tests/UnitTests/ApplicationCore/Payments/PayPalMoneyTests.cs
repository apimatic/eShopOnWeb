using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Payments;

public class PayPalMoneyTests
{
    [Fact]
    public void FormatsUsdToTwoFractionDigits()
    {
        Assert.Equal("19.50", PayPalMoney.Format(19.5m, "USD"));
        Assert.Equal("8.50", PayPalMoney.Format(8.5m, "usd"));
        Assert.Equal("12.00", PayPalMoney.Format(12m, "USD"));
    }

    [Fact]
    public void ParsesInvariantDecimalStrings()
    {
        Assert.Equal(19.50m, PayPalMoney.Parse("19.50"));
        Assert.Equal(0m, PayPalMoney.Parse(null));
    }
}
