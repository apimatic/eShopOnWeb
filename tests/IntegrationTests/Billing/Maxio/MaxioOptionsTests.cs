#nullable enable
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.Maxio;

public class MaxioOptionsTests
{
    private static MaxioOptions Options(string? baseUrl = null, string environment = MaxioEnvironments.Us) => new()
    {
        ApiKey = "key",
        Subdomain = "acme",
        ProductFamilyHandle = "family",
        BaseUrl = baseUrl,
        Environment = environment
    };

    [Fact]
    public void DerivesTheUsHostFromTheSubdomain()
    {
        Assert.Equal("https://acme.chargify.com/", Options().ResolveBaseAddress().ToString());
    }

    [Fact]
    public void DerivesTheEuHostFromTheSubdomain()
    {
        Assert.Equal("https://acme.ebilling.maxio.com/",
            Options(environment: MaxioEnvironments.Eu).ResolveBaseAddress().ToString());
    }

    [Fact]
    public void BaseUrlOverridesTheDerivedHostVerbatim()
    {
        Assert.Equal("https://billing.internal.example/",
            Options(baseUrl: "https://billing.internal.example").ResolveBaseAddress().ToString());
    }

    [Fact]
    public void BaseUrlKeepsAnyPathPrefixSoGatewayStyleAddressesSurvive()
    {
        var address = Options(baseUrl: "https://connector.api.maxio.com/api/v1/billing").ResolveBaseAddress();

        Assert.Equal("https://connector.api.maxio.com/api/v1/billing/", address.ToString());
        // Request paths are relative, so the prefix has to survive composition.
        Assert.Equal("https://connector.api.maxio.com/api/v1/billing/subscriptions.json",
            new System.Uri(address, "subscriptions.json").ToString());
    }

    [Fact]
    public void ValidationRejectsMissingCredentialsAndBadValues()
    {
        var validator = new MaxioOptionsValidator();

        Assert.True(validator.Validate(null, Options()).Succeeded);

        var missingKey = Options();
        missingKey.ApiKey = "";
        Assert.True(validator.Validate(null, missingKey).Failed);

        var badEnvironment = Options(environment: "MARS");
        Assert.True(validator.Validate(null, badEnvironment).Failed);

        var badBaseUrl = Options(baseUrl: "not-a-url");
        Assert.True(validator.Validate(null, badBaseUrl).Failed);
    }
}
