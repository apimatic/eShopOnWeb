using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioApiClientTests
{
    private class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public Queue<Func<HttpResponseMessage>> Responses { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Responses.Dequeue()());
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (MaxioApiClient Client, StubHandler Handler) Create(MaxioOptions options)
    {
        var handler = new StubHandler();
        var client = new MaxioApiClient(new HttpClient(handler), Options.Create(options), NullLogger<MaxioApiClient>.Instance);
        return (client, handler);
    }

    private static MaxioOptions Defaults() => new()
    {
        ApiKey = "k",
        Subdomain = "acme",
        ProductFamilyHandle = "fam"
    };

    [Fact]
    public void ResolvesUsBaseAddressFromSubdomain()
    {
        Assert.Equal("https://acme.chargify.com/", Defaults().ResolveBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void ResolvesEuBaseAddressFromSubdomain()
    {
        var options = Defaults();
        options.Environment = "EU";
        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveBaseAddress().AbsoluteUri);
    }

    [Fact]
    public async Task UsesBaseUrlOverrideVerbatimAndBasicAuth()
    {
        var options = Defaults();
        options.BaseUrl = "http://localhost:5999/maxio";
        var (client, handler) = Create(options);
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, "[]"));

        await client.ListProductsAsync("fam", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.StartsWith("http://localhost:5999/maxio/product_families/handle%3Afam/products.json", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal("k:x", Encoding.ASCII.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
    }

    [Fact]
    public async Task FindCustomerReturnsNullOn404()
    {
        var (client, handler) = Create(Defaults());
        handler.Responses.Enqueue(() => Json(HttpStatusCode.NotFound, "{}"));

        Assert.Null(await client.FindCustomerByReferenceAsync("r", CancellationToken.None));
    }

    [Fact]
    public async Task MapsUnprocessableToRejectedWithMaxioMessage()
    {
        var (client, handler) = Create(Defaults());
        handler.Responses.Enqueue(() => Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"No payment method was on file\"]}"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() =>
            client.CreateSubscriptionAsync(new MaxioNewSubscription { CustomerId = 1, ProductHandle = "p" }, CancellationToken.None));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Contains("No payment method was on file", ex.Message);
    }

    [Fact]
    public async Task MapsUnauthorizedToMisconfiguredWithoutLeakingKey()
    {
        var (client, handler) = Create(Defaults());
        handler.Responses.Enqueue(() => Json(HttpStatusCode.Unauthorized, "denied"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListProductsAsync("fam", CancellationToken.None));

        Assert.Equal(BillingFailureKind.Misconfigured, ex.Kind);
        Assert.DoesNotContain("k:x", ex.Message);
    }

    [Fact]
    public async Task RetriesTransientFailuresOnGet()
    {
        var (client, handler) = Create(Defaults());
        handler.Responses.Enqueue(() => Json(HttpStatusCode.ServiceUnavailable, "{}"));
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, "[]"));

        var products = await client.ListProductsAsync("fam", CancellationToken.None);

        Assert.Empty(products);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryPost()
    {
        var (client, handler) = Create(Defaults());
        handler.Responses.Enqueue(() => Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() =>
            client.CreateSubscriptionAsync(new MaxioNewSubscription { CustomerId = 1, ProductHandle = "p" }, CancellationToken.None));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void ValidatorRequiresCoreSettings()
    {
        var result = new MaxioOptionsValidator().Validate(null, new MaxioOptions());
        Assert.True(result.Failed);
        Assert.Contains("Maxio:ApiKey", result.FailureMessage);
        Assert.Contains("Maxio:Subdomain", result.FailureMessage);
        Assert.Contains("Maxio:ProductFamilyHandle", result.FailureMessage);
    }

    [Fact]
    public void ValidatorAcceptsBaseUrlInPlaceOfSubdomain()
    {
        var options = Defaults();
        options.Subdomain = null;
        options.BaseUrl = "http://localhost:1/";
        Assert.True(new MaxioOptionsValidator().Validate(null, options).Succeeded);
    }
}
