using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingGatewayTests
{
    [Fact]
    public async Task ListProductsForFamilyUsesHandlePrefixedPath()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler((request, _) =>
        {
            captured = request;
            var json = """
                [
                  {
                    "product": {
                      "id": 10,
                      "handle": "eshop-pro",
                      "name": "Pro Plan",
                      "description": "Full access",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month",
                      "archived_at": null
                    }
                  }
                ]
                """;
            return Task.FromResult(Json(json));
        });

        var gateway = CreateGateway(handler);
        var products = await gateway.ListProductsForFamilyAsync();

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal(
            "https://example.chargify.com/product_families/handle%3Aeshop-subscribe/products.json?per_page=200",
            captured.RequestUri!.ToString());
        Assert.Single(products);
        Assert.Equal("eshop-pro", products[0].Handle);
        Assert.Equal(29900, products[0].PriceInCents);
    }

    [Fact]
    public async Task FindCustomerByReferenceReturnsNullOn404()
    {
        var handler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var gateway = CreateGateway(handler);

        var customer = await gateway.FindCustomerByReferenceAsync("user-1");

        Assert.Null(customer);
    }

    [Fact]
    public async Task CreateCustomerPostsSpecShape()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHandler(async (request, _) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync();
            return Json("""
                {
                  "customer": {
                    "id": 55,
                    "first_name": "demouser",
                    "last_name": "eShopOnWeb",
                    "email": "demouser@microsoft.com",
                    "reference": "user-1"
                  }
                }
                """);
        });

        var gateway = CreateGateway(handler);
        var shopper = new Shopper("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
        var customer = await gateway.CreateCustomerAsync(shopper, "user-1");

        Assert.Equal(55, customer.Id);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://example.chargify.com/customers.json", captured.RequestUri!.ToString());
        Assert.Contains("\"first_name\":\"demouser\"", body);
        Assert.Contains("\"last_name\":\"eShopOnWeb\"", body);
        Assert.Contains("\"reference\":\"user-1\"", body);
        Assert.StartsWith("Basic ", captured.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task CreateSubscriptionPostsCustomerIdAndProductHandle()
    {
        string? body = null;
        var handler = new StubHandler(async (request, _) =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return Json("""
                {
                  "subscription": {
                    "id": 88,
                    "state": "active",
                    "reference": "user-1:eshop-pro",
                    "product_price_in_cents": 29900,
                    "next_assessment_at": "2026-09-19T00:00:00-04:00",
                    "current_period_ends_at": "2026-09-19T00:00:00-04:00",
                    "created_at": "2026-08-19T00:00:00-04:00",
                    "product": {
                      "handle": "eshop-pro",
                      "name": "Pro Plan"
                    }
                  }
                }
                """, HttpStatusCode.Created);
        });

        var gateway = CreateGateway(handler);
        var subscription = await gateway.CreateSubscriptionAsync(55, "eshop-pro", "user-1:eshop-pro");

        Assert.Equal(88, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":55", body);
        Assert.Contains("\"reference\":\"user-1:eshop-pro\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    private static MaxioBillingGateway CreateGateway(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.chargify.com/")
        };
        MaxioBillingGateway.ConfigureHttpClient(client, options.Value);
        return new MaxioBillingGateway(client, options, NullLogger<MaxioBillingGateway>.Instance);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request, cancellationToken);
    }
}
