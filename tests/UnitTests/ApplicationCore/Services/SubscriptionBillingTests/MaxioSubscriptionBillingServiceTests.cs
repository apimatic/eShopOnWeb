using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingTests;

public class MaxioSubscriptionBillingServiceTests
{
    private const string UserId = "demouser@microsoft.com";
    private const string ProHandle = "eshop-pro";

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
        var service = CreateService((_, _) => Json(HttpStatusCode.OK, json));

        var plans = await service.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal(ProHandle, plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.False(plan.RequireCreditCard);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerMissing()
    {
        var service = CreateService((_, _) => Json(HttpStatusCode.NotFound, """{"errors":"not found"}"""));

        var result = await service.ListMySubscriptionsAsync(UserId, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscriptionWithoutCreate()
    {
        var handler = new StubHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("customers/lookup", StringComparison.OrdinalIgnoreCase)
                || (path.Contains("/customers", StringComparison.OrdinalIgnoreCase) && request.RequestUri.Query.Contains("reference", StringComparison.OrdinalIgnoreCase)))
            {
                return Json(HttpStatusCode.OK, CustomerJson());
            }

            if (path.Contains("/customers/", StringComparison.OrdinalIgnoreCase) && path.Contains("subscription", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, "[" + SubscriptionJson() + "]");
            }

            if (path.Contains("subscriptions/lookup", StringComparison.OrdinalIgnoreCase)
                || (path.Contains("subscription", StringComparison.OrdinalIgnoreCase) && request.RequestUri.Query.Contains("reference", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Get))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/products", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, ProductJson());
            }

            if (HttpMethod.Post.Equals(request.Method) && path.Contains("subscription", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("CreateSubscription should not run when a live subscription exists. Path=" + request.RequestUri);
            }

            return Json(HttpStatusCode.OK, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserId, ProHandle, CancellationToken.None);

        Assert.Equal(99, result.Id);
        Assert.Equal(ProHandle, result.ProductHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesWhenNoneExist()
    {
        var handler = new StubHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("customers/lookup", StringComparison.OrdinalIgnoreCase)
                || (path.Contains("/customers", StringComparison.OrdinalIgnoreCase) && request.RequestUri.Query.Contains("reference", StringComparison.OrdinalIgnoreCase)))
            {
                return Json(HttpStatusCode.OK, CustomerJson());
            }

            if (path.Contains("/customers/", StringComparison.OrdinalIgnoreCase) && path.Contains("subscription", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (path.Contains("subscriptions/lookup", StringComparison.OrdinalIgnoreCase)
                || (path.Contains("subscription", StringComparison.OrdinalIgnoreCase) && request.RequestUri.Query.Contains("reference", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Get))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/products", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, ProductJson());
            }

            if (HttpMethod.Post.Equals(request.Method) && path.Contains("subscription", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.Created, SubscriptionJson());
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserId, ProHandle, CancellationToken.None);

        Assert.Equal(99, result.Id);
        Assert.Equal(ProHandle, result.ProductHandle);
        Assert.NotNull(result.NextBillingAt);
        Assert.Contains(handler.Requests, r => HttpMethod.Post.Equals(r.Method) && r.RequestUri!.AbsolutePath.Contains("subscription", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SubscribeAsync_RequiresProductHandle()
    {
        var service = CreateService((_, _) => Json(HttpStatusCode.OK, "{}"));

        var ex = await Assert.ThrowsAsync<BillingException>(() =>
            service.SubscribeAsync(UserId, "  ", CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void WriteOnceGate_RefusesSecondPost()
    {
        MaxioWriteGate.BeginWrite();
        MaxioWriteGate.CountOrReject(HttpMethod.Post);
        Assert.Throws<MaxioDuplicateWriteException>(() => MaxioWriteGate.CountOrReject(HttpMethod.Post));
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-1",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test") },
            MaxioServiceCollectionExtensions.BuildClientOptions(options.Value, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        return new MaxioSubscriptionBillingService(
            client,
            new PassThroughBudget(),
            Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>(),
            options);
    }

    private static MaxioSubscriptionBillingService CreateService(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        => CreateService(new StubHandler(responder));

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string CustomerJson() =>
        """
        {
          "customer": {
            "id": 42,
            "first_name": "demo",
            "last_name": "user",
            "email": "demouser@microsoft.com",
            "reference": "demouser@microsoft.com"
          }
        }
        """;

    private static string ProductJson() =>
        """
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
        """;

    private static string SubscriptionJson() =>
        """
        {
          "subscription": {
            "id": 99,
            "state": "active",
            "product_price_in_cents": 29900,
            "next_assessment_at": "2026-09-19T00:00:00Z",
            "current_period_ends_at": "2026-09-19T00:00:00Z",
            "product": {
              "id": 1,
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "price_in_cents": 29900
            }
          }
        }
        """;

    private sealed class PassThroughBudget : IMaxioCallBudget
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
            => call(cancellationToken);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request, cancellationToken));
        }
    }
}
