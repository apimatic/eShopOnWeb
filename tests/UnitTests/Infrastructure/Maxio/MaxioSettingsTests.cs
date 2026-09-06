using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesTheBaseAddressFromTheSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenItIsSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "acme",
            BaseUrl = "https://billing-gateway.internal/maxio"
        };

        // The override wins over the subdomain, and the path it carries is preserved.
        Assert.Equal("https://billing-gateway.internal/maxio/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void KeepsAnExistingTrailingSlashOnBaseUrl()
    {
        var settings = new MaxioSettings { BaseUrl = "https://acme.ebilling.maxio.com/" };

        Assert.Equal("https://acme.ebilling.maxio.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void IgnoresAnEmptyBaseUrlOverride()
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = "   " };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void DefaultsToRemittanceSoSignupDoesNotNeedAStoredCard()
    {
        Assert.Equal(MaxioCollectionMethods.Remittance, new MaxioSettings().PaymentCollectionMethod);
    }
}
