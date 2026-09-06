using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private static readonly SubscriberIdentity Subscriber =
        new("demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public async Task GetPlansAsync_ResolvesTheFamilyByHandleAndProjectsThePlans()
    {
        var (service, transport) = MaxioTestHost.Create(t => t.WithCatalog());

        var plans = await service.GetPlansAsync();

        // Cheapest first.
        Assert.Equal(new[] { "basic-plan", "pro-plan" }, plans.Select(p => p.Handle));

        var pro = plans.Single(p => p.Handle == "pro-plan");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresCreditCard);

        // No Maxio operation takes a family handle, so the products call must go out against the
        // numeric id resolved from the family listing.
        Assert.Equal(1, transport.CountOf(HttpMethod.Get, "/product_families.json"));
        Assert.Equal(1, transport.CountOf(HttpMethod.Get, $"/product_families/{MaxioTestHost.FamilyId}/products.json"));
    }

    [Fact]
    public async Task GetPlansAsync_ResolvesTheFamilyOnlyOnceAcrossCalls()
    {
        var (service, transport) = MaxioTestHost.Create(t => t.WithCatalog());

        await service.GetPlansAsync();
        await service.GetPlansAsync();

        Assert.Equal(1, transport.CountOf(HttpMethod.Get, "/product_families.json"));
        Assert.Equal(2, transport.CountOf(HttpMethod.Get, "/products.json"));
    }

    [Fact]
    public async Task GetPlansAsync_ReportsAMissingFamilyAsNotFound()
    {
        var (service, _) = MaxioTestHost.Create(t =>
            t.OnGet("/product_families.json", """[{"product_family": {"id": 9, "handle": "some-other-family"}}]"""));

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.GetPlansAsync());

        Assert.Equal(BillingFailureKind.NotFound, exception.Kind);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesTheCustomerAndTheSubscriptionOnAFirstSubscribe()
    {
        var (service, transport) = MaxioTestHost.Create(t =>
        {
            t.WithCatalog();
            // Absent first, then present once created.
            t.OnGetSequence("/customers/lookup.json",
                (HttpStatusCode.NotFound, """{"errors": ["Customer not found"]}"""),
                (HttpStatusCode.OK, MaxioTestHost.CustomerJson(MaxioTestHost.CustomerId, "ref")));
            t.OnPost("/customers.json", MaxioTestHost.CustomerJson(MaxioTestHost.CustomerId, "ref"));
            t.OnGet($"/customers/{MaxioTestHost.CustomerId}/subscriptions.json", "[]");
            t.OnPost("/subscriptions.json", MaxioTestHost.SubscriptionJson(500, "pro-plan", "active"));
        });

        var result = await service.SubscribeAsync(Subscriber, "pro-plan");

        Assert.False(result.AlreadySubscribed);
        Assert.True(result.CustomerCreated);
        Assert.Equal(500, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal("pro-plan", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);

        // Next billing comes from current_period_ends_at — Maxio returns no next_billing_at on a
        // subscription, so reading the wrong field would silently produce a null date.
        Assert.Equal("2026-02-01T00:00:00.0000000+00:00", result.Subscription.NextBillingAt!.Value.ToString("O"));
    }

    [Fact]
    public async Task SubscribeAsync_SendsTheCustomerIdPlanHandleAndCollectionMethodAndNoPaymentDetails()
    {
        var (service, transport) = MaxioTestHost.Create(t =>
        {
            t.WithCatalog();
            t.OnGet("/customers/lookup.json", MaxioTestHost.CustomerJson(MaxioTestHost.CustomerId, "ref"));
            t.OnGet($"/customers/{MaxioTestHost.CustomerId}/subscriptions.json", "[]");
            t.OnPost("/subscriptions.json", MaxioTestHost.SubscriptionJson(500, "pro-plan", "active"));
        });

        await service.SubscribeAsync(Subscriber, "pro-plan");

        var body = transport.LastBodyFor(HttpMethod.Post, "/subscriptions.json");
        Assert.NotNull(body);
        Assert.Contains("\"product_handle\":\"pro-plan\"", body);
        Assert.Contains($"\"customer_id\":{MaxioTestHost.CustomerId}", body);

        // Without a collection method Maxio tries to charge the first period at signup and rejects the
        // enrolment, even for a plan that does not require a credit card.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);

        // Nothing about payment details is sent — this integration captures no card.
        Assert.DoesNotContain("credit_card_attributes", body);
        Assert.DoesNotContain("payment_profile", body);

        // The customer already existed, so it must not be recreated.
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        var (service, transport) = MaxioTestHost.Create(t =>
        {
            t.WithCatalog();
            t.OnGet("/customers/lookup.json", MaxioTestHost.CustomerJson(MaxioTestHost.CustomerId, "ref"));
            t.OnGet($"/customers/{MaxioTestHost.CustomerId}/subscriptions.json",
                $"[{MaxioTestHost.SubscriptionJson(500, "pro-plan", "active")}]");
        });

        var result = await service.SubscribeAsync(Subscriber, "pro-plan");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(500, result.Subscription.Id);

        // The point of the whole flow: a repeated click creates nothing.
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_EnrollsAgainWhenTheOnlyPriorSubscriptionIsCancelled()
    {
        var (service, transport) = MaxioTestHost.Create(t =>
        {
            t.WithCatalog();
            t.OnGet("/customers/lookup.json", MaxioTestHost.CustomerJson(MaxioTestHost.CustomerId, "ref"));
            t.OnGet($"/customers/{MaxioTestHost.CustomerId}/subscriptions.json",
                $"[{MaxioTestHost.SubscriptionJson(500, "pro-plan", "canceled")}]");
            t.OnPost("/subscriptions.json", MaxioTestHost.SubscriptionJson(501, "pro-plan", "active"));
        });

        var result = await service.SubscribeAsync(Subscriber, "pro-plan");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(501, result.Subscription.Id);
        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAPlanOutsideTheConfiguredFamilyWithoutCallingMaxioAtAll()
    {
        var (service, transport) = MaxioTestHost.Create(t => t.WithCatalog());

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Subscriber, "not-a-plan"));

        Assert.Equal(BillingFailureKind.NotFound, exception.Kind);
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_SurfacesMaxioValidationMessagesVerbatim()
    {
        var (service, _) = MaxioTestHost.Create(t =>
        {
            t.WithCatalog();
            t.OnGet("/customers/lookup.json", MaxioTestHost.CustomerJson(MaxioTestHost.CustomerId, "ref"));
            t.OnGet($"/customers/{MaxioTestHost.CustomerId}/subscriptions.json", "[]");
            t.OnPost("/subscriptions.json",
                """{"errors": ["No payment method was on file for the $299.00 balance"]}""",
                HttpStatusCode.UnprocessableEntity);
        });

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Subscriber, "pro-plan"));

        // A rejection, not an outage: retrying it unchanged could never succeed.
        Assert.Equal(BillingFailureKind.Rejected, exception.Kind);
        Assert.Equal("No payment method was on file for the $299.00 balance", Assert.Single(exception.ProviderMessages));
    }

    [Fact]
    public async Task SubscribeAsync_ReportsRejectedCredentialsDistinctlyFromAnOutage()
    {
        var (service, _) = MaxioTestHost.Create(t =>
            t.OnGet("/product_families.json", """{"error": "unauthorized"}""", HttpStatusCode.Unauthorized));

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Subscriber, "pro-plan"));

        Assert.Equal(BillingFailureKind.Unauthenticated, exception.Kind);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotEnrollTwiceWhenTheConnectionFailsMidWrite()
    {
        // The SDK retries an HttpRequestException on every verb and that trigger cannot be disabled, so
        // without the write guard this POST would be resent and could enrol the customer twice. A reset
        // thrown after the bytes reached Maxio is indistinguishable from one thrown before.
        var (service, transport) = MaxioTestHost.Create(t =>
        {
            t.WithCatalog();
            t.OnGet("/customers/lookup.json", MaxioTestHost.CustomerJson(MaxioTestHost.CustomerId, "ref"));
            t.OnGet($"/customers/{MaxioTestHost.CustomerId}/subscriptions.json", "[]");
            t.OnPostThrows("/subscriptions.json", () => new HttpRequestException("connection reset"));
        });

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Subscriber, "pro-plan"));

        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));

        // The request left this process exactly once, so it may still have taken effect. Reporting it as
        // a plain failure would invite a caller to retry a write that might already have landed.
        Assert.Equal(BillingFailureKind.UnknownOutcome, exception.Kind);
    }

    [Fact]
    public async Task ReadsAreStillRetriedWhenTheConnectionFails()
    {
        // The guard is scoped to writes. If it leaked onto reads it would turn every transient blip into
        // a hard failure, so this asserts the retry really does still happen there.
        var (service, transport) = MaxioTestHost.Create(t => t.OnGetThrows("/product_families.json"));

        await Assert.ThrowsAsync<BillingException>(() => service.GetPlansAsync());

        Assert.True(transport.CountOf(HttpMethod.Get, "/product_families.json") > 1);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmptyForSomeoneWhoHasNeverSubscribed()
    {
        var (service, transport) = MaxioTestHost.Create(t =>
        {
            t.WithCatalog();
            t.OnGet("/customers/lookup.json", """{"errors": ["Customer not found"]}""", HttpStatusCode.NotFound);
        });

        var subscriptions = await service.GetSubscriptionsAsync(Subscriber);

        Assert.Empty(subscriptions);

        // No billing customer is a normal state, not a failure — and nothing may be created to fix it.
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ProjectsPlanAndBillingDates()
    {
        var (service, _) = MaxioTestHost.Create(t =>
        {
            t.WithCatalog();
            t.OnGet("/customers/lookup.json", MaxioTestHost.CustomerJson(MaxioTestHost.CustomerId, "ref"));
            t.OnGet($"/customers/{MaxioTestHost.CustomerId}/subscriptions.json",
                $"[{MaxioTestHost.SubscriptionJson(500, "pro-plan", "active")}]");
        });

        var subscription = Assert.Single(await service.GetSubscriptionsAsync(Subscriber));

        Assert.Equal(500, subscription.Id);
        Assert.Equal("pro-plan", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal("active", subscription.State);
        Assert.True(subscription.IsLive);
        Assert.Equal("remittance", subscription.PaymentCollectionMethod);
        Assert.NotNull(subscription.NextBillingAt);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_BackfillsThePlanWhenMaxioOmitsTheNestedProduct()
    {
        // ListCustomerSubscriptions has no include parameter to ask for the product, so a row without
        // one must still be reported rather than dropped or resolved with a read per subscription.
        var (service, transport) = MaxioTestHost.Create(t =>
        {
            t.WithCatalog();
            t.OnGet("/customers/lookup.json", MaxioTestHost.CustomerJson(MaxioTestHost.CustomerId, "ref"));
            t.OnGet($"/customers/{MaxioTestHost.CustomerId}/subscriptions.json",
                """[{"subscription": {"id": 500, "state": "active", "created_at": "2026-01-01T00:00:00Z"}}]""");
        });

        var subscription = Assert.Single(await service.GetSubscriptionsAsync(Subscriber));

        Assert.Equal(500, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(0, transport.CountOf(HttpMethod.Get, "/products/handle/"));
    }

    [Fact]
    public async Task IsNotConfigured_WhenTheApiKeyIsMissing()
    {
        var (service, transport) = MaxioTestHost.Create(
            t => t.WithCatalog(),
            settings => settings.ApiKey = null);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.GetPlansAsync());

        Assert.Equal(BillingFailureKind.NotConfigured, exception.Kind);
        Assert.Empty(transport.Requests);
    }
}
