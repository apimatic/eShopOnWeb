using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesBaseAddressFromSubdomainWhenNoOverrideIsSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void UsesBaseUrlOverrideInsteadOfTheSubdomain()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored",
            BaseUrl = "https://acme.ebilling.maxio.com"
        };

        Assert.Equal("https://acme.ebilling.maxio.com/", settings.ResolveBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void KeepsThePathOfABaseUrlOverride()
    {
        // The trailing slash is what stops HttpClient dropping "/maxio" when it resolves relative paths.
        var settings = new MaxioSettings { BaseUrl = "https://gateway.internal/maxio" };

        Assert.Equal("https://gateway.internal/maxio/", settings.ResolveBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void DoesNotAddASecondTrailingSlash()
    {
        var settings = new MaxioSettings { BaseUrl = "https://gateway.internal/maxio/" };

        Assert.Equal("https://gateway.internal/maxio/", settings.ResolveBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAnAbsoluteHttpUrl()
    {
        var settings = new MaxioSettings { BaseUrl = "acme.chargify.com" };

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseAddress());
        Assert.Contains("Maxio:BaseUrl", exception.Message);
    }

    [Fact]
    public void RejectsASubdomainThatIsActuallyAUrl()
    {
        var settings = new MaxioSettings { Subdomain = "https://acme.chargify.com" };

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseAddress());
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void RequiresEitherASubdomainOrABaseUrl()
    {
        Assert.Throws<BillingConfigurationException>(() => new MaxioSettings().ResolveBaseAddress());
    }

    [Fact]
    public void ValidationNamesEveryMissingSetting()
    {
        var exception = Assert.Throws<BillingConfigurationException>(
            () => MaxioSettingsValidator.EnsureValid(new MaxioSettings()));

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }

    [Fact]
    public void ValidationPassesForAFullyConfiguredSite()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "not-a-real-key",
            Subdomain = "acme",
            ProductFamilyHandle = "plans"
        };

        MaxioSettingsValidator.EnsureValid(settings);
    }
}
