using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Configuration;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_HonorsExplicitOverride_EvenWithSubdomainSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = "US",
            BaseUrl = "http://localhost:8080"
        };

        var resolved = settings.ResolveBaseUrl();

        Assert.Equal("http://localhost:8080/", resolved.ToString());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesUsHost_WhenBaseUrlNotSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = "US",
            BaseUrl = null
        };

        var resolved = settings.ResolveBaseUrl();

        Assert.Equal("https://apimatic-hackathon.chargify.com/", resolved.ToString());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesEuHost_WhenBaseUrlNotSetAndRegionIsEu()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = "EU",
            BaseUrl = ""
        };

        var resolved = settings.ResolveBaseUrl();

        Assert.Equal("https://apimatic-hackathon.ebilling.maxio.com/", resolved.ToString());
    }
}
