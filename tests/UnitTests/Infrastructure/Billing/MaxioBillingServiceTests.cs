using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingServiceTests
{
    [Fact]
    public void SplitDisplayName_UsesEmailLocalPart()
    {
        var (first, last) = MaxioBillingService.SplitDisplayName("demouser@microsoft.com", "demouser@microsoft.com");

        Assert.Equal("Demouser", first);
        Assert.Equal("Customer", last);
    }

    [Fact]
    public void CustomerReference_IsStableForUser()
    {
        Assert.Equal("eshoponweb:user-1", MaxioBillingService.CustomerReference("user-1"));
        Assert.Equal("eshoponweb:user-1:eshop-pro", MaxioBillingService.SubscriptionReference("user-1", "eshop-pro"));
    }

    [Fact]
    public async Task ListPlansAsync_ReadsProductsForConfiguredFamily()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("product_families/handle:eshop-subscribe/products.json", request.RequestUri!.ToString());
            return Json(HttpStatusCode.OK, """
                [
                  {
                    "product": {
                      "id": 1,
                      "name": "Pro Plan",
                      "handle": "eshop-pro",
                      "description": "Pro",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month"
                    }
                  }
                ]
                """);
        });

        var service = CreateService(handler);
        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenSubscriptionAlreadyExists()
    {
        var handler = new StubHandler((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;

            if (path.Contains("product_families/"))
            {
                return Json(HttpStatusCode.OK, """
                    [{ "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "description": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } }]
                    """);
            }

            if (path.Contains("customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 42, "email": "demouser@microsoft.com", "reference": "eshoponweb:user-1" } }""");
            }

            if (path.Contains("subscriptions/lookup.json"))
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "reference": "eshoponweb:user-1:eshop-pro",
                        "product_price_in_cents": 29900,
                        "current_period_ends_at": "2026-09-20T00:00:00Z",
                        "next_assessment_at": "2026-09-20T00:00:00Z",
                        "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "interval": 1, "interval_unit": "month", "price_in_cents": 29900 }
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.InternalServerError, """{ "errors": ["should not create"] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "errors": ["not found"] }""");
        });

        var service = CreateService(handler);
        var shopper = new ShopperIdentity("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

        var first = await service.SubscribeAsync(shopper, "eshop-pro");
        var second = await service.SubscribeAsync(shopper, "eshop-pro");

        Assert.Equal(99, first.Id);
        Assert.Equal(99, second.Id);
        Assert.Equal("active", first.State);
        Assert.Equal("eshop-pro", first.ProductHandle);
        Assert.Equal(299.00m, first.Price);
        Assert.NotNull(first.NextBillingDate);
        Assert.Equal(0, handler.PostCount);
    }

    private static MaxioBillingService CreateService(StubHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://cp-exp-3.chargify.com/") };
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-3",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var logger = Substitute.For<IAppLogger<MaxioBillingService>>();
        return new MaxioBillingService(client, options, logger);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

        public int PostCount { get; private set; }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                PostCount++;
            }

            return Task.FromResult(_responder(request, cancellationToken));
        }
    }
}
