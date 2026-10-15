using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using Microsoft.eShopWeb.Infrastructure.Maxio.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Tests;

public class ConfigurationTests
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "secret-value-123",
        Subdomain = "acme",
        ProductFamilyHandle = "family",
    };

    [Fact]
    public void CompleteSettingsAreValid()
    {
        Assert.True(new MaxioSettingsValidator().Validate(null, Valid()).Succeeded);
    }

    [Fact]
    public void MissingApiKeyFailsNamingTheKey()
    {
        var settings = Valid();
        settings.ApiKey = " ";

        var result = new MaxioSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Maxio:ApiKey", result.FailureMessage);
    }

    [Fact]
    public void FailureMessagesNeverEchoValues()
    {
        var settings = Valid();
        settings.ProductFamilyHandle = null;
        settings.Environment = "MARS";

        var result = new MaxioSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.DoesNotContain("secret-value-123", result.FailureMessage);
        Assert.DoesNotContain("MARS", result.FailureMessage);
    }

    [Fact]
    public void SubdomainIsOptionalWhenBaseUrlIsSet()
    {
        var settings = Valid();
        settings.Subdomain = null;
        settings.BaseUrl = "https://billing-proxy.internal";

        Assert.True(new MaxioSettingsValidator().Validate(null, settings).Succeeded);
    }

    [Fact]
    public void MissingSubdomainAndBaseUrlFails()
    {
        var settings = Valid();
        settings.Subdomain = null;

        Assert.Contains("Maxio:Subdomain", new MaxioSettingsValidator().Validate(null, settings).FailureMessage);
    }

    [Fact]
    public void UnknownCollectionMethodFails()
    {
        var settings = Valid();
        settings.PaymentCollectionMethod = "barter";

        Assert.Contains("Maxio:PaymentCollectionMethod", new MaxioSettingsValidator().Validate(null, settings).FailureMessage);
    }

    [Theory]
    [InlineData(null, null, "https://acme.chargify.com/")]
    [InlineData("EU", null, "https://acme.ebilling.maxio.com/")]
    [InlineData(null, "https://billing-proxy.internal", "https://billing-proxy.internal/")]
    public async Task BaseAddressComesFromSubdomainOrVerbatimBaseUrl(string? environment, string? baseUrl, string expectedPrefix)
    {
        var settings = Valid();
        settings.Environment = environment ?? "US";
        settings.BaseUrl = baseUrl;
        var maxio = FakeMaxio.WithDefaultCatalog();
        var client = new MaxioAdvancedBillingClient(new HttpClient(maxio),
            MaxioServiceCollectionExtensions.CreateClientOptions(settings, NullLoggerFactory.Instance, TimeProvider.System));

        await client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest { ProductFamilyId = "handle:family" });

        var request = Assert.Single(maxio.Calls);
        Assert.StartsWith("GET /product_families/", request);
        Assert.Equal(expectedPrefix, LastRequestBase(maxio));
    }

    [Fact]
    public void EnvironmentVariableAliasesAreTheLowestPrecedenceSource()
    {
        Environment.SetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN", "from-alias");
        Environment.SetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY", "alias-family");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Maxio:Subdomain"] = "from-config" })
                .AddMaxioEnvironmentAliases()
                .Build();

            Assert.Equal("from-config", configuration["Maxio:Subdomain"]);
            Assert.Equal("alias-family", configuration["Maxio:ProductFamilyHandle"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN", null);
            Environment.SetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY", null);
        }
    }

    private static string LastRequestBase(FakeMaxio maxio) => maxio.LastRequestUri!.GetLeftPart(UriPartial.Authority) + "/";
}
