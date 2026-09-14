using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesUsBaseAddressFromSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "acme" };

        Assert.Equal(new Uri("https://acme.chargify.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void DerivesEuBaseAddressFromSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "acme", Environment = "eu" };

        Assert.Equal(new Uri("https://acme.ebilling.maxio.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void UsesBaseUrlOverrideVerbatim()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "acme", BaseUrl = "https://billing.test/gateway" };

        Assert.Equal(new Uri("https://billing.test/gateway/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void BaseUrlOverrideWorksWithoutSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "key", BaseUrl = "https://billing.test/" };

        Assert.True(settings.IsConfigured);
        Assert.Equal(new Uri("https://billing.test/"), settings.ResolveBaseAddress());
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "acme", null)]
    [InlineData("key", "", "")]
    public void IsNotConfiguredWithoutCredentials(string? apiKey, string? subdomain, string? baseUrl)
    {
        var settings = new MaxioSettings { ApiKey = apiKey, Subdomain = subdomain, BaseUrl = baseUrl };

        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void RejectsMalformedBaseUrl()
    {
        var settings = new MaxioSettings { ApiKey = "key", BaseUrl = "not a url" };

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseAddress());
    }

    [Fact]
    public void ClampsTuningValuesToSaneRanges()
    {
        var settings = new MaxioSettings
        {
            TimeoutSeconds = 100_000,
            MaxRetryAttempts = -5,
            RetryBaseDelayMilliseconds = 1,
            PlanCacheSeconds = 0
        };

        Assert.Equal(TimeSpan.FromSeconds(300), settings.ResolveTimeout());
        Assert.Equal(0, settings.ResolveRetryAttempts());
        Assert.Equal(TimeSpan.FromMilliseconds(10), settings.ResolveRetryBaseDelay());
        Assert.Null(settings.ResolvePlanCacheDuration());
    }
}
