using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class BillingHelpersTests
{
    [Fact]
    public void ShopperNameSplitsEmail()
    {
        var (first, last) = ShopperName.FromEmail("demouser@microsoft.com", null);
        Assert.Equal("Demouser", first);
        Assert.Equal("Microsoft", last);
    }

    [Fact]
    public void SubscriptionReferenceIsStablePerUserAndPlan()
    {
        Assert.Equal("abc:eshop-pro", SubscriptionReference.ForPlan("abc", "eshop-pro"));
    }

    [Fact]
    public void MoneyConvertsCents()
    {
        Assert.Equal(29.00m, Money.FromCents(2900));
        Assert.Equal(299.00m, Money.FromCents(29900));
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", true)]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    public void BillingStateClassifiesEnrollment(string state, bool expected)
    {
        Assert.Equal(expected, BillingState.IsExistingEnrollment(state));
    }

    [Fact]
    public void MaxioOptionsUsesBaseUrlOverrideVerbatim()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://example.test/maxio"
        };

        Assert.Equal("https://example.test/maxio/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void MaxioOptionsDerivesChargifyUrlFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-1" };

        Assert.Equal("https://cp-exp-1.chargify.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void MaxioOptionsDerivesEuUrlFromEnvironment()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-1", Environment = "EU" };

        Assert.Equal("https://cp-exp-1.ebilling.maxio.com/", options.ResolveBaseAddress().ToString());
    }
}
