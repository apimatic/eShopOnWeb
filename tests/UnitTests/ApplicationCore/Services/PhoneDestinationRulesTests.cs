using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PhoneDestinationRulesTests
{
    [Fact]
    public void AcceptsValidMobile()
    {
        Assert.True(PhoneDestinationRules.IsUsableSmsDestination(true, "mobile", null));
    }

    [Fact]
    public void RejectsLandline()
    {
        Assert.False(PhoneDestinationRules.IsUsableSmsDestination(true, "landline", null));
    }

    [Fact]
    public void RejectsInvalidRange()
    {
        Assert.False(PhoneDestinationRules.IsUsableSmsDestination(false, "mobile", null));
    }

    [Fact]
    public void AcceptsWhenLineTypePackageUnavailable()
    {
        Assert.True(PhoneDestinationRules.IsUsableSmsDestination(true, "mobile", 10001));
        Assert.True(PhoneDestinationRules.IsUsableSmsDestination(true, null, null));
    }

    [Fact]
    public void AcceptsUnknownTypeWhenValid()
    {
        Assert.True(PhoneDestinationRules.IsUsableSmsDestination(true, "unknown", null));
    }
}
