using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The outbound target must be decided by configuration alone, so the same build can be pointed at
/// production, a sandbox tenant or a local mock.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ExplicitBaseUrlWinsOverTheSubdomain()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-2",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlLosesItsTrailingSlashButNothingElse()
    {
        var settings = new MaxioSettings { BaseUrl = "https://mock.internal:9443/maxio/" };

        Assert.Equal("https://mock.internal:9443/maxio", settings.ResolveBaseUrl());
    }

    [Fact]
    public void UsHostIsDerivedFromTheSubdomainWhenNoBaseUrlIsConfigured()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", Environment = "US" };

        Assert.Equal("https://cp-exp-2.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void EuropeanRegionDerivesTheEuropeanHost()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", Environment = "eu" };

        Assert.True(settings.IsEuropeanRegion);
        Assert.Equal("https://cp-exp-2.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void AnEmptyBaseUrlFallsBackToTheDerivedHostRatherThanBeingUsed()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", BaseUrl = "   " };

        Assert.Equal("https://cp-exp-2.chargify.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://mock.internal")]
    [InlineData("/relative/path")]
    public void ABaseUrlThatIsNotAnAbsoluteHttpUrlIsRefused(string configured)
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", BaseUrl = configured };

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("Maxio:BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNeitherABaseUrlNorASubdomainTheTargetCannotBeResolved()
    {
        var settings = new MaxioSettings();

        Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
        Assert.False(settings.TryResolveBaseUrl(out var baseUrl));
        Assert.Null(baseUrl);
    }

    [Fact]
    public void TimeoutAndRetryCountAreClampedToSaneValues()
    {
        var settings = new MaxioSettings { TimeoutSeconds = -5, MaxRetries = 99 };

        Assert.Equal(TimeSpan.FromSeconds(1), settings.Timeout);
        Assert.Equal(10, settings.RetryCount);
    }
}
