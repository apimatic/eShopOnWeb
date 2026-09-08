using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string User = "demouser@microsoft.com";
    private const string Plan = "eshop-pro";

    [Fact]
    public async Task GetCatalogAsyncMapsPlansAndMeteredComponent()
    {
        using var host = new MaxioTestHost(DefaultResponder());

        var catalog = await host.Service.GetCatalogAsync(CancellationToken.None);

        Assert.Equal(2, catalog.Plans.Count);
        var pro = Assert.Single(catalog.Plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresCreditCard);
        Assert.DoesNotContain(catalog.Plans, p => p.Handle == "retired-plan");
    }

    [Fact]
    public async Task SubscribeTwiceCreatesExactlyOneSubscription()
    {
        using var host = new MaxioTestHost(DefaultResponder());

        var first = await host.Service.SubscribeAsync(
            new SubscribeToPlanRequest(User, Plan, null, null), CancellationToken.None);
        var second = await host.Service.SubscribeAsync(
            new SubscribeToPlanRequest(User, Plan, null, null), CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(Plan, first.Subscription.PlanHandle);
        Assert.Equal("Pro Plan", first.Subscription.PlanName);
        Assert.Equal(29900, first.Subscription.PriceInCents);
        Assert.Equal("active", first.Subscription.State);
        Assert.Equal(first.Subscription.Reference, second.Subscription.Reference);

        // The idempotency gate: a repeat subscribe never issues a second create.
        Assert.Single(host.Handler.WritesTo("subscriptions.json"));
    }

    [Fact]
    public async Task ConcurrentDoubleClickCreatesExactlyOneSubscription()
    {
        using var host = new MaxioTestHost(ResponderWithSlowCreate());

        var first = host.Service.SubscribeAsync(
            new SubscribeToPlanRequest(User, Plan, null, null), CancellationToken.None);
        var second = host.Service.SubscribeAsync(
            new SubscribeToPlanRequest(User, Plan, null, null), CancellationToken.None);

        var results = await Task.WhenAll(first, second);

        var createdCount = (results[0].Created ? 1 : 0) + (results[1].Created ? 1 : 0);
        Assert.Equal(1, createdCount);
        Assert.Single(host.Handler.WritesTo("subscriptions.json"));
        Assert.Single(host.Handler.WritesTo("customers.json"));
    }

    [Fact]
    public async Task SubscribeToUnknownPlanThrowsPlanNotFound()
    {
        using var host = new MaxioTestHost(DefaultResponder());

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            host.Service.SubscribeAsync(
                new SubscribeToPlanRequest(User, "no-such-plan", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task GetSubscriptionsAsyncReturnsCurrentSubscriptions()
    {
        using var host = new MaxioTestHost(DefaultResponder());

        await host.Service.SubscribeAsync(
            new SubscribeToPlanRequest(User, Plan, null, null), CancellationToken.None);

        var subscriptions = await host.Service.GetSubscriptionsAsync(User, CancellationToken.None);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(Plan, subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.PriceInCents);
    }

    [Fact]
    public async Task GetSubscriptionsAsyncIsEmptyWhenNoCustomerExistsYet()
    {
        using var host = new MaxioTestHost(DefaultResponder());

        var subscriptions = await host.Service.GetSubscriptionsAsync(User, CancellationToken.None);

        Assert.Empty(subscriptions);
    }

    // -------- fixtures --------

    private static string CustomerReference => MaxioReferenceKeys.CustomerReference(User);
    private static string SubscriptionReference => MaxioReferenceKeys.SubscriptionReference(User, Plan);

    private static ScriptedHandler ResponderWithSlowCreate() => BuildHandler(slowCreate: true);

    private static ScriptedHandler DefaultResponder() => BuildHandler(slowCreate: false);

    private static ScriptedHandler BuildHandler(bool slowCreate)
    {
        var customerCreated = false;
        var subscriptionCreated = false;

        return new ScriptedHandler(async request =>
        {
            var method = request.Method;
            var path = request.RequestUri!.AbsolutePath;

            if (slowCreate && method == HttpMethod.Post && path == "/subscriptions.json")
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                subscriptionCreated = true;
                return await MaxioTestResponses.Json(HttpStatusCode.Created, SubscriptionJson);
            }

            if (method == HttpMethod.Get && path == "/product_families.json")
            {
                return await MaxioTestResponses.Json(ProductFamiliesJson);
            }

            if (method == HttpMethod.Get && path.StartsWith("/product_families/", StringComparison.Ordinal) &&
                path.EndsWith("/products.json", StringComparison.Ordinal))
            {
                return await MaxioTestResponses.Json(ProductsJson);
            }

            if (method == HttpMethod.Get && path == "/components/lookup.json")
            {
                return await MaxioTestResponses.Json(ComponentJson);
            }

            if (method == HttpMethod.Get && path.StartsWith("/products/handle/", StringComparison.Ordinal))
            {
                return path.EndsWith("/eshop-pro.json", StringComparison.Ordinal)
                    ? await MaxioTestResponses.Json(ProProductJson)
                    : await MaxioTestResponses.NotFound();
            }

            if (method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                return customerCreated
                    ? await MaxioTestResponses.Json(CustomerJson)
                    : await MaxioTestResponses.NotFound();
            }

            if (method == HttpMethod.Post && path == "/customers.json")
            {
                customerCreated = true;
                return await MaxioTestResponses.Json(CustomerJson);
            }

            if (method == HttpMethod.Post && path == "/subscriptions.json")
            {
                subscriptionCreated = true;
                return await MaxioTestResponses.Json(HttpStatusCode.Created, SubscriptionJson);
            }

            if (method == HttpMethod.Get && path.StartsWith("/customers/", StringComparison.Ordinal) &&
                path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return await MaxioTestResponses.Json(
                    subscriptionCreated ? SubscriptionsListJson : EmptyListJson);
            }

            return await MaxioTestResponses.NotFound();
        });
    }

    private const string ProductFamiliesJson =
        """
        [ { "product_family": { "id": 100, "name": "eShopSubscribe", "handle": "eshop-subscribe" } } ]
        """;

    private const string ProductsJson =
        """
        [
          { "product": { "id": 200, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false,
                         "archived_at": null,
                         "product_family": { "id": 100, "name": "eShopSubscribe", "handle": "eshop-subscribe" } } },
          { "product": { "id": 201, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false,
                         "archived_at": null,
                         "product_family": { "id": 100, "name": "eShopSubscribe", "handle": "eshop-subscribe" } } },
          { "product": { "id": 202, "name": "Retired Plan", "handle": "retired-plan", "price_in_cents": 100,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false,
                         "archived_at": "2020-01-01T00:00:00Z",
                         "product_family": { "id": 100, "name": "eShopSubscribe", "handle": "eshop-subscribe" } } }
        ]
        """;

    private const string ComponentJson =
        """
        { "component": { "id": 300, "handle": "api-call", "kind": "metered_component",
                         "product_family_handle": "eshop-subscribe", "price_per_unit_in_cents": 1 } }
        """;

    private const string ProProductJson =
        """
        { "product": { "id": 200, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900,
                       "interval": 1, "interval_unit": "month", "require_credit_card": false,
                       "archived_at": null,
                       "product_family": { "id": 100, "name": "eShopSubscribe", "handle": "eshop-subscribe" } } }
        """;

    private static string CustomerJson =>
        $$"""
          { "customer": { "id": 500, "reference": "{{CustomerReference}}", "email": "{{User}}",
                          "first_name": "demouser", "last_name": "microsoft" } }
          """;

    private static string SubscriptionJson =>
        $$"""
          { "subscription": { "id": 7001, "state": "active", "reference": "{{SubscriptionReference}}",
                              "product_price_in_cents": 29900,
                              "current_period_ends_at": "2026-10-08T12:00:00Z",
                              "next_assessment_at": "2026-10-08T12:00:00Z",
                              "created_at": "2026-09-08T12:00:00Z",
                              "product": { "id": 200, "name": "Pro Plan", "handle": "eshop-pro",
                                           "price_in_cents": 29900, "interval": 1,
                                           "interval_unit": "month", "require_credit_card": false } } }
          """;

    private static string SubscriptionsListJson =>
        $$"""
          [ { "subscription": { "id": 7001, "state": "active", "reference": "{{SubscriptionReference}}",
                                "product_price_in_cents": 29900,
                                "current_period_ends_at": "2026-10-08T12:00:00Z",
                                "next_assessment_at": "2026-10-08T12:00:00Z",
                                "created_at": "2026-09-08T12:00:00Z",
                                "product": { "id": 200, "name": "Pro Plan", "handle": "eshop-pro",
                                             "price_in_cents": 29900, "interval": 1,
                                             "interval_unit": "month", "require_credit_card": false } } } ]
          """;

    private const string EmptyListJson = "[]";
}
