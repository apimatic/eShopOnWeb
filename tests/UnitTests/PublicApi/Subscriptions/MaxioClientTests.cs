using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

public class MaxioClientTests
{
    [Fact]
    public async Task ListProductsUsesConfiguredFamilyHandleAndMapsSpecResponse()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, """
            [
              {
                "product": {
                  "id": 42,
                  "name": "Pro",
                  "handle": "pro-plan",
                  "description": "Pro plan",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "require_credit_card": false,
                  "archived_at": null,
                  "product_family": { "handle": "plans/family" }
                }
              }
            ]
            """));
        var client = CreateClient(handler, familyHandle: "plans/family");

        var products = await client.ListProductsAsync(default);

        var product = Assert.Single(products);
        Assert.Equal(42, product.Id);
        Assert.Equal("pro-plan", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("/product_families/handle:plans%2Ffamily/products.json", handler.RequestUri!.AbsolutePath);
        Assert.Contains("page=1&per_page=200&include_archived=false", handler.RequestUri.Query);
        Assert.Equal("Basic", handler.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes("test-api-key:x")), handler.Authorization.Parameter);
    }

    [Fact]
    public async Task CreateSubscriptionUsesOpenApiRequestShapeAndMapsConfirmation()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/subscriptions.json", request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "{\"subscription\":{\"product_handle\":\"pro-plan\",\"customer_id\":7,\"reference\":\"subscription-ref\",\"payment_collection_method\":\"remittance\"}}",
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return JsonResponse(HttpStatusCode.Created, """
                {
                  "subscription": {
                    "id": 99,
                    "state": "active",
                    "product_price_in_cents": 29900,
                    "current_period_ends_at": "2026-09-21T12:00:00Z",
                    "currency": "USD",
                    "reference": "subscription-ref",
                    "customer": { "id": 7, "reference": "customer-ref" },
                    "product": {
                      "name": "Pro",
                      "handle": "pro-plan",
                      "interval": 1,
                      "interval_unit": "month",
                      "product_family": { "handle": "family" }
                    }
                  }
                }
                """);
        });
        var client = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(7, null, "pro-plan", "subscription-ref", default);

        Assert.Equal(99, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero), subscription.CurrentPeriodEndsAt);
    }

    [Fact]
    public async Task CreateSubscriptionCanCreateCustomerThroughContractualCustomerAttributes()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(
                "{\"subscription\":{\"product_handle\":\"basic-plan\",\"customer_attributes\":{\"first_name\":\"Demo\",\"last_name\":\"Customer\",\"email\":\"demo@example.com\",\"reference\":\"customer-ref\"},\"reference\":\"subscription-ref\",\"payment_collection_method\":\"remittance\"}}",
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return JsonResponse(HttpStatusCode.Created, """
                {
                  "subscription": {
                    "id": 100,
                    "state": "active",
                    "customer": { "id": 8, "reference": "customer-ref" },
                    "product": { "name": "Basic", "handle": "basic-plan", "interval": 1, "interval_unit": "month" }
                  }
                }
                """);
        });
        var client = CreateClient(handler);
        var customer = new MaxioCreateCustomer
        {
            FirstName = "Demo",
            LastName = "Customer",
            Email = "demo@example.com",
            Reference = "customer-ref"
        };

        var subscription = await client.CreateSubscriptionAsync(null, customer, "basic-plan", "subscription-ref", default);

        Assert.Equal(100, subscription.Id);
        Assert.Equal(8, subscription.Customer!.Id);
    }

    [Fact]
    public async Task CustomerLookupReturnsNullOnContractualNotFound()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var customer = await client.FindCustomerByReferenceAsync("a reference", default);

        Assert.Null(customer);
        Assert.Equal("?reference=a%20reference", handler.RequestUri!.Query);
    }

    private static MaxioClient CreateClient(RecordingHandler handler, string familyHandle = "family") =>
        new(
            new HttpClient(handler),
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-api-key",
                Subdomain = "test-site",
                ProductFamilyHandle = familyHandle,
                BaseUrl = "https://maxio.test"
            }));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(_response(request));
        }
    }
}
