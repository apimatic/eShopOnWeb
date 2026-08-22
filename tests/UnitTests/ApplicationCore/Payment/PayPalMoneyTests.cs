using Microsoft.eShopWeb.ApplicationCore.Payment;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Payment;

public class PayPalMoneyTests
{
    [Fact]
    public void FormatsUsdToTwoDecimalPlaces()
    {
        Assert.Equal("19.50", PayPalMoney.Format(19.5m, "USD"));
        Assert.Equal("8.50", PayPalMoney.Format(8.5m, "USD"));
    }

    [Fact]
    public void BuyerCustomerIdFitsPayPalPattern()
    {
        var id = PayPalCustomerId.ForBuyer("demouser@microsoft.com");
        Assert.Equal(22, id.Length);
        Assert.Matches("^[0-9a-z]+$", id);
    }
}
