using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioOptionsTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "key",
        Subdomain = "demo-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public void DerivesTheBaseAddressFromTheSubdomain()
    {
        Assert.Equal("https://demo-site.chargify.com/", Valid().ResolveBaseAddress().ToString());
    }

    [Theory]
    [InlineData("https://billing.example.com", "https://billing.example.com/")]
    [InlineData("https://billing.example.com/", "https://billing.example.com/")]
    [InlineData("https://billing.example.com/maxio", "https://billing.example.com/maxio/")]
    public void PrefersAnExplicitBaseUrl(string configured, string expected)
    {
        var options = Valid();
        options.BaseUrl = configured;

        Assert.Equal(expected, options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AcceptsAValidConfiguration()
    {
        Assert.True(new MaxioOptionsValidator().Validate(null, Valid()).Succeeded);
    }

    [Fact]
    public void RejectsAMissingApiKey()
    {
        var options = Valid();
        options.ApiKey = " ";

        var result = new MaxioOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Maxio:ApiKey", result.FailureMessage);
    }

    [Fact]
    public void RejectsAMissingProductFamilyHandle()
    {
        var options = Valid();
        options.ProductFamilyHandle = null;

        var result = new MaxioOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Maxio:ProductFamilyHandle", result.FailureMessage);
    }

    [Fact]
    public void RequiresASubdomainOnlyWhenThereIsNoBaseUrl()
    {
        var options = Valid();
        options.Subdomain = null;

        Assert.True(new MaxioOptionsValidator().Validate(null, options).Failed);

        options.BaseUrl = "https://billing.example.com";
        Assert.True(new MaxioOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void RejectsASubdomainThatIsActuallyAUrl()
    {
        var options = Valid();
        options.Subdomain = "https://demo-site.chargify.com";

        var result = new MaxioOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("bare site subdomain", result.FailureMessage);
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAnAbsoluteHttpUrl()
    {
        var options = Valid();
        options.BaseUrl = "demo-site.chargify.com";

        var result = new MaxioOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("absolute http or https URL", result.FailureMessage);
    }
}
