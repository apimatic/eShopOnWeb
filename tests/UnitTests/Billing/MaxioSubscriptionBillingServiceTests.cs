using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;
using static Microsoft.eShopWeb.UnitTests.Billing.MaxioTestHarness;

namespace Microsoft.eShopWeb.UnitTests.Billing;

/// <summary>
/// Exercises the adapter through the real SDK over a stubbed transport, so serialization, the generated error
/// types and the retry pipeline are all real — only the network is faked.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Shopper =
        SubscriberIdentity.ForUser("demouser@microsoft.com");

    // Routes are matched in order, so the more specific path fragments come first.
    private static MaxioRouter Catalog() => new MaxioRouter()
        .Map(HttpMethod.Get, "products/handle/", HttpStatusCode.OK, ProPlanJson)
        .Map(HttpMethod.Get, "/products.json", HttpStatusCode.OK, ProductsJson)
        .Map(HttpMethod.Get, "product_families.json", HttpStatusCode.OK, ProductFamiliesJson)
        .Map(HttpMethod.Get, "site", HttpStatusCode.OK, SiteJson);

    // -----------------------------------------------------------------------------------------------
    // Plans
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ListPlansAsyncMapsPricesAndDropsArchivedPlans()
    {
        var (service, _) = CreateService(Catalog());

        var plans = await service.ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", ProPlanHandle }, plans.Select(p => p.Handle));

        var pro = plans.Single(p => p.Handle == ProPlanHandle);
        Assert.Equal(299.00m, pro.Price);          // price_in_cents is a long; it must not be reported as cents
        Assert.Equal("USD", pro.Currency);         // products carry no currency — it comes from the site
        Assert.Equal(1, pro.IntervalCount);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task ListPlansAsyncStillListsPlansWhenTheSiteCannotBeRead()
    {
        var router = new MaxioRouter()
            .Map(HttpMethod.Get, "/products.json", HttpStatusCode.OK, ProductsJson)
            .Map(HttpMethod.Get, "product_families.json", HttpStatusCode.OK, ProductFamiliesJson)
            .Map(HttpMethod.Get, "site", HttpStatusCode.InternalServerError, "{}");

        var (service, _) = CreateService(router);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.All(plans, p => Assert.Null(p.Currency));
    }

    [Fact]
    public async Task ListPlansAsyncReportsAnUnreachableProviderAsUnavailable()
    {
        var router = new MaxioRouter()
            .Map(HttpMethod.Get, "product_families.json",
                _ => throw new HttpRequestException("connection reset"));

        var (service, _) = CreateService(router);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync());

        Assert.Equal(502, ex.StatusCode);
        Assert.DoesNotContain("connection reset", ex.Message);   // provider detail never reaches the caller
    }

    // -----------------------------------------------------------------------------------------------
    // Subscribe — the hero flow
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SubscribeAsyncCreatesTheCustomerThenTheSubscriptionForANewShopper()
    {
        var router = Catalog()
            .Map(HttpMethod.Get, "customers/lookup", HttpStatusCode.NotFound, "{}")
            .Map(HttpMethod.Post, "/customers.json", HttpStatusCode.Created, CustomerJson)
            .Map(HttpMethod.Get, "/subscriptions", HttpStatusCode.OK, "[]")
            .Map(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson);

        var (service, handler) = CreateService(router);

        var result = await service.SubscribeAsync(Shopper, ProPlanHandle);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(SubscriptionId, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal(ProPlanHandle, result.Subscription.PlanHandle);
        Assert.Equal(
            DateTimeOffset.Parse("2026-10-06T15:52:48-04:00"), result.Subscription.NextBillingDate);

        var customerBody = BodyOf(handler, HttpMethod.Post, "/customers.json");
        Assert.Contains("\"reference\":\"eshoponweb-demouser@microsoft.com\"", customerBody);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", customerBody);

        var subscriptionBody = BodyOf(handler, HttpMethod.Post, "/subscriptions.json");
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody);
        Assert.Contains($"\"customer_id\":{CustomerId}", subscriptionBody);
        // Nothing is captured for payment, so collection must not be left on the site's automatic default.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionBody);
        Assert.DoesNotContain("credit_card", subscriptionBody);
    }

    [Fact]
    public async Task SubscribeAsyncReturnsTheExistingSubscriptionInsteadOfCreatingASecond()
    {
        var router = Catalog()
            .Map(HttpMethod.Get, "customers/lookup", HttpStatusCode.OK, CustomerJson)
            .Map(HttpMethod.Get, "/subscriptions", HttpStatusCode.OK, SubscriptionListJson)
            .Map(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson);

        var (service, handler) = CreateService(router);

        var result = await service.SubscribeAsync(Shopper, ProPlanHandle);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(SubscriptionId, result.Subscription.Id);

        // The point of the guard: no write was issued at all.
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers"));
    }

    [Fact]
    public async Task SubscribeAsyncCreatesAFreshSubscriptionWhenTheOldOneWasCanceled()
    {
        var router = Catalog()
            .Map(HttpMethod.Get, "customers/lookup", HttpStatusCode.OK, CustomerJson)
            .Map(HttpMethod.Get, "/subscriptions", HttpStatusCode.OK, CanceledSubscriptionListJson)
            .Map(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson);

        var (service, handler) = CreateService(router);

        var result = await service.SubscribeAsync(Shopper, ProPlanHandle);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions"));
    }

    [Fact]
    public async Task ConcurrentSubscribeRequestsProduceExactlyOneSubscription()
    {
        var created = 0;
        var subscriptions = "[]";

        var router = Catalog()
            .Map(HttpMethod.Get, "customers/lookup", HttpStatusCode.OK, CustomerJson)
            .Map(HttpMethod.Get, "/subscriptions",
                _ => MaxioRouter.Json(HttpStatusCode.OK, Volatile.Read(ref subscriptions)))
            .Map(HttpMethod.Post, "/subscriptions.json", _ =>
            {
                Interlocked.Increment(ref created);
                Volatile.Write(ref subscriptions, SubscriptionListJson);
                return MaxioRouter.Json(HttpStatusCode.Created, SubscriptionJson);
            });

        var (service, _) = CreateService(router);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => service.SubscribeAsync(Shopper, ProPlanHandle)));

        Assert.Equal(1, created);
        Assert.Equal(1, results.Count(r => !r.AlreadySubscribed));
        Assert.All(results, r => Assert.Equal(SubscriptionId, r.Subscription.Id));
    }

    [Fact]
    public async Task SubscribeAsyncRejectsAnUnknownPlanWithoutTouchingCustomerState()
    {
        var router = new MaxioRouter()
            .Map(HttpMethod.Get, "products/handle/", HttpStatusCode.NotFound, "{}");

        var (service, handler) = CreateService(router);

        var ex = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(Shopper, "no-such-plan"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Empty(handler.Requests.Where(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task SubscribeAsyncRejectsAPlanFromAnotherProductFamily()
    {
        var router = new MaxioRouter()
            .Map(HttpMethod.Get, "products/handle/", HttpStatusCode.OK, ForeignPlanJson);

        var (service, _) = CreateService(router);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(Shopper, ProPlanHandle));
    }

    [Fact]
    public async Task SubscribeAsyncSurfacesMaxioValidationMessagesAsAClientError()
    {
        var router = Catalog()
            .Map(HttpMethod.Get, "customers/lookup", HttpStatusCode.OK, CustomerJson)
            .Map(HttpMethod.Get, "/subscriptions", HttpStatusCode.OK, "[]")
            .Map(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity,
                """{ "errors": ["No payment method was on file for the $299.00 balance"] }""");

        var (service, _) = CreateService(router);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Shopper, ProPlanHandle));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("No payment method was on file", ex.Message);
    }

    [Fact]
    public async Task SubscribeAsyncReusesTheCustomerMaxioAlreadyHasWhenCreationIsRejected()
    {
        // Maxio permits one customer per reference; a 422 here means the record already exists, and the SDK
        // cannot surface the message for that status — so the adapter settles it by looking the customer up.
        var lookups = 0;

        var router = Catalog()
            .Map(HttpMethod.Get, "customers/lookup", _ =>
                Interlocked.Increment(ref lookups) == 1
                    ? MaxioRouter.Json(HttpStatusCode.NotFound, "{}")
                    : MaxioRouter.Json(HttpStatusCode.OK, CustomerJson))
            .Map(HttpMethod.Post, "/customers.json", HttpStatusCode.UnprocessableEntity,
                """{ "errors": { "per_page": ["already taken"] } }""")
            .Map(HttpMethod.Get, "/subscriptions", HttpStatusCode.OK, "[]")
            .Map(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson);

        var (service, handler) = CreateService(router);

        var result = await service.SubscribeAsync(Shopper, ProPlanHandle);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(2, lookups);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers"));
    }

    // -----------------------------------------------------------------------------------------------
    // Write-once under a transport fault
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task AConnectionFailureOnCreateNeverResendsTheWrite()
    {
        var router = Catalog()
            .Map(HttpMethod.Get, "customers/lookup", HttpStatusCode.OK, CustomerJson)
            .Map(HttpMethod.Get, "/subscriptions", HttpStatusCode.OK, "[]")
            .Map(HttpMethod.Post, "/subscriptions.json",
                _ => throw new HttpRequestException("connection reset"));

        var (service, handler) = CreateService(router);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Shopper, ProPlanHandle));

        // The SDK retries a transport fault on any verb; the guard is what holds this at one.
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions"));
        Assert.Equal(504, ex.StatusCode);
    }

    [Fact]
    public async Task AConnectionFailureOnCreateIsReconciledWhenTheWriteHadAlreadyLanded()
    {
        var subscriptions = "[]";

        var router = Catalog()
            .Map(HttpMethod.Get, "customers/lookup", HttpStatusCode.OK, CustomerJson)
            .Map(HttpMethod.Get, "/subscriptions",
                _ => MaxioRouter.Json(HttpStatusCode.OK, Volatile.Read(ref subscriptions)))
            .Map(HttpMethod.Post, "/subscriptions.json", _ =>
            {
                // The write reached Maxio; only the response was lost.
                Volatile.Write(ref subscriptions, SubscriptionListJson);
                throw new HttpRequestException("connection reset after the request was received");
            });

        var (service, handler) = CreateService(router);

        var result = await service.SubscribeAsync(Shopper, ProPlanHandle);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(SubscriptionId, result.Subscription.Id);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions"));
    }

    // -----------------------------------------------------------------------------------------------
    // My subscriptions
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ListSubscriptionsAsyncIsEmptyForAShopperWithNoBillingAccount()
    {
        var router = new MaxioRouter()
            .Map(HttpMethod.Get, "customers/lookup", HttpStatusCode.NotFound, "{}");

        var (service, handler) = CreateService(router);

        Assert.Empty(await service.ListSubscriptionsAsync(Shopper));
        Assert.Single(handler.Requests);   // a miss must not cost a second round trip
    }

    [Fact]
    public async Task ListSubscriptionsAsyncReportsPlanPriceStateAndNextBillingDate()
    {
        var router = new MaxioRouter()
            .Map(HttpMethod.Get, "customers/lookup", HttpStatusCode.OK, CustomerJson)
            .Map(HttpMethod.Get, "/subscriptions", HttpStatusCode.OK, SubscriptionListJson);

        var (service, _) = CreateService(router);

        var subscription = Assert.Single(await service.ListSubscriptionsAsync(Shopper));

        Assert.Equal(SubscriptionId, subscription.Id);
        Assert.Equal(ProPlanHandle, subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal("active", subscription.State);
        Assert.Equal("remittance", subscription.PaymentCollectionMethod);
        Assert.True(subscription.IsLive);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T15:52:48-04:00"), subscription.NextBillingDate);
    }

    [Fact]
    public async Task ListSubscriptionsAsyncReportsAProviderFailureAsUnavailable()
    {
        var router = new MaxioRouter()
            .Map(HttpMethod.Get, "customers/lookup", HttpStatusCode.Unauthorized, "unauthorized");

        var (service, _) = CreateService(router);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListSubscriptionsAsync(Shopper));

        // A bad API key is this deployment's problem, not the caller's, so it must not surface as a 401.
        Assert.Equal(502, ex.StatusCode);
    }

    private static string BodyOf(StubHandler handler, HttpMethod method, string pathFragment)
    {
        for (var i = 0; i < handler.Requests.Count; i++)
        {
            if (handler.Requests[i].Method == method &&
                handler.Requests[i].RequestUri!.AbsolutePath.Contains(pathFragment, StringComparison.OrdinalIgnoreCase))
            {
                return handler.Bodies[i];
            }
        }

        throw new InvalidOperationException($"No {method} request to {pathFragment} was captured.");
    }
}
