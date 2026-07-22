using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The configurable target server (§2.3): an explicit BaseUrl must win verbatim, otherwise the host
/// is derived from the subdomain + data-center region. A build that silently ignored BaseUrl would
/// fail these tests.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ExplicitBaseUrl_WinsVerbatim_OverSubdomain()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = "US",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void NoBaseUrl_UsDerivesChargifyHost()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", Environment = "US", BaseUrl = null };

        Assert.Equal("https://apimatic-hackathon.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void NoBaseUrl_EuDerivesEbillingHost()
    {
        var settings = new MaxioSettings { Subdomain = "acme", Environment = "EU", BaseUrl = "  " };

        Assert.Equal("https://acme.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void NoBaseUrl_NoSubdomain_Throws()
    {
        var settings = new MaxioSettings { Subdomain = null, BaseUrl = null };

        Assert.Throws<System.InvalidOperationException>(() => settings.ResolveBaseUrl());
    }
}
