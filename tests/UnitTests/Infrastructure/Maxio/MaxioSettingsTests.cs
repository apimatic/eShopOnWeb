using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesUsHostFromSubdomainByDefault()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal(new Uri("https://acme.chargify.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void DerivesEuHostWhenEnvironmentIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "acme", Environment = "eu" };

        Assert.Equal(new Uri("https://acme.ebilling.maxio.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenSupplied()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored",
            Environment = "EU",
            BaseUrl = "https://billing.internal.example.com/"
        };

        Assert.Equal(new Uri("https://billing.internal.example.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void AppendsTrailingSlashToBaseUrlSoRelativePathsResolve()
    {
        var settings = new MaxioSettings { BaseUrl = "https://billing.internal.example.com/api/v1" };

        Assert.Equal(new Uri("https://billing.internal.example.com/api/v1/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void ThrowsWhenNeitherSubdomainNorBaseUrlIsSet()
    {
        var settings = new MaxioSettings { ApiKey = "key" };

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseAddress());
    }

    [Theory]
    [InlineData(null, "acme", "family", false)]
    [InlineData("key", null, "family", false)]
    [InlineData("key", "acme", null, false)]
    [InlineData("key", "acme", "family", true)]
    public void IsConfiguredRequiresKeySiteAndFamily(string? apiKey, string? subdomain, string? family, bool expected)
    {
        var settings = new MaxioSettings
        {
            ApiKey = apiKey,
            Subdomain = subdomain,
            ProductFamilyHandle = family
        };

        Assert.Equal(expected, settings.IsConfigured);
    }

    [Fact]
    public void BaseUrlSatisfiesTheSiteRequirementOnItsOwn()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            BaseUrl = "https://billing.internal.example.com",
            ProductFamilyHandle = "family"
        };

        Assert.True(settings.IsConfigured);
    }
}
