using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void BaseAddressIsDerivedFromTheSubdomainByDefault()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "acme", ProductFamilyHandle = "family" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AnExplicitBaseUrlOverridesTheSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = "family",
            BaseUrl = "https://billing-proxy.internal/maxio/"
        };

        Assert.Equal("https://billing-proxy.internal/maxio/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AnExplicitBaseUrlGetsATrailingSlashSoRelativePathsResolve()
    {
        var settings = new MaxioSettings { ApiKey = "key", ProductFamilyHandle = "family", BaseUrl = "https://acme.example.com" };

        Assert.Equal("https://acme.example.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ABaseUrlAloneIsEnoughToBeConfigured()
    {
        var settings = new MaxioSettings { ApiKey = "key", ProductFamilyHandle = "family", BaseUrl = "https://acme.example.com" };

        Assert.True(settings.IsConfigured);
        Assert.Empty(settings.DescribeMissingSettings());
    }

    [Fact]
    public void MissingSettingsAreReportedByKeyName()
    {
        var missing = new MaxioSettings().DescribeMissingSettings();

        Assert.Contains("Maxio:ApiKey", missing);
        Assert.Contains(missing, m => m.StartsWith("Maxio:Subdomain"));
        Assert.Contains("Maxio:ProductFamilyHandle", missing);
    }

    [Theory]
    [InlineData("", "acme", "family")]
    [InlineData("key", "", "family")]
    [InlineData("key", "acme", "")]
    [InlineData("   ", "acme", "family")]
    public void BlankSettingsCountAsUnset(string apiKey, string subdomain, string family)
    {
        var settings = new MaxioSettings { ApiKey = apiKey, Subdomain = subdomain, ProductFamilyHandle = family };

        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void SubscriptionsAreBilledByInvoiceUnlessConfiguredOtherwise()
    {
        // Sites that do not capture a card up front can only sell if collection is remittance, so
        // that is the default a fresh deployment gets.
        Assert.Equal("remittance", new MaxioSettings().PaymentCollectionMethod);
    }
}
