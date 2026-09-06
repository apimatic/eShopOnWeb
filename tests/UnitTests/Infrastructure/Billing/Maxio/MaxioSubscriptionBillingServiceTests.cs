using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Subscriber =
        SubscriberIdentity.Create("demo@example.com", "demo@example.com", "Demo", "eShopOnWeb");

    /// <summary>
    /// Routes a faked Maxio response per request. Anything unrouted answers 500 with the path, so a wrong
    /// URL fails the test loudly instead of looking like a provider outage.
    /// </summary>
    private static StubMaxioHandler Route(
        string? customerLookup = null,
        HttpStatusCode customerLookupStatus = HttpStatusCode.NotFound,
        string? customerSubscriptions = null,
        string? createSubscription = null,
        HttpStatusCode createSubscriptionStatus = HttpStatusCode.Created,
        string? products = null,
        bool relationshipInvoicing = true,
        Func<HttpRequestMessage, HttpResponseMessage>? createSubscriptionOverride = null)
    {
        products ??= MaxioBillingHarness.Products(
            MaxioBillingHarness.Product("eshop-pro", "Pro Plan", 29900),
            MaxioBillingHarness.Product("basic-plan", "Basic Plan", 2900));

        return new StubMaxioHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("/site.json", StringComparison.Ordinal))
            {
                return MaxioBillingHarness.Json(HttpStatusCode.OK, MaxioBillingHarness.Site(relationshipInvoicing: relationshipInvoicing));
            }

            if (path.EndsWith("/products.json", StringComparison.Ordinal))
            {
                return MaxioBillingHarness.Json(HttpStatusCode.OK, products);
            }

            if (path.Contains("product_families", StringComparison.Ordinal))
            {
                return MaxioBillingHarness.Json(HttpStatusCode.OK, MaxioBillingHarness.ProductFamilies());
            }

            if (path.Contains("/customers/lookup.json", StringComparison.Ordinal))
            {
                return customerLookup is null
                    ? MaxioBillingHarness.NotFound()
                    : MaxioBillingHarness.Json(customerLookupStatus, customerLookup);
            }

            if (path.Contains("/subscriptions.json", StringComparison.Ordinal) && path.Contains("/customers/", StringComparison.Ordinal))
            {
                return MaxioBillingHarness.Json(HttpStatusCode.OK, customerSubscriptions ?? "[]");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.Ordinal))
            {
                return MaxioBillingHarness.Json(HttpStatusCode.Created, MaxioBillingHarness.Customer());
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                if (createSubscriptionOverride is not null)
                {
                    return createSubscriptionOverride(request);
                }

                return MaxioBillingHarness.Json(
                    createSubscriptionStatus,
                    createSubscription ?? MaxioBillingHarness.Subscription(1001, "active", "eshop-pro", "Pro Plan", 29900));
            }

            return MaxioBillingHarness.Json(HttpStatusCode.InternalServerError, "{\"errors\":[\"unrouted: " + path + "\"]}");
        });
    }

    [Fact]
    public async Task GetPlansAsync_MapsPlansFromTheConfiguredFamilyAndTakesCurrencyFromTheSite()
    {
        var handler = Route();
        var billing = MaxioBillingHarness.Build(handler);

        var plans = await billing.GetPlansAsync();

        Assert.Collection(plans,
            plan =>
            {
                Assert.Equal("basic-plan", plan.Handle);
                Assert.Equal(2900, plan.PriceInCents);
                Assert.Equal(29m, plan.Price);
                Assert.Equal("USD", plan.Currency);
                Assert.Equal(1, plan.Interval);
                Assert.Equal("month", plan.IntervalUnit);
            },
            plan =>
            {
                Assert.Equal("eshop-pro", plan.Handle);
                Assert.Equal(299m, plan.Price);
            });

        // The family is addressed by its resolved numeric id, never by the handle.
        Assert.Contains(handler.Requests, request => request.Path.Contains($"/product_families/{MaxioBillingHarness.FamilyId}/products.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPlansAsync_ExcludesArchivedPlans()
    {
        var handler = Route(products: MaxioBillingHarness.Products(
            MaxioBillingHarness.Product("eshop-pro", "Pro Plan", 29900),
            MaxioBillingHarness.Product("retired-plan", "Retired", 100, archivedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))));

        var plans = await MaxioBillingHarness.Build(handler).GetPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
        Assert.Contains(handler.Requests, request => request.Path.Contains("include_archived=false", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPlansAsync_ReportsPlansThatRequireACard()
    {
        // request_credit_card is a deprecated legacy field and must not be read as a payment requirement.
        var handler = Route(products: MaxioBillingHarness.Products(
            MaxioBillingHarness.Product("card-less", "Card-less", 100, requireCreditCard: false, requestCreditCard: true),
            MaxioBillingHarness.Product("card-required", "Card required", 200, requireCreditCard: true, requestCreditCard: false)));

        var plans = await MaxioBillingHarness.Build(handler).GetPlansAsync();

        Assert.False(plans.Single(plan => plan.Handle == "card-less").RequiresPaymentMethod);
        Assert.True(plans.Single(plan => plan.Handle == "card-required").RequiresPaymentMethod);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesTheCustomerAndTheSubscription_WhenNeitherExists()
    {
        var handler = Route();
        var billing = MaxioBillingHarness.Build(handler);

        var result = await billing.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(1001, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.Equal(MaxioBillingHarness.PeriodEndsAt, result.Subscription.NextBillingDate);

        var customerBody = handler.BodyOf(HttpMethod.Post, "/customers.json");
        Assert.Contains("\"reference\":\"eshoponweb:demo@example.com\"", customerBody, StringComparison.Ordinal);

        var subscriptionBody = handler.BodyOf(HttpMethod.Post, "/subscriptions.json");
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody, StringComparison.Ordinal);
        Assert.Contains($"\"customer_id\":{MaxioBillingHarness.CustomerId}", subscriptionBody, StringComparison.Ordinal);

        // No card is captured by this API, so no payment fields may be sent.
        Assert.DoesNotContain("credit_card", subscriptionBody, StringComparison.Ordinal);
        Assert.DoesNotContain("payment_profile", subscriptionBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_CollectsByRemittanceOnRelationshipInvoicingSites()
    {
        var handler = Route(relationshipInvoicing: true);

        await MaxioBillingHarness.Build(handler).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Contains(
            "\"payment_collection_method\":\"remittance\"",
            handler.BodyOf(HttpMethod.Post, "/subscriptions.json"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_CollectsByInvoiceOnLegacyStatementsSites()
    {
        var handler = Route(relationshipInvoicing: false);

        await MaxioBillingHarness.Build(handler).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Contains(
            "\"payment_collection_method\":\"invoice\"",
            handler.BodyOf(HttpMethod.Post, "/subscriptions.json"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_HonoursAConfiguredCollectionMethod()
    {
        var handler = Route();
        var billing = MaxioBillingHarness.Build(
            handler,
            new Dictionary<string, string?> { ["Maxio:PaymentCollectionMethod"] = "prepaid" });

        await billing.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Contains(
            "\"payment_collection_method\":\"prepaid\"",
            handler.BodyOf(HttpMethod.Post, "/subscriptions.json"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesTheExistingCustomer_InsteadOfCreatingASecondOne()
    {
        var handler = Route(customerLookup: MaxioBillingHarness.Customer(), customerLookupStatus: HttpStatusCode.OK);

        var result = await MaxioBillingHarness.Build(handler).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingSubscription_AndWritesNothing_WhenAlreadySubscribed()
    {
        var handler = Route(
            customerLookup: MaxioBillingHarness.Customer(),
            customerLookupStatus: HttpStatusCode.OK,
            customerSubscriptions: MaxioBillingHarness.Subscriptions(
                MaxioBillingHarness.Subscription(2002, "active", "eshop-pro", "Pro Plan", 29900)));

        var result = await MaxioBillingHarness.Build(handler).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(2002, result.Subscription.Id);

        // The whole point: a repeated subscribe must not write to Maxio at all.
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_IgnoresSubscriptionsThatAreNoLongerLive()
    {
        var handler = Route(
            customerLookup: MaxioBillingHarness.Customer(),
            customerLookupStatus: HttpStatusCode.OK,
            customerSubscriptions: MaxioBillingHarness.Subscriptions(
                MaxioBillingHarness.Subscription(3003, "canceled", "eshop-pro", "Pro Plan", 29900)));

        var result = await MaxioBillingHarness.Build(handler).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_IgnoresALiveSubscriptionToADifferentPlan()
    {
        var handler = Route(
            customerLookup: MaxioBillingHarness.Customer(),
            customerLookupStatus: HttpStatusCode.OK,
            customerSubscriptions: MaxioBillingHarness.Subscriptions(
                MaxioBillingHarness.Subscription(4004, "active", "basic-plan", "Basic Plan", 2900)));

        var result = await MaxioBillingHarness.Build(handler).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.Created);
    }

    [Fact]
    public async Task SubscribeAsync_DeliversTheWriteOnlyOnce_WhenTheConnectionFails()
    {
        // A transport failure is retried by the SDK on every verb, POST included. Without the write-once
        // guard this is where a shopper gets enrolled twice.
        var handler = Route(createSubscriptionOverride: _ => throw new HttpRequestException("connection reset"));
        var billing = MaxioBillingHarness.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingException>(() => billing.SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(BillingFailureKind.OutcomeUnknown, exception.Kind);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_ReconcilesAnUnknownOutcome_WhenTheWriteActuallyLanded()
    {
        var landed = false;
        var handler = new StubMaxioHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("/site.json", StringComparison.Ordinal))
            {
                return MaxioBillingHarness.Json(HttpStatusCode.OK, MaxioBillingHarness.Site());
            }

            if (path.EndsWith("/products.json", StringComparison.Ordinal))
            {
                return MaxioBillingHarness.Json(HttpStatusCode.OK, MaxioBillingHarness.Products(
                    MaxioBillingHarness.Product("eshop-pro", "Pro Plan", 29900)));
            }

            if (path.Contains("product_families", StringComparison.Ordinal))
            {
                return MaxioBillingHarness.Json(HttpStatusCode.OK, MaxioBillingHarness.ProductFamilies());
            }

            if (path.Contains("/customers/lookup.json", StringComparison.Ordinal))
            {
                return MaxioBillingHarness.Json(HttpStatusCode.OK, MaxioBillingHarness.Customer());
            }

            if (path.Contains("/subscriptions.json", StringComparison.Ordinal) && path.Contains("/customers/", StringComparison.Ordinal))
            {
                return MaxioBillingHarness.Json(
                    HttpStatusCode.OK,
                    landed
                        ? MaxioBillingHarness.Subscriptions(MaxioBillingHarness.Subscription(5005, "active", "eshop-pro", "Pro Plan", 29900))
                        : "[]");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                // The server received and applied the write; the answer never made it back.
                landed = true;
                throw new HttpRequestException("connection reset after the request was accepted");
            }

            return MaxioBillingHarness.Json(HttpStatusCode.InternalServerError, "{}");
        });

        var result = await MaxioBillingHarness.Build(handler).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(5005, result.Subscription.Id);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_SurfacesMaxioValidationMessages()
    {
        var handler = Route(
            createSubscriptionStatus: HttpStatusCode.UnprocessableEntity,
            createSubscription: "{\"errors\":[\"No payment method was on file for the $299.00 balance\"]}");

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => MaxioBillingHarness.Build(handler).SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Rejected, exception.Kind);
        Assert.Equal(422, exception.ProviderStatusCode);
        Assert.Equal("No payment method was on file for the $299.00 balance", Assert.Single(exception.ProviderMessages));
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAnUnknownPlan_WithoutWritingAnything()
    {
        var handler = Route();

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => MaxioBillingHarness.Build(handler).SubscribeAsync(Subscriber, "no-such-plan"));

        Assert.Equal(BillingFailureKind.NotFound, exception.Kind);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAPlanThatRequiresACard_WithoutWritingAnything()
    {
        var handler = Route(products: MaxioBillingHarness.Products(
            MaxioBillingHarness.Product("card-required", "Card required", 29900, requireCreditCard: true)));

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => MaxioBillingHarness.Build(handler).SubscribeAsync(Subscriber, "card-required"));

        Assert.Equal(BillingFailureKind.InvalidRequest, exception.Kind);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_FailsAsAConfigurationProblem_WhenTheApiKeyIsMissing()
    {
        var handler = Route();
        var billing = MaxioBillingHarness.Build(handler, new Dictionary<string, string?> { ["Maxio:ApiKey"] = null });

        var exception = await Assert.ThrowsAsync<BillingException>(() => billing.SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Configuration, exception.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetPlansAsync_FailsAsAConfigurationProblem_WhenTheProductFamilyDoesNotExist()
    {
        var handler = Route();
        var billing = MaxioBillingHarness.Build(
            handler,
            new Dictionary<string, string?> { ["Maxio:ProductFamilyHandle"] = "not-on-this-site" });

        var exception = await Assert.ThrowsAsync<BillingException>(() => billing.GetPlansAsync());

        Assert.Equal(BillingFailureKind.Configuration, exception.Kind);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsNothing_AndCreatesNoCustomer_WhenTheShopperHasNeverSubscribed()
    {
        var handler = Route();

        var subscriptions = await MaxioBillingHarness.Build(handler).GetSubscriptionsAsync(Subscriber);

        Assert.Empty(subscriptions);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEveryStateAndNewestFirst()
    {
        var handler = Route(
            customerLookup: MaxioBillingHarness.Customer(),
            customerLookupStatus: HttpStatusCode.OK,
            customerSubscriptions: MaxioBillingHarness.Subscriptions(
                MaxioBillingHarness.Subscription(6006, "canceled", "basic-plan", "Basic Plan", 2900),
                MaxioBillingHarness.Subscription(6007, "active", "eshop-pro", "Pro Plan", 29900)));

        var subscriptions = await MaxioBillingHarness.Build(handler).GetSubscriptionsAsync(Subscriber);

        Assert.Equal(2, subscriptions.Count);
        Assert.Contains(subscriptions, subscription => subscription.State == "canceled");
        Assert.Contains(subscriptions, subscription => subscription.State == "active");
    }

    [Fact]
    public async Task GetPlansAsync_ReportsAProviderOutageAsUnavailable()
    {
        var handler = new StubMaxioHandler(_ => throw new HttpRequestException("no route to host"));

        var exception = await Assert.ThrowsAsync<BillingException>(() => MaxioBillingHarness.Build(handler).GetPlansAsync());

        Assert.Equal(BillingFailureKind.Unavailable, exception.Kind);
    }

    [Fact]
    public async Task GetPlansAsync_ReportsBadCredentialsAsAConfigurationProblem()
    {
        // A 401 is never the caller's fault, so it must not surface as a client error.
        var handler = new StubMaxioHandler(_ => MaxioBillingHarness.Json(HttpStatusCode.Unauthorized, "{\"errors\":[\"Unauthorized\"]}"));

        var exception = await Assert.ThrowsAsync<BillingException>(() => MaxioBillingHarness.Build(handler).GetPlansAsync());

        Assert.Equal(BillingFailureKind.Configuration, exception.Kind);
        Assert.Equal(401, exception.ProviderStatusCode);
    }
}
