using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesBaseAddressFromSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "acme", ProductFamilyHandle = "f" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void BaseUrlOverrideWinsOverSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "acme",
            ProductFamilyHandle = "f",
            BaseUrl = "https://acme.ebilling.maxio.com"
        };

        Assert.Equal("https://acme.ebilling.maxio.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void BaseAddressAlwaysEndsInSlashSoRelativePathsResolveUnderIt()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            ProductFamilyHandle = "f",
            BaseUrl = "https://gateway.example.com/api/v1/billing"
        };

        var resolved = settings.ResolveBaseAddress();

        Assert.Equal("https://gateway.example.com/api/v1/billing/", resolved.ToString());
        Assert.Equal("https://gateway.example.com/api/v1/billing/subscriptions.json", new Uri(resolved, "subscriptions.json").ToString());
    }

    [Fact]
    public void BaseUrlAloneIsEnough()
    {
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://elsewhere.example.com/" };

        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public void ReportsEveryMissingKey()
    {
        var errors = new MaxioSettings().GetConfigurationErrors();

        Assert.False(new MaxioSettings().IsConfigured);
        Assert.Contains(errors, e => e.Contains("Maxio:ApiKey"));
        Assert.Contains(errors, e => e.Contains("Maxio:Subdomain"));
        Assert.Contains(errors, e => e.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAnAbsoluteHttpUrl()
    {
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "not-a-url" };

        Assert.False(settings.IsConfigured);
        Assert.Contains(settings.GetConfigurationErrors(), e => e.Contains("Maxio:BaseUrl"));
    }

    [Fact]
    public void ConfigurationErrorsNeverLeakConfiguredValues()
    {
        var settings = new MaxioSettings { ApiKey = "super-secret-key" };

        Assert.DoesNotContain(settings.GetConfigurationErrors(), e => e.Contains("super-secret-key"));
    }
}
