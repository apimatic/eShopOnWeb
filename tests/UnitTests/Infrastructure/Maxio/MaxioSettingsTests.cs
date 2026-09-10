using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesBaseUriFromSubdomainPerSpecServerTemplate()
    {
        var settings = new MaxioSettings { Subdomain = "example-site" };

        Assert.Equal(new Uri("https://example-site.chargify.com/"), settings.ResolveBaseUri());
    }

    [Fact]
    public void UsesExplicitBaseUrlVerbatimWhenProvided()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "example-site",
            BaseUrl = "https://proxy.internal.example.com/maxio",
        };

        Assert.Equal(new Uri("https://proxy.internal.example.com/maxio/"), settings.ResolveBaseUri());
    }

    [Fact]
    public void ValidateThrowsWhenApiKeyMissing()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", ProductFamilyHandle = "eshop-subscribe" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void ValidateThrowsWhenNeitherBaseUrlNorSubdomainProvided()
    {
        var settings = new MaxioSettings { ApiKey = "key", ProductFamilyHandle = "eshop-subscribe" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void ValidatePassesWithApiKeySubdomainAndFamily()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "example-site",
            ProductFamilyHandle = "eshop-subscribe",
        };

        settings.Validate();
    }
}
