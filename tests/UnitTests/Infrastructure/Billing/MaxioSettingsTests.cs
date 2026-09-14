using System;
using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSettingsTests
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "demo-subscriptions"
    };

    [Fact]
    public void AFullyConfiguredSectionHasNoProblems()
    {
        Assert.Empty(Valid().Validate());
    }

    [Fact]
    public void AMissingApiKeyIsReported()
    {
        var settings = Valid();
        settings.ApiKey = null;

        Assert.Contains(settings.Validate(), problem => problem.Contains("Maxio:ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingProductFamilyHandleIsReported()
    {
        var settings = Valid();
        settings.ProductFamilyHandle = " ";

        Assert.Contains(settings.Validate(), problem => problem.Contains("Maxio:ProductFamilyHandle", StringComparison.Ordinal));
    }

    [Fact]
    public void ABaseUrlOnItsOwnIsEnoughToAddressASite()
    {
        var settings = Valid();
        settings.Subdomain = null;
        settings.BaseUrl = "https://billing.example.internal";

        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void NeitherASubdomainNorABaseUrlIsReported()
    {
        var settings = Valid();
        settings.Subdomain = null;
        settings.BaseUrl = null;

        Assert.Contains(settings.Validate(), problem => problem.Contains("Maxio:BaseUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void ARelativeBaseUrlIsReported()
    {
        var settings = Valid();
        settings.BaseUrl = "/not-absolute";

        Assert.Contains(settings.Validate(), problem => problem.Contains("absolute URL", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownEnvironmentIsReported()
    {
        var settings = Valid();
        settings.Environment = "MARS";

        Assert.Contains(settings.Validate(), problem => problem.Contains("Maxio:Environment", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSubdomainIsSubstitutedIntoTheServerTemplate()
    {
        var options = MaxioBillingServiceCollectionExtensions.BuildOptions(Valid());

        Assert.Contains("{site}", options.Server.Production.Us.BaseUrl, StringComparison.Ordinal);
        Assert.Equal("test-site", options.Server.Production.Us.Site);
    }

    [Fact]
    public void AConfiguredBaseUrlIsUsedVerbatim()
    {
        var settings = Valid();
        settings.BaseUrl = "https://billing.example.internal/v1";

        var options = MaxioBillingServiceCollectionExtensions.BuildOptions(settings);

        // No {site} token, so nothing is substituted and the address is used exactly as configured.
        Assert.Equal("https://billing.example.internal/v1", options.Server.Production.Us.BaseUrl);
    }

    [Fact]
    public void TheEuEnvironmentIsConfiguredOnItsOwnBranch()
    {
        var settings = Valid();
        settings.Environment = "eu";

        var options = MaxioBillingServiceCollectionExtensions.BuildOptions(settings);

        // Only the branch matching the selected environment is ever read.
        Assert.Equal("test-site", options.Server.Production.Eu.Site);
    }

    [Fact]
    public void TheApiKeyIsSentAsTheBasicAuthUserName()
    {
        var options = MaxioBillingServiceCollectionExtensions.BuildOptions(Valid());

        Assert.NotNull(options.BasicAuth);
        Assert.Equal("test-key", options.BasicAuth!.Username);
    }

    [Fact]
    public void RetriesAreBoundedAndAttemptsAreTimedOut()
    {
        var options = MaxioBillingServiceCollectionExtensions.BuildOptions(Valid());

        Assert.Equal(1, options.Retry.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Retry.Timeout);
    }

    [Fact]
    public void RetriesNeverDropBelowTheFloorTheRetryPipelineAccepts()
    {
        var settings = Valid();
        settings.MaxRetries = 0;

        // The pipeline rejects zero at construction, so a zero in configuration is clamped rather than
        // passed through to a client that would fail to build.
        Assert.Equal(1, MaxioBillingServiceCollectionExtensions.BuildOptions(settings).Retry.MaxRetries);
    }
}
