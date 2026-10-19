using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSettingsValidatorTests
{
    private readonly MaxioSettingsValidator _validator = new();

    [Fact]
    public void CompleteSettings_AreValid()
    {
        Assert.True(_validator.Validate(null, MaxioTestHost.Settings()).Succeeded);
    }

    [Theory]
    [InlineData("", "example-site", "example-family", null, "Maxio:ApiKey")]
    [InlineData("   ", "example-site", "example-family", null, "Maxio:ApiKey")]
    [InlineData("k", "example-site", "", null, "Maxio:ProductFamilyHandle")]
    [InlineData("k", "", "example-family", null, "Maxio:Subdomain")]
    [InlineData("k", "", "example-family", "not a url", "Maxio:BaseUrl")]
    public void MissingOrBlankKey_FailsAndNamesTheKey(string apiKey, string subdomain, string family, string? baseUrl, string expectedKey)
    {
        var result = _validator.Validate(null, new MaxioSettings
        {
            ApiKey = apiKey,
            Subdomain = subdomain,
            ProductFamilyHandle = family,
            BaseUrl = baseUrl
        });

        Assert.True(result.Failed);
        Assert.Contains(expectedKey, result.FailureMessage);
    }

    [Fact]
    public void BaseUrl_ReplacesSubdomainRequirement()
    {
        var result = _validator.Validate(null, new MaxioSettings
        {
            ApiKey = "k",
            ProductFamilyHandle = "example-family",
            BaseUrl = "https://example-site.example.test"
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void BaseUrl_WhenSet_IsUsedAsTheBaseAddress()
    {
        var settings = MaxioTestHost.Settings();
        settings.BaseUrl = "https://billing-proxy.example.test/";

        var options = MaxioBillingServiceCollectionExtensions.CreateClientOptions(settings, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        Assert.Equal("https://billing-proxy.example.test", options.Server.Production.Us.BaseUrl);
        Assert.Equal("example-site", options.Server.Production.Us.Site);
        Assert.Equal("x", options.BasicAuth!.Password);
        Assert.False(options.Logging.LogRequestBody);
        Assert.NotNull(options.Logging.LoggerFactory);
    }
}
