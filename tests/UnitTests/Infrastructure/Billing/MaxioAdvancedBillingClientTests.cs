using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioAdvancedBillingClientTests
{
    [Fact]
    public async Task FindCustomerByReferenceAsync_ReturnsNull_On404()
    {
        var client = CreateClient((request) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("customers/lookup.json?reference=user-1", request.RequestUri!.ToString());
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"errors":["Not found"]}""", Encoding.UTF8, "application/json")
            };
        });

        var customer = await client.FindCustomerByReferenceAsync("user-1");

        Assert.Null(customer);
    }

    [Fact]
    public async Task ListProductsForFamilyAsync_UsesHandlePrefixedPath()
    {
        const string body = """
            [
              {
                "product": {
                  "id": 1,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month"
                }
              }
            ]
            """;

        var client = CreateClient((request) =>
        {
            Assert.Contains("product_families/handle:eshop-subscribe/products.json", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });

        var products = await client.ListProductsForFamilyAsync("eshop-subscribe");

        Assert.Single(products);
        Assert.Equal("eshop-pro", products[0].Handle);
        Assert.Equal(29900, products[0].PriceInCents);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_PostsProductHandleAndCustomerId()
    {
        const string body = """
            {
              "subscription": {
                "id": 55,
                "state": "active",
                "product_price_in_cents": 2900,
                "next_assessment_at": "2026-09-19T12:00:00-04:00",
                "product": { "handle": "basic-plan", "name": "Basic Plan" }
              }
            }
            """;

        string? posted = null;
        var client = CreateClient((request) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("subscriptions.json", request.RequestUri!.ToString());
            posted = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });

        var created = await client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            ProductHandle = "basic-plan",
            CustomerId = 42,
            Reference = "user-1:basic-plan"
        });

        Assert.Equal(55, created.Id);
        Assert.Equal("active", created.State);
        Assert.Contains("\"product_handle\":\"basic-plan\"", posted);
        Assert.Contains("\"customer_id\":42", posted);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", posted);
    }

    private static MaxioAdvancedBillingClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var http = new HttpClient(new StubHandler(responder))
        {
            BaseAddress = new Uri("https://cp-exp-4.chargify.com/")
        };
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-4",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioAdvancedBillingClient(http, options, NullLogger<MaxioAdvancedBillingClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
