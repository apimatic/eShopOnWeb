using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly BillingBuyer Buyer = new("user-1", "demouser@microsoft.com", "Demo", "User");

    [Fact]
    public async Task ListPlansAsync_MapsProductsFromFamily()
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
                  "require_credit_card": false
                }
              }
            ]
            """;
        var (service, handler) = CreateService((_, _) => Json(HttpStatusCode.OK, json));

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.False(plan.RequiresCreditCard);
        Assert.Contains("product_families", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("eshop-subscribe", Uri.UnescapeDataString(handler.LastRequest.RequestUri.AbsolutePath));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerThenSubscription_WhenNewBuyer()
    {
        var (service, handler) = CreateService((request, count) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("customers") && path.Contains("lookup") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.NotFound, """{"errors":"Not Found"}""");
            }

            if (path.Contains("customers") && !path.Contains("subscription") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Created, """
                    { "customer": { "id": 42, "reference": "user-1", "email": "demouser@microsoft.com", "first_name": "Demo", "last_name": "User" } }
                    """);
            }

            if (path.Contains("subscriptions") && path.Contains("lookup") && request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (path.Contains("customers") && path.Contains("subscriptions") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (path.Contains("subscriptions") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "reference": "user-1:eshop-pro",
                        "current_period_ends_at": "2026-09-19T00:00:00Z",
                        "next_assessment_at": "2026-09-19T00:00:00Z",
                        "product": { "handle": "eshop-pro", "name": "Pro Plan" }
                      }
                    }
                    """);
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await service.SubscribeAsync(Buyer, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("customers"));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WhenAlreadyEnrolled()
    {
        var existing = """
            {
              "subscription": {
                "id": 77,
                "state": "active",
                "product_price_in_cents": 29900,
                "reference": "user-1:eshop-pro",
                "product": { "handle": "eshop-pro", "name": "Pro Plan" }
              }
            }
            """;
        var createPosts = 0;
        var (service, _) = CreateService((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("customers") && path.Contains("lookup"))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 42, "reference": "user-1", "email": "demouser@microsoft.com", "first_name": "Demo", "last_name": "User" } }
                    """);
            }

            if (path.Contains("subscriptions") && path.Contains("lookup"))
            {
                return Json(HttpStatusCode.OK, existing);
            }

            if (path.Contains("subscriptions") && request.Method == HttpMethod.Post)
            {
                createPosts++;
                return Json(HttpStatusCode.Created, existing);
            }

            return Json(HttpStatusCode.OK, "[]");
        });

        var first = await service.SubscribeAsync(Buyer, "eshop-pro");
        var second = await service.SubscribeAsync(Buyer, "eshop-pro");

        Assert.Equal(77, first.Id);
        Assert.Equal(77, second.Id);
        Assert.Equal(0, createPosts);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmpty_WhenCustomerMissing()
    {
        var (service, _) = CreateService((_, _) => Json(HttpStatusCode.NotFound, """{"errors":"Not Found"}"""));

        var result = await service.ListMySubscriptionsAsync("unknown-user");

        Assert.Empty(result);
    }

    private static (MaxioSubscriptionBillingService Service, StubHandler Handler) CreateService(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://cp-exp-1.chargify.com")
        }, new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings
        {
            ProductFamilyHandle = "eshop-subscribe",
            Subdomain = "cp-exp-1"
        });
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return (new MaxioSubscriptionBillingService(client, settings, logger), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        public StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request, Requests.Count));
        }
    }
}
