using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesTheAddressFromTheSiteSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "example-site" };

        Assert.Equal(new Uri("https://example-site.chargify.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_PrefersAnExplicitBaseUrlOverTheSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "example-site",
            BaseUrl = "https://billing.internal.example.com/maxio/"
        };

        Assert.Equal(new Uri("https://billing.internal.example.com/maxio/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_AllowsABaseUrlWithoutATrailingSlash()
    {
        // Relative request paths are only appended to a base address that ends in a slash.
        var settings = new MaxioSettings { ApiKey = "key", BaseUrl = "https://billing.example.com" };

        Assert.Equal(new Uri("https://billing.example.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_RejectsAMalformedBaseUrl()
    {
        var settings = new MaxioSettings { ApiKey = "key", BaseUrl = "not a url" };

        Assert.Throws<FormatException>(() => settings.ResolveBaseAddress());
    }

    [Theory]
    [InlineData(null, "example-site", "demo-subscriptions")]
    [InlineData("key", null, "demo-subscriptions")]
    [InlineData("key", "example-site", null)]
    public void Validate_RejectsAnIncompleteConfiguration(string? apiKey, string? subdomain, string? productFamily)
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            ApiKey = apiKey,
            Subdomain = subdomain,
            ProductFamilyHandle = productFamily
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_AcceptsABaseUrlInPlaceOfASubdomain()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            ApiKey = "key",
            BaseUrl = "https://billing.example.com",
            ProductFamilyHandle = "demo-subscriptions"
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void MissingConfigurationIsReportedAsAnActionableBillingFailure()
    {
        // Validation runs on resolution rather than at startup, so an unconfigured deployment
        // still boots - but reading the settings must fail with a message an operator can act on.
        var options = new OptionsManager<MaxioSettings>(new OptionsFactory<MaxioSettings>(
            Array.Empty<IConfigureOptions<MaxioSettings>>(),
            Array.Empty<IPostConfigureOptions<MaxioSettings>>(),
            new[] { new MaxioSettingsValidator() }));

        var exception = Assert.Throws<BillingConfigurationException>(() => MaxioOptionsAccessor.Resolve(options));

        Assert.Contains("Maxio:ApiKey is required", exception.Message);
    }
}
