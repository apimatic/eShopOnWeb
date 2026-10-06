using MaxioAdvancedBilling;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSettingsTests
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "offline-test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "test-family"
    };

    [Fact]
    public void Validator_AcceptsCompleteSettings()
    {
        Assert.True(new MaxioSettingsValidator().Validate(null, Valid()).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_RejectsMissingOrBlankApiKey_NamingTheKey(string? apiKey)
    {
        var settings = Valid();
        settings.ApiKey = apiKey;

        var result = new MaxioSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Maxio:ApiKey", result.FailureMessage);
    }

    [Fact]
    public void Validator_RejectsMissingProductFamilyHandle()
    {
        var settings = Valid();
        settings.ProductFamilyHandle = " ";

        Assert.Contains("Maxio:ProductFamilyHandle", new MaxioSettingsValidator().Validate(null, settings).FailureMessage);
    }

    [Fact]
    public void Validator_RequiresSubdomain_UnlessBaseUrlIsSet()
    {
        var settings = Valid();
        settings.Subdomain = null;
        Assert.Contains("Maxio:Subdomain", new MaxioSettingsValidator().Validate(null, settings).FailureMessage);

        settings.BaseUrl = "https://billing.example.test";
        Assert.True(new MaxioSettingsValidator().Validate(null, settings).Succeeded);
    }

    [Fact]
    public void Validator_RejectsRelativeBaseUrl_WithoutEchoingIt()
    {
        var settings = Valid();
        settings.BaseUrl = "not-a-url-secretish";

        var result = new MaxioSettingsValidator().Validate(null, settings);

        Assert.Contains("Maxio:BaseUrl", result.FailureMessage);
        Assert.DoesNotContain("secretish", result.FailureMessage);
    }

    [Fact]
    public void Registration_BindsTheMaxioSection_AndFailsFastWhenTheApiKeyIsBlank()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "",
                ["Maxio:Subdomain"] = "test-site",
                ["Maxio:ProductFamilyHandle"] = "test-family"
            })
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);
        using var provider = services.BuildServiceProvider();

        // The MAXIO_API_KEY fallback would legitimately fill the blank key; take it out of play for this test.
        var original = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        Environment.SetEnvironmentVariable("MAXIO_API_KEY", null);
        try
        {
            var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MaxioSettings>>().Value);
            Assert.Contains("Maxio:ApiKey", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAXIO_API_KEY", original);
        }
    }

    [Fact]
    public async Task ClientOptions_UseBaseUrlVerbatim_WhenSet()
    {
        var settings = Valid();
        settings.BaseUrl = "https://billing-proxy.example.test";

        var host = await HostCalledAsync(settings);

        Assert.Equal("billing-proxy.example.test", host);
    }

    [Fact]
    public async Task ClientOptions_DeriveTheHostFromTheSubdomain_WhenNoBaseUrl()
    {
        var host = await HostCalledAsync(Valid());

        Assert.Equal("test-site.chargify.com", host);
    }

    [Fact]
    public async Task ClientOptions_SendTheApiKeyAsBasicAuthUsername()
    {
        var maxio = new FakeMaxioHandler();
        string? authorization = null;
        maxio.Intercept = (request, _, _) =>
        {
            authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult<HttpResponseMessage?>(null);
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(maxio),
            MaxioServiceCollectionExtensions.CreateClientOptions(Valid(), NullLoggerFactory.Instance));

        await client.ProductFamilies.ListProductsForProductFamily(
            new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest { ProductFamilyId = "handle:test-family" });

        var expected = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("offline-test-key:x"));
        Assert.Equal($"Basic {expected}", authorization);
    }

    [Fact]
    public void ClientOptions_KeepRequestBodiesOutOfLogs()
    {
        var options = MaxioServiceCollectionExtensions.CreateClientOptions(Valid(), LoggerFactory.Create(_ => { }));

        Assert.False(options.Logging.LogRequestBody);
        Assert.NotNull(options.Logging.LoggerFactory);
    }

    private static async Task<string> HostCalledAsync(MaxioSettings settings)
    {
        var maxio = new FakeMaxioHandler();
        var client = new MaxioAdvancedBillingClient(new HttpClient(maxio),
            MaxioServiceCollectionExtensions.CreateClientOptions(settings, NullLoggerFactory.Instance));

        await client.ProductFamilies.ListProductsForProductFamily(
            new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest { ProductFamilyId = "handle:test-family" });

        return maxio.Requests.Single().Uri.Host;
    }
}
