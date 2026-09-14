using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesBaseUrlFromSubdomainWhenBaseUrlNotSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "acme",
            BaseUrl = "https://acme.example-billing.test/api"
        };

        // BaseUrl overrides the subdomain-derived address (trailing slash trimmed only).
        Assert.Equal("https://acme.example-billing.test/api", settings.ResolveBaseUrl());
    }

    [Fact]
    public void TrimsTrailingSlashFromBaseUrl()
    {
        var settings = new MaxioSettings { BaseUrl = "https://acme.chargify.com/" };

        Assert.Equal("https://acme.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ValidateThrowsWhenApiKeyMissing()
    {
        var settings = new MaxioSettings { Subdomain = "acme", ProductFamilyHandle = "fam" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void ValidateThrowsWhenSubdomainAndBaseUrlBothMissing()
    {
        var settings = new MaxioSettings { ApiKey = "key", ProductFamilyHandle = "fam" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void ValidateThrowsWhenProductFamilyHandleMissing()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "acme" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void ValidatePassesWithBaseUrlInsteadOfSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            BaseUrl = "https://acme.chargify.com",
            ProductFamilyHandle = "fam"
        };

        var exception = Record.Exception(() => settings.Validate());
        Assert.Null(exception);
    }
}
