#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionServiceTests
{
    private static readonly SubscriberIdentity Shopper =
        new("shopper@example.com", "shopper@example.com");

    /// <summary>
    /// Routes a fake Maxio by verb and path. Anything unrouted answers 500, so a call the test did
    /// not anticipate fails loudly instead of silently passing.
    /// </summary>
    private static Func<HttpRequestMessage, string, HttpResponseMessage> Router(
        string? products = null,
        Func<HttpResponseMessage>? customerLookup = null,
        Func<HttpResponseMessage>? createCustomer = null,
        Func<HttpResponseMessage>? customerSubscriptions = null,
        Func<HttpResponseMessage>? createSubscription = null,
        Func<HttpResponseMessage>? subscriptionLookup = null)
    {
        return (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var isPost = request.Method == HttpMethod.Post;

            if (path.Contains("/products.json", StringComparison.OrdinalIgnoreCase))
            {
                return MaxioStubHandler.Json(HttpStatusCode.OK, products ?? MaxioResponses.TwoProducts);
            }

            if (path.Contains("/customers/lookup", StringComparison.OrdinalIgnoreCase))
            {
                return customerLookup?.Invoke()
                    ?? MaxioStubHandler.Json(HttpStatusCode.NotFound, "{}");
            }

            if (path.Contains("/subscriptions/lookup", StringComparison.OrdinalIgnoreCase))
            {
                return subscriptionLookup?.Invoke()
                    ?? MaxioStubHandler.Json(HttpStatusCode.NotFound, "{}");
            }

            if (path.Contains("/customers/", StringComparison.OrdinalIgnoreCase) &&
                path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return customerSubscriptions?.Invoke()
                    ?? MaxioStubHandler.Json(HttpStatusCode.OK, MaxioResponses.NoSubscriptions);
            }

            if (isPost && path.Contains("customers", StringComparison.OrdinalIgnoreCase))
            {
                return createCustomer?.Invoke()
                    ?? MaxioStubHandler.Json(HttpStatusCode.Created, MaxioResponses.Customer);
            }

            if (isPost && path.Contains("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return createSubscription?.Invoke()
                    ?? MaxioStubHandler.Json(HttpStatusCode.Created, MaxioResponses.CreatedSubscription);
            }

            return MaxioStubHandler.Json(HttpStatusCode.InternalServerError, "{\"unrouted\":true}");
        };
    }

    [Fact]
    public async Task ListPlansAsync_MapsPriceIntervalAndFamily()
    {
        var handler = new MaxioStubHandler(Router());
        await using var provider = MaxioTestHost.Build(handler);

        var plans = await provider.Service().ListPlansAsync();

        var pro = Assert.Single(plans, plan => plan.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("test-family", pro.ProductFamilyHandle);
        Assert.False(pro.RequiresPaymentMethod);
        Assert.False(pro.HasTrial);
        Assert.Equal(0, pro.SetupFeeInCents);
    }

    [Fact]
    public async Task ListPlansAsync_RequestsTheConfiguredFamilyAndDropsArchivedPlans()
    {
        var handler = new MaxioStubHandler(Router(products: MaxioResponses.ProductsIncludingArchived));
        await using var provider = MaxioTestHost.Build(handler);

        var plans = await provider.Service().ListPlansAsync();

        Assert.Equal(new[] { "eshop-pro" }, plans.Select(plan => plan.Handle));

        var request = Assert.Single(handler.Requests);
        Assert.Contains("test-family", Uri.UnescapeDataString(request.Path));
    }

    [Fact]
    public async Task ListPlansAsync_ReportsAMissingProductFamilyAsConfiguration()
    {
        var handler = new MaxioStubHandler((request, _) =>
            MaxioStubHandler.Json(HttpStatusCode.NotFound, "\"Product family not found\""));
        await using var provider = MaxioTestHost.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => provider.Service().ListPlansAsync());

        Assert.Contains("test-family", exception.Message);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesTheCustomerAndTheSubscriptionWhenNeitherExists()
    {
        var handler = new MaxioStubHandler(Router());
        await using var provider = MaxioTestHost.Build(handler);

        var result = await provider.Service().SubscribeAsync(new SubscribeRequest(Shopper, "eshop-pro"));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(900, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal("eshop-pro", result.Plan.Handle);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);

        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "customers"));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsync_SendsADeterministicReferenceAndACardlessCollectionMethod()
    {
        var handler = new MaxioStubHandler(Router());
        await using var provider = MaxioTestHost.Build(handler);

        await provider.Service().SubscribeAsync(new SubscribeRequest(Shopper, "eshop-pro"));

        var customerCreate = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path.Contains("customers"));
        Assert.Contains("\"reference\":", customerCreate.Body);
        Assert.Contains(MaxioReference.ForCustomer(Shopper.UserName), customerCreate.Body);

        var subscriptionCreate = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path.Contains("subscriptions"));
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionCreate.Body);
        Assert.Contains("\"customer_id\":42", subscriptionCreate.Body);

        // The plan needs no card, so the request must not fall through to the site default
        // (automatic), which rejects a signup with no payment method on file.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionCreate.Body);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingSubscriptionWithoutCreatingASecond()
    {
        var handler = new MaxioStubHandler(Router(
            customerLookup: () => MaxioStubHandler.Json(HttpStatusCode.OK, MaxioResponses.Customer),
            customerSubscriptions: () => MaxioStubHandler.Json(HttpStatusCode.OK, MaxioResponses.ActiveProSubscription)));
        await using var provider = MaxioTestHost.Build(handler);

        var result = await provider.Service().SubscribeAsync(new SubscribeRequest(Shopper, "eshop-pro"));

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(777, result.Subscription.Id);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "subscriptions"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "customers"));
    }

    [Fact]
    public async Task SubscribeAsync_AllowsResubscribingAfterCancellationWithAFreshReference()
    {
        // The shopper's earlier, now-cancelled subscription already owns the base reference.
        var baseReference = MaxioReference.ForSubscription(
            MaxioReference.ForCustomer(Shopper.UserName), "eshop-pro");

        var handler = new MaxioStubHandler(Router(
            customerLookup: () => MaxioStubHandler.Json(HttpStatusCode.OK, MaxioResponses.Customer),
            customerSubscriptions: () => MaxioStubHandler.Json(HttpStatusCode.OK,
                MaxioResponses.CanceledProSubscription(baseReference))));
        await using var provider = MaxioTestHost.Build(handler);

        var result = await provider.Service().SubscribeAsync(new SubscribeRequest(Shopper, "eshop-pro"));

        Assert.False(result.AlreadySubscribed);

        // A cancelled subscription does not block a new signup, but the new one must not collide
        // with the reference the old one still holds.
        var create = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path.Contains("subscriptions"));
        Assert.DoesNotContain("\"reference\":\"" + baseReference + "\"", create.Body);
        Assert.Contains(baseReference + "--2", create.Body);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesTheCustomerWhenAConcurrentRequestCreatedItFirst()
    {
        var lookupCalls = 0;
        var handler = new MaxioStubHandler(Router(
            customerLookup: () =>
            {
                lookupCalls++;

                // Miss first, then hit: the shape of losing a race to a concurrent request.
                return lookupCalls == 1
                    ? MaxioStubHandler.Json(HttpStatusCode.NotFound, "{}")
                    : MaxioStubHandler.Json(HttpStatusCode.OK, MaxioResponses.Customer);
            },
            createCustomer: () => MaxioStubHandler.Json(HttpStatusCode.UnprocessableEntity, MaxioResponses.DuplicateCustomerError)));
        await using var provider = MaxioTestHost.Build(handler);

        var result = await provider.Service().SubscribeAsync(new SubscribeRequest(Shopper, "eshop-pro"));

        Assert.Equal(900, result.Subscription.Id);
        Assert.Equal(2, lookupCalls);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "customers"));
    }

    [Fact]
    public async Task SubscribeAsync_SurfacesTheProvidersRejectionVerbatimAsAValidationFailure()
    {
        var handler = new MaxioStubHandler(Router(
            createSubscription: () => MaxioStubHandler.Json(HttpStatusCode.UnprocessableEntity, MaxioResponses.NoPaymentMethodError)));
        await using var provider = MaxioTestHost.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => provider.Service().SubscribeAsync(new SubscribeRequest(Shopper, "eshop-pro")));

        Assert.Contains("No payment method was on file", exception.Message);
        Assert.Contains("No payment method was on file for the $299.00 balance", exception.Errors);
        Assert.Equal(422, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAnUnknownPlanWithoutCallingTheProvider()
    {
        var handler = new MaxioStubHandler(Router());
        await using var provider = MaxioTestHost.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingNotFoundException>(
            () => provider.Service().SubscribeAsync(new SubscribeRequest(Shopper, "no-such-plan")));

        Assert.Contains("no-such-plan", exception.Message);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAnUnspecifiedPlanWhenNoDefaultIsConfigured()
    {
        var handler = new MaxioStubHandler(Router());
        await using var provider = MaxioTestHost.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => provider.Service().SubscribeAsync(new SubscribeRequest(Shopper)));

        Assert.Contains("eshop-pro", exception.Message);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsync_UsesTheConfiguredDefaultPlanWhenTheCallerNamesNone()
    {
        var handler = new MaxioStubHandler(Router());
        await using var provider = MaxioTestHost.Build(handler,
            new Dictionary<string, string?> { ["Maxio:DefaultProductHandle"] = "eshop-pro" });

        var result = await provider.Service().SubscribeAsync(new SubscribeRequest(Shopper));

        Assert.Equal("eshop-pro", result.Plan.Handle);
    }

    /// <summary>
    /// The SDK resends on a transport failure regardless of verb, so without the write-once guard
    /// a shopper could be enrolled twice off one click. This asserts the count the provider
    /// actually receives - not merely that an exception was raised.
    /// </summary>
    [Fact]
    public async Task SubscribeAsync_PutsTheEnrollmentOnTheWireExactlyOnceWhenTheConnectionFails()
    {
        var handler = new MaxioStubHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                throw new HttpRequestException("connection reset");
            }

            return Router()(request, string.Empty);
        });
        await using var provider = MaxioTestHost.Build(handler);

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(
            () => provider.Service().SubscribeAsync(new SubscribeRequest(Shopper, "eshop-pro")));

        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsync_ReconcilesAWriteWhoseOutcomeWasLost()
    {
        var handler = new MaxioStubHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                // The provider received it; the response never came back.
                throw new HttpRequestException("connection reset");
            }

            return Router(subscriptionLookup: () =>
                MaxioStubHandler.Json(HttpStatusCode.OK, MaxioResponses.FoundSubscription))(request, string.Empty);
        });
        await using var provider = MaxioTestHost.Build(handler);

        var result = await provider.Service().SubscribeAsync(new SubscribeRequest(Shopper, "eshop-pro"));

        Assert.Equal(901, result.Subscription.Id);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "subscriptions"));
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmptyForAShopperWhoNeverSubscribed()
    {
        var handler = new MaxioStubHandler(Router());
        await using var provider = MaxioTestHost.Build(handler);

        var subscriptions = await provider.Service().ListSubscriptionsAsync(Shopper);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_MapsStatePriceAndNextBillingDate()
    {
        var handler = new MaxioStubHandler(Router(
            customerLookup: () => MaxioStubHandler.Json(HttpStatusCode.OK, MaxioResponses.Customer),
            customerSubscriptions: () => MaxioStubHandler.Json(HttpStatusCode.OK, MaxioResponses.ActiveProSubscription)));
        await using var provider = MaxioTestHost.Build(handler);

        var subscription = Assert.Single(await provider.Service().ListSubscriptionsAsync(Shopper));

        Assert.Equal(777, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.True(subscription.IsLive);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299m, subscription.Price);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingAt);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReportsAnUnreachableProviderAsUnavailableNotAsEmpty()
    {
        var handler = new MaxioStubHandler((_, _) => throw new HttpRequestException("dns failure"));
        await using var provider = MaxioTestHost.Build(handler);

        await Assert.ThrowsAsync<BillingUnavailableException>(
            () => provider.Service().ListSubscriptionsAsync(Shopper));
    }

    /// <summary>
    /// An unreadable lookup body must never be mistaken for "this shopper has no billing
    /// customer" - that conversion would turn a corrupt response into a spurious create.
    /// </summary>
    [Fact]
    public async Task ListSubscriptionsAsync_DoesNotTreatAnUnreadableBodyAsAMiss()
    {
        var handler = new MaxioStubHandler((_, _) =>
            MaxioStubHandler.Json(HttpStatusCode.OK, "{ \"customer\": \"not-an-object\" }"));
        await using var provider = MaxioTestHost.Build(handler);

        await Assert.ThrowsAsync<BillingUnavailableException>(
            () => provider.Service().ListSubscriptionsAsync(Shopper));
    }

    [Fact]
    public async Task ListPlansAsync_ReportsRejectedCredentialsAsConfigurationNotAsAnOutage()
    {
        var handler = new MaxioStubHandler((_, _) =>
            MaxioStubHandler.Json(HttpStatusCode.Unauthorized, "{}"));
        await using var provider = MaxioTestHost.Build(handler);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => provider.Service().ListPlansAsync());
    }

    [Fact]
    public async Task ListPlansAsync_TargetsTheSubdomainSiteByDefault()
    {
        var handler = new MaxioStubHandler(Router());
        await using var provider = MaxioTestHost.Build(handler);

        await provider.Service().ListPlansAsync();

        Assert.Equal("test-site.chargify.com", handler.Requests[0].Host);
    }

    /// <summary>
    /// The base-URL override must be used verbatim - the default address is a template and a
    /// value without the site placeholder has to pass through unchanged.
    /// </summary>
    [Fact]
    public async Task ListPlansAsync_UsesTheConfiguredBaseUrlVerbatimWhenOneIsSet()
    {
        var handler = new MaxioStubHandler(Router());
        await using var provider = MaxioTestHost.Build(handler, new Dictionary<string, string?>
        {
            ["Maxio:BaseUrl"] = "https://maxio-gateway.internal.example.com"
        });

        await provider.Service().ListPlansAsync();

        Assert.Equal("maxio-gateway.internal.example.com", handler.Requests[0].Host);
    }

    [Fact]
    public async Task ListPlansAsync_IsServedFromCacheWithinTheConfiguredWindow()
    {
        var handler = new MaxioStubHandler(Router());
        await using var provider = MaxioTestHost.Build(handler,
            new Dictionary<string, string?> { ["Maxio:PlanCacheSeconds"] = "60" });

        await provider.Service().ListPlansAsync();
        await provider.Service().ListPlansAsync();

        Assert.Single(handler.Requests);
    }
}
