using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioAdvancedBillingClientTests
{
    [Fact]
    public async Task ListProductsForFamily_DeserializesPlans()
    {
        var json = """
            [
              {
                "product": {
                  "id": 1,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "description": "Pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "product_family": { "id": 9, "name": "eShop", "handle": "eshop-subscribe" }
                }
              }
            ]
            """;

        var client = CreateClient((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("product_families/handle:eshop-subscribe/products.json", request.RequestUri!.ToString());
            return Json(json);
        });

        var plans = await client.ListProductsForFamilyAsync("eshop-subscribe");

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("eshop-subscribe", plan.ProductFamilyHandle);
    }

    [Fact]
    public async Task CreateSubscription_PostsHandleCustomerAndUniquenessToken()
    {
        var json = """
            {
              "subscription": {
                "id": 555,
                "state": "active",
                "product_price_in_cents": 2900,
                "next_assessment_at": "2026-09-21T00:00:00Z",
                "reference": "eshop:user-1:basic-plan",
                "product": {
                  "name": "Basic Plan",
                  "handle": "basic-plan",
                  "price_in_cents": 2900,
                  "interval": 1,
                  "interval_unit": "month",
                  "product_family": { "handle": "eshop-subscribe" }
                }
              }
            }
            """;

        string? posted = null;
        var client = CreateClient(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("subscriptions.json", request.RequestUri!.ToString());
            posted = await request.Content!.ReadAsStringAsync();
            return await Json(json, HttpStatusCode.Created);
        });

        var created = await client.CreateSubscriptionAsync(new CreateBillingSubscription(
            "basic-plan", 42, "eshop:user-1:basic-plan", "token-abc"));

        Assert.Equal(555, created.Id);
        Assert.Equal("active", created.State);
        Assert.Equal(29.00m, created.Price);
        Assert.Equal("basic-plan", created.ProductHandle);
        Assert.NotNull(created.NextBillingDate);
        Assert.Contains("\"product_handle\":\"basic-plan\"", posted);
        Assert.Contains("\"customer_id\":42", posted);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", posted);
        Assert.Contains("\"uniqueness_token\":\"token-abc\"", posted);
    }

    [Fact]
    public async Task FindCustomerByReference_ReturnsNullOn404()
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var customer = await client.FindCustomerByReferenceAsync("missing");

        Assert.Null(customer);
    }

    private static MaxioAdvancedBillingClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var http = new HttpClient(new StubHandler(handler))
        {
            BaseAddress = new Uri("https://cp-exp-1.chargify.com/")
        };
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-1",
            ProductFamilyHandle = "eshop-subscribe"
        };
        return new MaxioAdvancedBillingClient(http, Substitute.For<ILogger<MaxioAdvancedBillingClient>>(), settings);
    }

    private static Task<HttpResponseMessage> Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _callback;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        {
            _callback = callback;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _callback(request, cancellationToken);
    }
}
