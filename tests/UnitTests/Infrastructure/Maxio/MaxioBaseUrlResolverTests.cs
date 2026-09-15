using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBaseUrlResolverTests
{
    [Fact]
    public void Resolve_UsesBaseUrlWhenSet()
    {
        var settings = new MaxioSettings
        {
            BaseUrl = "https://custom.example.test/v1/",
            Subdomain = "ignored"
        };

        var url = MaxioBaseUrlResolver.Resolve(settings, "EU");

        Assert.Equal("https://custom.example.test/v1", url);
    }

    [Fact]
    public void Resolve_UsesUsChargifyHostByDefault()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4" };

        var url = MaxioBaseUrlResolver.Resolve(settings);

        Assert.Equal("https://cp-exp-4.chargify.com", url);
    }

    [Fact]
    public void Resolve_UsesEuHostWhenEnvironmentIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        var url = MaxioBaseUrlResolver.Resolve(settings, "EU");

        Assert.Equal("https://acme.ebilling.maxio.com", url);
    }

    [Fact]
    public void Resolve_ThrowsWhenSubdomainAndBaseUrlMissing()
    {
        Assert.Throws<MaxioConfigurationException>(() => MaxioBaseUrlResolver.Resolve(new MaxioSettings()));
    }
}
