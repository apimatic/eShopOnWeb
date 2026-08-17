using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class PhoneMaskTests
{
    [Fact]
    public void ShowsOnlyLastFourDigits()
    {
        Assert.Equal("***1588", PhoneMask.Mask("+18254751588"));
    }

    [Fact]
    public void DoesNotLeakShortNumbers()
    {
        Assert.Equal("****", PhoneMask.Mask("123"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void HandlesMissingNumbers(string? input)
    {
        Assert.Equal("unknown", PhoneMask.Mask(input));
    }
}
