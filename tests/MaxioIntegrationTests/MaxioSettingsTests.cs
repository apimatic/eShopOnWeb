using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The configurable target server is a hard requirement: the same build must be able to reach
/// production, a dev/sandbox tenant, or a local mock purely through configuration.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ExplicitBaseUrlWinsVerbatimOverTheSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "apimatic-hackathon",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlIsNotRewrittenOrSuffixed()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "ignored",
            BaseUrl = "https://billing.internal.example.com/api/v2"
        };

        Assert.Equal("https://billing.internal.example.com/api/v2", settings.ResolveBaseUrl());
    }

    [Fact]
    public void SurroundingWhitespaceOnAnExplicitBaseUrlIsTrimmed()
    {
        var settings = new MaxioSettings { ApiKey = "key", BaseUrl = "  http://localhost:8080  " };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void AnEmptyBaseUrlFallsBackToTheSubdomainDerivedUsHost()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "apimatic-hackathon",
            Environment = MaxioSettings.UsRegion,
            BaseUrl = string.Empty
        };

        Assert.Equal("https://apimatic-hackathon.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void TheEuropeanRegionDerivesADifferentHost()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "apimatic-hackathon",
            Environment = MaxioSettings.EuRegion
        };

        Assert.True(settings.IsEuropeanRegion);
        Assert.Equal("https://apimatic-hackathon.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void TheRegionIsMatchedCaseInsensitively()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "site", Environment = "eu" };

        Assert.True(settings.IsEuropeanRegion);
    }

    [Fact]
    public void NeitherABaseUrlNorASubdomainIsAConfigurationFailure()
    {
        var settings = new MaxioSettings { ApiKey = "key" };

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("Maxio:BaseUrl", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void TheOutboundRequestActuallyTargetsTheConfiguredMockServer()
    {
        var settings = BillingClientFixture.DefaultSettings();
        settings.BaseUrl = "http://localhost:8080";

        var handler = new StubHttpMessageHandler();
        handler.RespondWith(ProviderPayloads.ProPlan);

        var client = BillingClientFixture.Build(settings, handler);
        _ = client.FindPlanByHandleAsync("eshop-pro").GetAwaiter().GetResult();

        Assert.Equal("localhost", handler.LastRequest.RequestUri!.Host);
        Assert.Equal(8080, handler.LastRequest.RequestUri.Port);
    }

    [Fact]
    public void TheOutboundRequestTargetsTheSubdomainDerivedHostWhenNoOverrideIsSet()
    {
        var settings = BillingClientFixture.DefaultSettings();
        settings.Subdomain = "apimatic-hackathon";
        settings.BaseUrl = null;

        var handler = new StubHttpMessageHandler();
        handler.RespondWith(ProviderPayloads.ProPlan);

        var client = BillingClientFixture.Build(settings, handler);
        _ = client.FindPlanByHandleAsync("eshop-pro").GetAwaiter().GetResult();

        Assert.Equal("apimatic-hackathon.chargify.com", handler.LastRequest.RequestUri!.Host);
    }
}
