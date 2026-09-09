using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUri_DerivesChargifyHost_FromSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-7" };
        Assert.Equal("https://cp-exp-7.chargify.com/", settings.ResolveBaseUri().ToString());
    }

    [Fact]
    public void ResolveBaseUri_UsesBaseUrlVerbatim_WhenProvided()
    {
        var settings = new MaxioSettings { Subdomain = "ignored", BaseUrl = "https://proxy.example.com/maxio" };
        Assert.Equal("https://proxy.example.com/maxio/", settings.ResolveBaseUri().ToString());
    }

    [Fact]
    public void Validate_Throws_WhenApiKeyMissing()
    {
        var settings = new MaxioSettings { Subdomain = "s", ProductFamilyHandle = "f" };
        Assert.Throws<System.InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenSubdomainAndBaseUrlMissing()
    {
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f" };
        Assert.Throws<System.InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Passes_WhenBaseUrlProvidedWithoutSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "k", BaseUrl = "https://x.example.com", ProductFamilyHandle = "f" };
        settings.Validate(); // should not throw
    }
}
