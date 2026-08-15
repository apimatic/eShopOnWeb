using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using IAppLoggerOfService = Microsoft.eShopWeb.ApplicationCore.Interfaces.IAppLogger<Microsoft.eShopWeb.Infrastructure.Maxio.MaxioBillingService>;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Unit tests for <see cref="MaxioBillingService"/> using the SDK's HttpClient seam (a fake handler),
/// so no real Maxio calls happen. Covers mapping, idempotency signalling, and failure translation.
/// </summary>
public class MaxioBillingServiceTests
{
    private const string FamilyHandle = "test-family";

    private const string FamiliesJson =
        """[ { "product_family": { "id": 100, "handle": "test-family", "name": "Test Family" } } ]""";

    private const string ProductsJson =
        """
        [
          { "product": { "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } },
          { "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } }
        ]
        """;

    private const string SubscriptionsJson =
        """
        [
          { "subscription": {
              "id": 900, "state": "active",
              "next_assessment_at": "2026-09-16T00:00:00Z",
              "current_period_ends_at": "2026-09-16T00:00:00Z",
              "product_price_in_cents": 29900,
              "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }
        ]
        """;

    private static MaxioBillingService BuildService(Func<HttpRequestMessage, HttpResponseMessage> responder, out RoutingStubHttpMessageHandler handler)
    {
        handler = new RoutingStubHttpMessageHandler(responder);

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "x" },
            Environment = ServerEnvironment.Us,
            // Keep tests fast and deterministic — don't wait through the default retry backoff.
            Retry = RetryOptions.Default() with { MaxRetries = 1, Delay = TimeSpan.Zero }
        };
        options.Server.Production.Us.Site = "test";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);

        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test",
            Subdomain = "test",
            ProductFamilyHandle = FamilyHandle,
            Currency = "USD",
            PaymentCollectionMethod = "remittance"
        });

        return new MaxioBillingService(client, settings, Substitute.For<IAppLoggerOfService>());
    }

    /// <summary>Routes the multi-call flows by HTTP method + path so one responder serves a whole scenario.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> DefaultResponder(
        string? customerJson = null, string? subscriptionsJson = null, string? createSubscriptionJson = null)
    {
        return request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var isGet = request.Method == HttpMethod.Get;

            if (isGet && path.Contains("/products"))
            {
                return RoutingStubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            }

            if (isGet && path.Contains("product_families"))
            {
                return RoutingStubHttpMessageHandler.Json(HttpStatusCode.OK, FamiliesJson);
            }

            if (isGet && path.Contains("subscriptions"))
            {
                return RoutingStubHttpMessageHandler.Json(HttpStatusCode.OK, subscriptionsJson ?? "[]");
            }

            if (isGet && path.Contains("customers"))
            {
                // customer lookup-by-reference
                return customerJson is null
                    ? RoutingStubHttpMessageHandler.Json(HttpStatusCode.NotFound, """{ "errors": ["not found"] }""")
                    : RoutingStubHttpMessageHandler.Json(HttpStatusCode.OK, customerJson);
            }

            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                return RoutingStubHttpMessageHandler.Json(HttpStatusCode.Created, createSubscriptionJson ?? "{}");
            }

            if (request.Method == HttpMethod.Post && path.Contains("customers"))
            {
                return RoutingStubHttpMessageHandler.Json(HttpStatusCode.Created, customerJson ?? """{ "customer": { "id": 555 } }""");
            }

            return RoutingStubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        };
    }

    [Fact]
    public async Task GetPlansAsync_MapsProductsToPlans()
    {
        var service = BuildService(DefaultResponder(), out _);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("USD", pro.Currency);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_ReturnsEmpty_WhenCustomerNotFound()
    {
        // customerJson null => the customer lookup returns 404, which must map to "no subscriptions",
        // never leak as an error.
        var service = BuildService(DefaultResponder(customerJson: null), out _);

        var subscriptions = await service.GetSubscriptionsForUserAsync("unknown@example.com");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_MapsSubscriptions_WhenCustomerExists()
    {
        var service = BuildService(
            DefaultResponder(customerJson: """{ "customer": { "id": 555 } }""", subscriptionsJson: SubscriptionsJson),
            out _);

        var subscriptions = await service.GetSubscriptionsForUserAsync("demouser@microsoft.com");

        var sub = Assert.Single(subscriptions);
        Assert.Equal(900, sub.Id);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.Equal("active", sub.State);
        Assert.Equal(29900, sub.PriceInCents);
        Assert.NotNull(sub.NextBillingDate);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesExistingLiveSubscription_Idempotent()
    {
        // Customer already exists and already has a live eshop-pro subscription: subscribe must NOT create
        // a new one, and must report AlreadyExisted = true. No POST should be issued.
        var service = BuildService(
            DefaultResponder(customerJson: """{ "customer": { "id": 555 } }""", subscriptionsJson: SubscriptionsJson),
            out var handler);

        var result = await service.SubscribeAsync(new SubscribeRequest
        {
            UserReference = "demouser@microsoft.com",
            Email = "demouser@microsoft.com",
            PlanHandle = "eshop-pro"
        });

        Assert.True(result.AlreadyExisted);
        Assert.Equal(900, result.Subscription.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_Throws400_WhenPlanUnknown()
    {
        var service = BuildService(DefaultResponder(), out _);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.SubscribeAsync(new SubscribeRequest
        {
            UserReference = "demouser@microsoft.com",
            Email = "demouser@microsoft.com",
            PlanHandle = "no-such-plan"
        }));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task GetPlansAsync_Throws503_OnTransportFailure()
    {
        // The stub throws instead of answering => a connection failure. It must surface as the single
        // billing-exception type with a 5xx (unreachable) status, not escape as HttpRequestException.
        var service = BuildService(_ => throw new HttpRequestException("connection reset"), out _);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.GetPlansAsync());

        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task GetPlansAsync_Throws500_WhenConfiguredFamilyMissing()
    {
        // Families list succeeds but does not contain the configured handle => misconfiguration => 500.
        var service = BuildService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("product_families"))
            {
                return RoutingStubHttpMessageHandler.Json(HttpStatusCode.OK,
                    """[ { "product_family": { "id": 1, "handle": "some-other-family", "name": "Other" } } ]""");
            }

            return RoutingStubHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
        }, out _);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.GetPlansAsync());

        Assert.Equal(500, ex.StatusCode);
    }
}
