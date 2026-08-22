using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class MoneyFormatterTests
{
    [Fact]
    public void FormatsUsdToTheCent()
    {
        Assert.Equal("19.50", MoneyFormatter.ToPayPalValue(19.5m, "USD"));
        Assert.Equal("8.50", MoneyFormatter.ToPayPalValue(8.5m, "USD"));
        Assert.Equal("12.00", MoneyFormatter.ToPayPalValue(12m, "USD"));
    }

    [Fact]
    public void ParsesPayPalAmountStrings()
    {
        Assert.Equal(19.50m, MoneyFormatter.Parse("19.50"));
        Assert.Equal(0m, MoneyFormatter.Parse(null));
    }
}
