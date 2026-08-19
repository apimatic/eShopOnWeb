using Microsoft.eShopWeb.ApplicationCore.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class MaxioMoneyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(29900, 299)]
    [InlineData(29, 0.29)]
    public void FromCentsConvertsToDecimalDollars(long cents, decimal expected)
    {
        Assert.Equal(expected, MaxioMoney.FromCents(cents));
    }

    [Fact]
    public void FromCentsTreatsNullAsZero()
    {
        Assert.Equal(0m, MaxioMoney.FromCents(null));
    }
}
