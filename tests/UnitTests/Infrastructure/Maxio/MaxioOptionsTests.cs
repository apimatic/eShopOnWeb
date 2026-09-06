using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public void DerivesTheUsServerFromTheSubdomain()
    {
        Assert.Equal("https://acme.chargify.com/", Valid().ResolveBaseAddress().ToString());
    }

    [Fact]
    public void DerivesTheEuServerFromTheSubdomain()
    {
        var options = Valid();
        options.Environment = MaxioOptions.EuEnvironment;

        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void BaseUrlOverridesTheDerivedServer()
    {
        var options = Valid();
        options.BaseUrl = "https://billing.internal.example.com/maxio";

        Assert.Equal("https://billing.internal.example.com/maxio/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void BaseUrlRemovesTheNeedForASubdomain()
    {
        var options = Valid();
        options.Subdomain = null;
        options.BaseUrl = "https://billing.internal.example.com";

        Assert.True(options.IsConfigured);
        Assert.Equal("https://billing.internal.example.com/", options.ResolveBaseAddress().ToString());
    }

    [Theory]
    [InlineData(null, "acme", "family", "ApiKey")]
    [InlineData("key", null, "family", "Subdomain")]
    [InlineData("key", "acme", null, "ProductFamilyHandle")]
    public void ReportsEveryMissingSetting(string? apiKey, string? subdomain, string? family, string expectedMention)
    {
        var options = new MaxioOptions { ApiKey = apiKey, Subdomain = subdomain, ProductFamilyHandle = family };

        Assert.False(options.IsConfigured);
        Assert.Contains(options.Validate(), e => e.Contains(expectedMention, StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsARelativeBaseUrl()
    {
        var options = Valid();
        options.BaseUrl = "not-a-url";

        Assert.Contains(options.Validate(), e => e.Contains("absolute URL", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildsTheSpecifiedBasicAuthParameter()
    {
        // The specification's BasicAuth scheme: API key as the user name, fixed password "x".
        var parameter = MaxioApiClient.BuildBasicAuthParameter("abc123");

        Assert.Equal("abc123:x", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parameter)));
    }
}
