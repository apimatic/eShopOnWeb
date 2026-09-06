using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesBaseAddressFromSubdomainUsingTheSpecServerTemplate()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "acme-billing", ProductFamilyHandle = "demo-subscriptions" };

        Assert.Equal("https://acme-billing.chargify.com", settings.ResolveBaseAddress());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenSupplied()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme-billing",
            ProductFamilyHandle = "demo-subscriptions",
            BaseUrl = "https://acme-billing.ebilling.maxio.com"
        };

        Assert.Equal("https://acme-billing.ebilling.maxio.com", settings.ResolveBaseAddress());
    }

    [Fact]
    public void TrimsTrailingSlashFromBaseUrlSoSpecPathsAppendCleanly()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            ProductFamilyHandle = "demo-subscriptions",
            BaseUrl = "https://stub.local/maxio/"
        };

        Assert.Equal("https://stub.local/maxio", settings.ResolveBaseAddress());
    }

    [Fact]
    public void BaseUrlOverrideRemovesTheNeedForASubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            ProductFamilyHandle = "demo-subscriptions",
            BaseUrl = "https://stub.local"
        };

        Assert.True(settings.IsConfigured);
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void ReportsEveryMissingSetting()
    {
        var settings = new MaxioSettings();

        var errors = settings.Validate();

        Assert.False(settings.IsConfigured);
        Assert.Contains(errors, error => error.Contains("ApiKey"));
        Assert.Contains(errors, error => error.Contains("Subdomain"));
        Assert.Contains(errors, error => error.Contains("ProductFamilyHandle"));
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAnAbsoluteHttpUrl()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            ProductFamilyHandle = "demo-subscriptions",
            BaseUrl = "not-a-url"
        };

        Assert.Contains(settings.Validate(), error => error.Contains("BaseUrl"));
    }

    [Fact]
    public void DefaultsToRemittanceCollectionSoPlansWithoutACardCanBeSubscribedTo()
    {
        Assert.Equal("remittance", new MaxioSettings().PaymentCollectionMethod);
    }
}
