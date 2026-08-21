using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioAdvancedBillingClientTests
{
    [Fact]
    public async Task ListProductsForFamilyAsync_DeserializesOfficialProductPayload()
    {
        const string json = """
            [
              {
                "product": {
                  "id": 3801242,
                  "name": "Free product",
                  "handle": "zero-dollar-product",
                  "description": "",
                  "price_in_cents": 10000,
                  "interval": 1,
                  "interval_unit": "month",
                  "archived_at": null,
                  "product_family": {
                    "id": 527890,
                    "name": "Acme Projects",
                    "handle": "billing-plans"
                  }
                }
              }
            ]
            """;

        var handler = new ScriptedHandler((request) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("product_families/handle:eshop-subscribe/products.json", request.RequestUri!.ToString());
            return Json(json);
        });

        var client = CreateClient(handler);
        var products = await client.ListProductsForFamilyAsync("eshop-subscribe");

        var product = Assert.Single(products);
        Assert.Equal("zero-dollar-product", product.Handle);
        Assert.Equal("Free product", product.Name);
        Assert.Equal(10000, product.PriceInCents);
        Assert.Equal(1, product.Interval);
        Assert.Equal("month", product.IntervalUnit);
        Assert.Equal("billing-plans", product.ProductFamilyHandle);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_ReturnsNullOn404()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var customer = await client.FindCustomerByReferenceAsync("user-1");

        Assert.Null(customer);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_PostsProductHandleAndCustomerId()
    {
        const string json = """
            {
              "subscription": {
                "id": 15236915,
                "state": "active",
                "product_price_in_cents": 29900,
                "next_assessment_at": "2016-11-15T14:48:10-05:00",
                "product": {
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "price_in_cents": 29900
                }
              }
            }
            """;

        string? posted = null;
        var handler = new ScriptedHandler(async (request) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("subscriptions.json", request.RequestUri!.ToString());
            posted = await request.Content!.ReadAsStringAsync();
            return Json(json);
        });

        var client = CreateClient(handler);
        var subscription = await client.CreateSubscriptionAsync(88, "eshop-pro");

        Assert.Equal(15236915, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal("Pro Plan", subscription.ProductName);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal(new DateTimeOffset(2016, 11, 15, 14, 48, 10, TimeSpan.FromHours(-5)), subscription.NextBillingAt);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", posted);
        Assert.Contains("\"customer_id\":88", posted);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", posted);
    }

    [Fact]
    public void ReadErrorMessage_FlattensErrorsArray()
    {
        var message = MaxioAdvancedBillingClient.ReadErrorMessage(
            """{"errors":["Reference must be unique."]}""", 422);

        Assert.Equal("Reference must be unique.", message);
    }

    private static MaxioAdvancedBillingClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.chargify.com/")
        };
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioAdvancedBillingClient(http, options);
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this(request => Task.FromResult(responder(request)))
        {
        }

        public ScriptedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request);
    }
}
