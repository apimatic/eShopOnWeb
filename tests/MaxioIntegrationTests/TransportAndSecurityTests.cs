using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// How the integration behaves on the wire: where it points, what it presents, what it retries, and
/// — above all — what it never replays.
/// </summary>
public class TransportAndSecurityTests
{
    [Fact]
    public async Task TheConfiguredBaseUrlIsWhereTheTrafficGoes()
    {
        var provider = new FakeBillingProvider().WithCatalog();
        var (client, _) = BillingClientFixture.Create(provider);

        await client.ListPlansAsync();

        Assert.All(provider.Requests, request =>
            Assert.StartsWith("http://localhost:18080/", request.Uri.AbsoluteUri, StringComparison.Ordinal));
        Assert.Equal("http://localhost:18080/product_families.json",
            provider.Requests[0].Uri.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task RetargetingIsAConfigurationChangeAndNothingElse()
    {
        var settings = BillingClientFixture.Settings();
        settings.BaseUrl = "http://127.0.0.1:19999/maxio";

        var provider = new FakeBillingProvider().WithCatalog();
        var (client, _) = BillingClientFixture.Create(provider, settings);

        await client.ListPlansAsync();

        Assert.StartsWith("http://127.0.0.1:19999/maxio/", provider.Requests[0].Uri.AbsoluteUri,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoBaseUrlTheSubdomainDecidesTheHost()
    {
        var settings = BillingClientFixture.Settings();
        settings.BaseUrl = null;
        settings.Subdomain = "cp-exp-2";

        var provider = new FakeBillingProvider().WithCatalog();
        var (client, _) = BillingClientFixture.Create(provider, settings);

        await client.ListPlansAsync();

        Assert.Equal("cp-exp-2.chargify.com", provider.Requests[0].Uri.Host);
    }

    [Fact]
    public async Task TheApiKeyIsPresentedAsBasicCredentialsAndNeverInTheUrl()
    {
        var provider = new FakeBillingProvider().WithCatalog();
        var (client, _) = BillingClientFixture.Create(provider);

        await client.ListPlansAsync();

        var request = provider.Requests[0];
        Assert.Equal("Basic", request.AuthScheme);

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(request.AuthParameter!));
        Assert.Equal($"{BillingClientFixture.ApiKey}:x", decoded);

        Assert.DoesNotContain(BillingClientFixture.ApiKey, request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheApiKeyNeverReachesTheLogEvenWhenTheProviderEchoesItBack()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions.json",
                $$"""{"errors":["rejected for key {{BillingClientFixture.ApiKey}}"]}""",
                HttpStatusCode.UnprocessableEntity);
        var (client, logger) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingRequestRejectedException>(
            () => client.CreateSubscriptionAsync(88001, "eshop-pro"));

        Assert.DoesNotContain(BillingClientFixture.ApiKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(BillingClientFixture.ApiKey, string.Join("\n", exception.ProviderErrors),
            StringComparison.Ordinal);
        Assert.DoesNotContain(BillingClientFixture.ApiKey, logger.AllText, StringComparison.Ordinal);
        Assert.Contains("[redacted]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailingReadIsRetriedUpToTheConfiguredLimitAndThenSucceeds()
    {
        var settings = BillingClientFixture.Settings();
        settings.MaxRetries = 2;
        settings.TimeoutSeconds = 30;

        var provider = new FakeBillingProvider()
            .RespondInSequence(HttpMethod.Get, "/product_families.json",
                StubResponse.Status(HttpStatusCode.ServiceUnavailable),
                StubResponse.Status(HttpStatusCode.ServiceUnavailable),
                StubResponse.Ok(BillingPayloads.ProductFamilies))
            .Respond(HttpMethod.Get, "/product_families/3023074/products.json", BillingPayloads.ProductsForFamily);
        var (client, _) = BillingClientFixture.Create(provider, settings);

        var plans = await client.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(3, provider.CallsTo("/product_families.json"));
    }

    [Fact]
    public async Task AFailingWriteIsNeverReplayedSoNothingCanBeBilledTwice()
    {
        var settings = BillingClientFixture.Settings();
        settings.MaxRetries = 3;
        settings.TimeoutSeconds = 30;

        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/usages.json", """{"errors":["upstream temporarily unavailable"]}""",
                HttpStatusCode.ServiceUnavailable);
        var (client, _) = BillingClientFixture.Create(provider, settings);

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => client.RecordUsageAsync(15236915, 1m, "one order"));

        Assert.Equal(1, provider.CallsTo("/usages.json"));
    }

    [Fact]
    public async Task ASubscriptionIsNeverCreatedTwiceByARetry()
    {
        var settings = BillingClientFixture.Settings();
        settings.MaxRetries = 3;
        settings.TimeoutSeconds = 30;

        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions.json", "{}", HttpStatusCode.GatewayTimeout);
        var (client, _) = BillingClientFixture.Create(provider, settings);

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => client.CreateSubscriptionAsync(88001, "eshop-pro"));

        Assert.Equal(1, provider.CallsTo("/subscriptions.json"));
    }

    [Fact]
    public async Task EveryOutboundCallCarriesCredentialsNotJustTheFirst()
    {
        var provider = new FakeBillingProvider().WithCatalog();
        var (client, _) = BillingClientFixture.Create(provider);

        await client.ListPlansAsync();

        Assert.Equal(2, provider.Requests.Count);
        Assert.All(provider.Requests, request => Assert.Equal("Basic", request.AuthScheme));
    }
}
