using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioSettingsTests;

public class Validate
{
    [Fact]
    public void AcceptsAFullyConfiguredIntegration()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "cp-exp-3",
            Environment = "US"
        };

        settings.Validate();
    }

    [Fact]
    public void RejectsAMissingApiKeySoTheHostFailsToStart()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3" };

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.Validate());
        Assert.Contains("ApiKey", exception.Message);
    }

    [Fact]
    public void RejectsABlankApiKey()
    {
        var settings = new MaxioSettings { ApiKey = "   ", Subdomain = "cp-exp-3" };

        Assert.Throws<BillingConfigurationException>(() => settings.Validate());
    }

    [Fact]
    public void RejectsAnIntegrationWithNoReachableHost()
    {
        var settings = new MaxioSettings { ApiKey = "key" };

        Assert.Throws<BillingConfigurationException>(() => settings.Validate());
    }

    [Fact]
    public void RejectsANonPositiveRequestTimeout()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "cp-exp-3",
            RequestTimeoutSeconds = 0
        };

        Assert.Throws<BillingConfigurationException>(() => settings.Validate());
    }

    [Fact]
    public void NeverExposesTheApiKeyInAValidationMessage()
    {
        var settings = new MaxioSettings { ApiKey = "super-secret-key", BaseUrl = "not-a-url" };

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.Validate());

        Assert.DoesNotContain("super-secret-key", exception.Message);
    }
}
