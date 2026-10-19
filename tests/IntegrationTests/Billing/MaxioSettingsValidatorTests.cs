#nullable enable
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSettingsValidatorTests
{
    private readonly MaxioSettingsValidator _validator = new();

    [Fact]
    public void AcceptsSubdomainConfiguration()
    {
        var result = _validator.Validate(null, new MaxioSettings { ApiKey = "k", Subdomain = "site", ProductFamilyHandle = "family" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AcceptsBaseUrlInsteadOfSubdomain()
    {
        var result = _validator.Validate(null, new MaxioSettings { ApiKey = "k", BaseUrl = "https://billing.example.test", ProductFamilyHandle = "family" });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null, "site", "family", null, "Maxio:ApiKey")]
    [InlineData("  ", "site", "family", null, "Maxio:ApiKey")]
    [InlineData("k", null, "family", null, "Maxio:Subdomain")]
    [InlineData("k", "site", "", null, "Maxio:ProductFamilyHandle")]
    [InlineData("k", null, "family", "not-a-url", "Maxio:BaseUrl")]
    public void RejectsMissingOrBlankSettingsNamingTheKey(string? apiKey, string? subdomain, string? family, string? baseUrl, string expectedKey)
    {
        var result = _validator.Validate(null, new MaxioSettings { ApiKey = apiKey, Subdomain = subdomain, ProductFamilyHandle = family, BaseUrl = baseUrl });

        Assert.True(result.Failed);
        Assert.Contains(expectedKey, result.FailureMessage);
    }
}
