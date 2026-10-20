using System.Net;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

public class MaxioConfigurationTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "test-placeholder-not-a-key",
        Subdomain = "test-placeholder",
        ProductFamilyHandle = "eshop-subscribe",
    };

    [Fact]
    public void Validator_accepts_complete_settings()
    {
        Assert.True(new MaxioOptionsValidator().Validate(null, Valid()).Succeeded);
    }

    [Theory]
    [InlineData("ApiKey", "Maxio:ApiKey")]
    [InlineData("ProductFamilyHandle", "Maxio:ProductFamilyHandle")]
    [InlineData("Subdomain", "Maxio:Subdomain")]
    public void Validator_fails_naming_the_blank_key(string property, string expectedKey)
    {
        var options = Valid();
        typeof(MaxioOptions).GetProperty(property)!.SetValue(options, "   ");

        var result = new MaxioOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(expectedKey, result.FailureMessage);
    }

    [Fact]
    public void Validator_accepts_BaseUrl_instead_of_Subdomain()
    {
        var options = Valid();
        options.Subdomain = null;
        options.BaseUrl = "https://maxio.test";

        Assert.True(new MaxioOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Validator_rejects_a_BaseUrl_that_is_not_absolute_http()
    {
        var options = Valid();
        options.BaseUrl = "not a url";

        var result = new MaxioOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Maxio:BaseUrl", result.FailureMessage);
    }

    [Fact]
    public void Validator_never_echoes_a_value()
    {
        var options = Valid();
        options.ApiKey = "VALUE-THAT-MUST-NOT-APPEAR";
        options.ProductFamilyHandle = null;

        var result = new MaxioOptionsValidator().Validate(null, options);

        Assert.DoesNotContain("VALUE-THAT-MUST-NOT-APPEAR", result.FailureMessage);
    }

    [Fact]
    public async Task Host_refuses_to_start_when_settings_are_missing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMaxioSubscriptionBilling(configuration);
            })
            .Build();

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains("Maxio:ApiKey", ex.Message);
    }

    [Fact]
    public async Task BaseUrl_override_is_used_verbatim_with_basic_auth()
    {
        var fake = new FakeMaxio();
        var harness = new Harness(fake.CreateHandler());

        await harness.NewCatalog().GetPlansAsync(CancellationToken.None);

        var request = Assert.Single(harness.Stub.Requests);
        Assert.Equal("maxio.test", request.Uri.Host);
        Assert.Equal("https", request.Uri.Scheme);
        Assert.StartsWith("Basic ", request.Authorization);
    }

    [Fact]
    public async Task Subdomain_derives_the_host_when_no_BaseUrl_is_set()
    {
        var fake = new FakeMaxio();
        var harness = new Harness(fake.CreateHandler(), new MaxioOptions
        {
            ApiKey = "test-placeholder-not-a-key",
            Subdomain = "my-site",
            ProductFamilyHandle = Harness.FamilyHandle,
        });

        await harness.NewCatalog().GetPlansAsync(CancellationToken.None);

        Assert.Equal("my-site.chargify.com", Assert.Single(harness.Stub.Requests).Uri.Host);
    }

    [Fact]
    public async Task A_provider_401_on_a_read_is_not_reported_as_the_callers_fault()
    {
        var fake = new FakeMaxio { Products = _ => Task.FromResult(StubHandler.Empty(HttpStatusCode.Unauthorized)) };
        var harness = new Harness(fake.CreateHandler());

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => harness.NewCatalog().GetPlansAsync(CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
    }
}
