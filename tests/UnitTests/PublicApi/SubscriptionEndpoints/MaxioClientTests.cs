using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.SubscriptionEndpoints;

public class MaxioClientTests
{
    [Fact]
    public async Task UsesSpecServerAuthAndFamilyProductsPath()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            [{"product":{"id":12,"name":"Basic","handle":"basic-plan","description":"Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":5,"name":"Plans","handle":"family"}}}]
            """));
        var client = CreateClient(handler);

        var products = await client.ListProductsAsync(CancellationToken.None);

        Assert.Single(products);
        Assert.Equal("https://example.test/root/product_families/handle%3Afamily/products.json?per_page=200", handler.RequestUri!.AbsoluteUri);
        Assert.Equal("Basic", handler.Authorization!.Scheme);
        Assert.Equal("test-api-key:x", Encoding.ASCII.GetString(Convert.FromBase64String(handler.Authorization.Parameter!)));
    }

    [Fact]
    public async Task CreatesSubscriptionWithOnlySpecDefinedFields()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Created, SubscriptionJson));
        var client = CreateClient(handler);

        await client.CreateSubscriptionAsync(
            new MaxioCreateSubscription("basic-plan", "customer-ref", "subscription-ref", "remittance"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://example.test/root/subscriptions.json", handler.RequestUri!.AbsoluteUri);
        Assert.Equal(
            "{\"subscription\":{\"product_handle\":\"basic-plan\",\"customer_reference\":\"customer-ref\",\"reference\":\"subscription-ref\",\"payment_collection_method\":\"remittance\"}}",
            handler.Body);
    }

    [Fact]
    public async Task TreatsSpecLookup404AsMissing()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var customer = await client.FindCustomerAsync("customer ref", CancellationToken.None);

        Assert.Null(customer);
        Assert.EndsWith("/customers/lookup.json?reference=customer%20ref", handler.RequestUri!.AbsoluteUri);
    }

    private static MaxioClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "unused",
            ProductFamilyHandle = "family",
            BaseUrl = "https://example.test/root"
        });
        return new MaxioClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private const string SubscriptionJson = """
        {"subscription":{"id":99,"state":"active","product_price_in_cents":2900,"current_period_ends_at":"2026-09-21T00:00:00Z","next_assessment_at":"2026-09-21T00:00:00Z","reference":"subscription-ref","currency":"USD","customer":{"id":7,"first_name":"Demo","last_name":"Customer","email":"demo@example.test","reference":"customer-ref"},"product":{"id":12,"name":"Basic","handle":"basic-plan","description":"Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false,"product_family":{"id":5,"name":"Plans","handle":"family"}}}}
        """;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            Authorization = request.Headers.Authorization;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response(request);
        }
    }
}
