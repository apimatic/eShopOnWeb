using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.MaxioTests;

/// <summary>
/// Exercises the Maxio subscription client against a stubbed Maxio API, focusing on the
/// idempotency behaviour that protects against duplicate customers/subscriptions.
/// </summary>
public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private static MaxioSubscriptionService CreateService(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.chargify.com/") };
        var options = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = FamilyHandle,
        });
        return new MaxioSubscriptionService(httpClient, options, NullLogger<MaxioSubscriptionService>.Instance);
    }

    // Canned responses for the plan-catalog calls that every operation performs.
    private static (HttpStatusCode, string)? RoutePlanCatalog(StubHttpMessageHandler.RecordedRequest r)
    {
        if (r.Method == "GET" && r.Path.EndsWith("/product_families.json"))
        {
            return (HttpStatusCode.OK,
                """[{"product_family":{"id":100,"handle":"eshop-subscribe","name":"eShopSubscribe"}}]""");
        }

        if (r.Method == "GET" && r.Path.EndsWith("/product_families/100/products.json"))
        {
            return (HttpStatusCode.OK,
                """
                [
                  {"product":{"id":11,"name":"Basic Plan","handle":"basic-plan","description":null,
                    "price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,
                    "archived_at":null,"product_family":{"id":100,"handle":"eshop-subscribe","name":"eShopSubscribe"}}},
                  {"product":{"id":12,"name":"Pro Plan","handle":"eshop-pro","description":null,
                    "price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,
                    "archived_at":null,"product_family":{"id":100,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}
                ]
                """);
        }

        return null;
    }

    [Fact]
    public async Task GetPlansAsync_MapsProductsToPlans()
    {
        var handler = new StubHttpMessageHandler(r => RoutePlanCatalog(r) ?? (HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task SubscribeAsync_WhenLiveSubscriptionExists_ReturnsExisting_AndDoesNotCreate()
    {
        var handler = new StubHttpMessageHandler(r =>
        {
            var catalog = RoutePlanCatalog(r);
            if (catalog is not null)
            {
                return catalog.Value;
            }

            if (r.Method == "GET" && r.Path.EndsWith("/customers/lookup.json"))
            {
                return (HttpStatusCode.OK,
                    """{"customer":{"id":5,"first_name":"Demo","last_name":"eShopOnWeb","email":"demo@example.com","reference":"eshopweb-user-U1"}}""");
            }

            if (r.Method == "GET" && r.Path.EndsWith("/customers/5/subscriptions.json"))
            {
                return (HttpStatusCode.OK,
                    """
                    [{"subscription":{"id":999,"state":"active","product_price_in_cents":29900,"currency":"USD",
                      "current_period_started_at":"2026-07-01T00:00:00+00:00","current_period_ends_at":"2026-08-01T00:00:00+00:00",
                      "next_assessment_at":"2026-08-01T00:00:00+00:00",
                      "product":{"id":12,"name":"Pro Plan","handle":"eshop-pro"},
                      "customer":{"id":5,"reference":"eshopweb-user-U1"}}}]
                    """);
            }

            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var subscriber = new SubscriberIdentity("U1", "demo@example.com");
        var result = await service.SubscribeAsync(subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        // Crucially: no subscription was created.
        Assert.DoesNotContain(handler.Requests, req => req.Method == "POST" && req.Path.EndsWith("/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenNoCustomer_CreatesCustomerThenSubscription_WithRemittance()
    {
        var handler = new StubHttpMessageHandler(r =>
        {
            var catalog = RoutePlanCatalog(r);
            if (catalog is not null)
            {
                return catalog.Value;
            }

            if (r.Method == "GET" && r.Path.EndsWith("/customers/lookup.json"))
            {
                return (HttpStatusCode.NotFound, string.Empty);
            }

            if (r.Method == "POST" && r.Path.EndsWith("/customers.json"))
            {
                return (HttpStatusCode.Created,
                    """{"customer":{"id":7,"first_name":"Demo","last_name":"eShopOnWeb","email":"demo@example.com","reference":"eshopweb-user-U1"}}""");
            }

            if (r.Method == "GET" && r.Path.EndsWith("/customers/7/subscriptions.json"))
            {
                return (HttpStatusCode.OK, "[]");
            }

            if (r.Method == "POST" && r.Path.EndsWith("/subscriptions.json"))
            {
                return (HttpStatusCode.Created,
                    """
                    {"subscription":{"id":123,"state":"active","product_price_in_cents":29900,"currency":"USD",
                      "current_period_started_at":"2026-07-29T00:00:00+00:00","current_period_ends_at":"2026-08-29T00:00:00+00:00",
                      "next_assessment_at":"2026-08-29T00:00:00+00:00",
                      "product":{"id":12,"name":"Pro Plan","handle":"eshop-pro"},
                      "customer":{"id":7,"reference":"eshopweb-user-U1"}}}
                    """);
            }

            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var subscriber = new SubscriberIdentity("U1", "demo@example.com");
        var result = await service.SubscribeAsync(subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(123, result.Subscription.Id);

        var createCustomer = handler.Requests.Single(req => req.Method == "POST" && req.Path.EndsWith("/customers.json"));
        Assert.Contains("eshopweb-user-U1", createCustomer.Body);

        var createSub = handler.Requests.Single(req => req.Method == "POST" && req.Path.EndsWith("/subscriptions.json"));
        Assert.Contains("remittance", createSub.Body);
        Assert.Contains("\"customer_id\":7", createSub.Body);
        Assert.Contains("eshop-pro", createSub.Body);
    }

    [Fact]
    public async Task SubscribeAsync_WhenPlanHandleUnknown_Throws()
    {
        var handler = new StubHttpMessageHandler(r => RoutePlanCatalog(r) ?? (HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var subscriber = new SubscriberIdentity("U1", "demo@example.com");

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(subscriber, "nope"));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_WhenNoCustomer_ReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler(r =>
        {
            if (r.Method == "GET" && r.Path.EndsWith("/customers/lookup.json"))
            {
                return (HttpStatusCode.NotFound, string.Empty);
            }

            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var result = await service.GetSubscriptionsAsync(new SubscriberIdentity("U1", "demo@example.com"));

        Assert.Empty(result);
    }
}
