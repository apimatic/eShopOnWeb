using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string CustomerLookup = "/customers/lookup.json";
    private const string Customers = "/customers.json";
    private const string SubscriptionLookup = "/subscriptions/lookup.json";
    private const string Subscriptions = "/subscriptions.json";

    private static readonly SubscriberIdentity Shopper = new("user-1", "shopper@example.com");

    private const string CustomerJson = """{"customer":{"id":501,"reference":"eshop-user-1","email":"shopper@example.com"}}""";

    /// <summary>A customer that does not exist until the first customer create reaches the fake.</summary>
    private static void NewCustomer(MaxioTestHarness h) => h.Maxio
        .On(HttpMethod.Get, CustomerLookup, (_, _) => h.Maxio.Count(HttpMethod.Post, Customers) == 0
            ? FakeMaxio.Json(HttpStatusCode.NotFound, "")
            : FakeMaxio.Json(HttpStatusCode.OK, CustomerJson))
        .On(HttpMethod.Post, Customers, HttpStatusCode.Created, CustomerJson)
        .On(HttpMethod.Get, Subscriptions, HttpStatusCode.OK, "[]");

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionWithBodyRootTokensAndCollectionMethod()
    {
        using var h = new MaxioTestHarness();
        NewCustomer(h);
        h.Maxio
            .On(HttpMethod.Get, SubscriptionLookup, HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, Subscriptions, (request, _) => FakeMaxio.Json(HttpStatusCode.Created,
                MaxioTestHarness.SubscriptionJson(9001, "active", "eshop-user-1-pro-1")));

        var result = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(9001, result.Subscription!.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.NotNull(result.Subscription.NextBillingAt);

        var claim = await h.NewDb().SubscriptionSignups.SingleAsync();
        Assert.Equal(SubscriptionSignupStatus.Completed, claim.Status);

        var customerBody = Root(Assert.Single(h.Maxio.BodiesOf(HttpMethod.Post, Customers)));
        Assert.Equal(claim.CustomerUniquenessToken, customerBody.GetProperty("uniqueness_token").GetString());
        Assert.Equal("eshop-user-1", customerBody.GetProperty("customer").GetProperty("reference").GetString());
        Assert.False(customerBody.GetProperty("customer").TryGetProperty("uniqueness_token", out _));

        var subscriptionBody = Root(Assert.Single(h.Maxio.BodiesOf(HttpMethod.Post, Subscriptions)));
        Assert.Equal(claim.SubscriptionUniquenessToken, subscriptionBody.GetProperty("uniqueness_token").GetString());
        Assert.NotEqual(claim.CustomerUniquenessToken, claim.SubscriptionUniquenessToken);
        var subscription = subscriptionBody.GetProperty("subscription");
        Assert.False(subscription.TryGetProperty("uniqueness_token", out _));
        Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());
        Assert.Equal("pro", subscription.GetProperty("product_handle").GetString());
        Assert.Equal("eshop-user-1", subscription.GetProperty("customer_reference").GetString());
        Assert.Equal(claim.SubscriptionReference, subscription.GetProperty("reference").GetString());
        Assert.False(subscription.TryGetProperty("customer_attributes", out _));
    }

    [Fact]
    public async Task SecondSubscribeReturnsExistingSubscriptionWithoutAnotherCreate()
    {
        using var h = new MaxioTestHarness();
        NewCustomer(h);
        h.Maxio
            .On(HttpMethod.Get, SubscriptionLookup, HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, Subscriptions, HttpStatusCode.Created,
                MaxioTestHarness.SubscriptionJson(9001, "active", "eshop-user-1-pro-1"))
            .On(HttpMethod.Get, "/subscriptions/9001.json", HttpStatusCode.OK,
                MaxioTestHarness.SubscriptionJson(9001, "active", "eshop-user-1-pro-1"));

        await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);
        var second = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, Subscriptions));
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, Customers));
    }

    [Fact]
    public async Task UnknownPlanIsRefusedBeforeAnyWrite()
    {
        using var h = new MaxioTestHarness();

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => h.NewService().SubscribeAsync(Shopper, "not-a-plan"));

        Assert.Equal(SubscriptionBillingError.UnknownPlan, ex.Error);
        Assert.DoesNotContain(h.Maxio.Requests, r => r.Method == HttpMethod.Post);
        Assert.Empty(h.NewDb().SubscriptionSignups);
    }

    [Fact]
    public async Task CustomerWhoAlreadyHoldsThePlanGetsNoSecondSubscription()
    {
        using var h = new MaxioTestHarness();
        h.Maxio
            .On(HttpMethod.Get, CustomerLookup, HttpStatusCode.OK, """{"customer":{"id":501,"reference":"eshop-user-1"}}""")
            .On(HttpMethod.Get, Subscriptions, HttpStatusCode.OK,
                "[" + MaxioTestHarness.SubscriptionJson(7001, "on_hold", "created-elsewhere") + "]");

        var result = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(7001, result.Subscription!.Id);
        Assert.Equal(0, h.Maxio.Count(HttpMethod.Post, Subscriptions));
        Assert.Contains(h.Maxio.Requests, r => r.PathAndQuery.Contains("customer_id=501"));
    }

    [Fact]
    public async Task DroppedConnectionOnCreateIsSettledByReadingBackTheReference()
    {
        using var h = new MaxioTestHarness();
        NewCustomer(h);
        h.Maxio
            .On(HttpMethod.Post, Subscriptions, (_, _) => throw new HttpRequestException("connection reset"))
            // First lookup (before the create): not there. Second (the read-back): the create had landed.
            .On(HttpMethod.Get, SubscriptionLookup, (_, call) => call == 1
                ? FakeMaxio.Json(HttpStatusCode.NotFound, "")
                : FakeMaxio.Json(HttpStatusCode.OK, MaxioTestHarness.SubscriptionJson(9002, "active", "eshop-user-1-pro-1")));

        var result = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(9002, result.Subscription!.Id);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, Subscriptions)); // never resent
    }

    [Fact]
    public async Task UnsettledCreateIsPendingAndIsResentWithTheSameTokenOnlyAfter120Seconds()
    {
        using var h = new MaxioTestHarness();
        NewCustomer(h);
        h.Maxio
            .On(HttpMethod.Get, SubscriptionLookup, HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, Subscriptions, (_, call) => call == 1
                ? throw new HttpRequestException("connection reset")
                : FakeMaxio.Json(HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson(9003, "active", "eshop-user-1-pro-1")));

        var first = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);
        Assert.Equal(SubscribeOutcome.Pending, first.Outcome);
        Assert.Equal(SubscriptionSignupStatus.Unknown, (await h.NewDb().SubscriptionSignups.SingleAsync()).Status);

        // Within Maxio's 120 s window: read back only, no resend.
        h.Time.Advance(TimeSpan.FromSeconds(30));
        var early = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);
        Assert.Equal(SubscribeOutcome.Pending, early.Outcome);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, Subscriptions));

        h.Time.Advance(TimeSpan.FromSeconds(100));
        var settled = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);

        Assert.Equal(SubscribeOutcome.Created, settled.Outcome);
        Assert.Equal(first.SignupId, settled.SignupId);
        var tokens = h.Maxio.BodiesOf(HttpMethod.Post, Subscriptions)
            .Select(b => Root(b).GetProperty("uniqueness_token").GetString()).ToList();
        Assert.Equal(2, tokens.Count);
        Assert.Equal(tokens[0], tokens[1]);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, Customers));
    }

    [Fact]
    public async Task ConflictWithNothingFoundAfter120SecondsStartsAgainWithANewToken()
    {
        using var h = new MaxioTestHarness();
        NewCustomer(h);
        h.Maxio
            .On(HttpMethod.Get, SubscriptionLookup, HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, Subscriptions, (_, call) => call == 1
                ? FakeMaxio.Json(HttpStatusCode.Conflict, """{"errors":["DuplicatePrevention::DuplicateSubmissionError"]}""")
                : FakeMaxio.Json(HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson(9004, "active", "eshop-user-1-pro-1")));

        var first = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);
        Assert.Equal(SubscribeOutcome.Pending, first.Outcome);
        Assert.True((await h.NewDb().SubscriptionSignups.SingleAsync()).SubscriptionCreateConflicted);

        h.Time.Advance(TimeSpan.FromSeconds(121));
        var settled = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);

        Assert.Equal(SubscribeOutcome.Created, settled.Outcome);
        var tokens = h.Maxio.BodiesOf(HttpMethod.Post, Subscriptions)
            .Select(b => Root(b).GetProperty("uniqueness_token").GetString()).ToList();
        Assert.Equal(2, tokens.Count);
        Assert.NotEqual(tokens[0], tokens[1]);
    }

    [Fact]
    public async Task RejectedSignupClosesTheClaimSoTheNextAttemptUsesNewTokens()
    {
        using var h = new MaxioTestHarness();
        NewCustomer(h);
        h.Maxio
            .On(HttpMethod.Get, SubscriptionLookup, HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, Subscriptions, HttpStatusCode.UnprocessableEntity,
                """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan));

        Assert.Equal(SubscriptionBillingError.Rejected, ex.Error);
        Assert.DoesNotContain("payment method", ex.Message, StringComparison.OrdinalIgnoreCase); // Maxio's text stays in our logs
        Assert.Equal(SubscriptionSignupStatus.Rejected, (await h.NewDb().SubscriptionSignups.SingleAsync()).Status);
    }

    [Fact]
    public async Task ActionLinkOn422IsReturnedAsActionRequired()
    {
        using var h = new MaxioTestHarness();
        NewCustomer(h);
        h.Maxio
            .On(HttpMethod.Get, SubscriptionLookup, HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, Subscriptions, HttpStatusCode.UnprocessableEntity,
                """{"errors":["Authentication required"],"action_link":"https://example.test/3ds"}""");

        var result = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);

        Assert.Equal(SubscribeOutcome.ActionRequired, result.Outcome);
        Assert.Equal("https://example.test/3ds", result.ActionLink);
    }

    [Fact]
    public async Task SignupHeldByAnotherWorkerIsRefusedAsInProgress()
    {
        using var h = new MaxioTestHarness();
        await using (var db = h.NewDb())
        {
            // A claim another request has just made (its constructor takes the lease).
            db.SubscriptionSignups.Add(new SubscriptionSignup(Shopper.UserId, Shopper.Email, MaxioTestHarness.Plan, 1, h.Time.Now));
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan));

        Assert.Equal(SubscriptionBillingError.SignupInProgress, ex.Error);
        Assert.DoesNotContain(h.Maxio.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task StoreRefusesASecondClaimWithTheSameKey()
    {
        using var h = new MaxioTestHarness();
        await using (var first = h.NewDb())
        {
            first.SubscriptionSignups.Add(new SubscriptionSignup("u", "u@example.com", "pro", 1, h.Time.Now));
            await first.SaveChangesAsync();
        }

        await using var second = h.NewDb();
        second.SubscriptionSignups.Add(new SubscriptionSignup("u", "u@example.com", "pro", 1, h.Time.Now));

        // The service treats exactly this as "already claimed" (MaxioSubscriptionService.IsDuplicateKey).
        var ex = await Assert.ThrowsAnyAsync<ArgumentException>(() => second.SaveChangesAsync());
        Assert.Contains("same key", ex.Message);
    }

    [Fact]
    public async Task DroppedConnectionOnCustomerCreateIsReadBackByReference()
    {
        using var h = new MaxioTestHarness();
        h.Maxio
            .On(HttpMethod.Get, CustomerLookup, (_, call) => call == 1
                ? FakeMaxio.Json(HttpStatusCode.NotFound, "")
                : FakeMaxio.Json(HttpStatusCode.OK, """{"customer":{"id":502,"reference":"eshop-user-1"}}"""))
            .On(HttpMethod.Post, Customers, (_, _) => throw new HttpRequestException("connection reset"))
            .On(HttpMethod.Get, Subscriptions, HttpStatusCode.OK, "[]")
            .On(HttpMethod.Get, SubscriptionLookup, HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, Subscriptions, HttpStatusCode.Created,
                MaxioTestHarness.SubscriptionJson(9005, "active", "eshop-user-1-pro-1"));

        var result = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, Customers));
    }

    [Fact]
    public async Task ThrottledReadIsNotRetriedAndPausesLaterCalls()
    {
        using var h = new MaxioTestHarness();
        h.Maxio.On(HttpMethod.Get, "/site.json", HttpStatusCode.TooManyRequests, "{}");

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => h.NewService().ListPlansAsync());
        Assert.Equal(SubscriptionBillingError.Throttled, ex.Error);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Get, "/site.json"));

        var paused = await Assert.ThrowsAsync<SubscriptionBillingException>(() => h.NewService().ListPlansAsync());
        Assert.Equal(SubscriptionBillingError.Throttled, paused.Error);
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Get, "/site.json")); // no request while paused
    }

    [Fact]
    public async Task PlanListCutShortByThePageCeilingSaysSo()
    {
        using var h = new MaxioTestHarness();
        var id = 100;
        h.Maxio.On(HttpMethod.Get, $"/product_families/handle:{MaxioTestHarness.Family}/products.json", (_, _) =>
            FakeMaxio.Json(HttpStatusCode.OK, $$$"""[{"product":{"id":{{{Interlocked.Increment(ref id)}}},"handle":"p{{{id}}}","name":"P","price_in_cents":100}}]"""));

        var plans = await h.NewService().ListPlansAsync();

        Assert.False(plans.IsComplete);
        Assert.Equal(MaxioGateway.MaxPages, plans.Plans.Count);
    }

    [Fact]
    public async Task PlansAreReadToAnEmptyPageWithPerPage200()
    {
        using var h = new MaxioTestHarness();

        var plans = await h.NewService().ListPlansAsync();

        Assert.True(plans.IsComplete);
        var plan = Assert.Single(plans.Plans);
        Assert.Equal("pro", plan.Handle);
        Assert.Equal(299m, plan.Price);
        Assert.Equal("USD", plan.Currency);
        Assert.Contains(h.Maxio.Requests, r => r.PathAndQuery.Contains("page=2") && r.PathAndQuery.Contains("per_page=200"));
    }

    [Fact]
    public async Task MySubscriptionsIsEmptyForAShopperWithNoBillingCustomer()
    {
        using var h = new MaxioTestHarness();
        h.Maxio.On(HttpMethod.Get, CustomerLookup, HttpStatusCode.NotFound, "");

        var list = await h.NewService().ListSubscriptionsAsync(Shopper);

        Assert.Empty(list.Subscriptions);
        Assert.True(list.IsComplete);
    }

    [Fact]
    public async Task BaseUrlOverrideIsUsedVerbatim()
    {
        using var h = new MaxioTestHarness();

        await h.NewService().ListPlansAsync();

        Assert.NotEmpty(h.Maxio.Hosts);
        Assert.All(h.Maxio.Hosts, host => Assert.Equal("maxio.test", host));
    }

    [Fact]
    public async Task ReconcilerSettlesAnUnknownSignupWithoutTheShopperRetrying()
    {
        using var h = new MaxioTestHarness();
        NewCustomer(h);
        h.Maxio
            .On(HttpMethod.Get, SubscriptionLookup, HttpStatusCode.NotFound, "")
            .On(HttpMethod.Post, Subscriptions, (_, call) => call == 1
                ? throw new HttpRequestException("connection reset")
                : FakeMaxio.Json(HttpStatusCode.Created, MaxioTestHarness.SubscriptionJson(9006, "active", "eshop-user-1-pro-1")));
        var pending = await h.NewService().SubscribeAsync(Shopper, MaxioTestHarness.Plan);
        Assert.Equal(SubscribeOutcome.Pending, pending.Outcome);

        var services = new ServiceCollection();
        services.AddScoped(_ => h.NewDb());
        services.AddScoped<ISubscriptionService>(sp => h.NewService(sp.GetRequiredService<CatalogContext>()));
        await using var provider = services.BuildServiceProvider();
        var reconciler = new SubscriptionSignupReconciler(provider.GetRequiredService<IServiceScopeFactory>(), h.Time,
            NullLogger<SubscriptionSignupReconciler>.Instance);

        await reconciler.ReconcileOnceAsync(CancellationToken.None); // too early: nothing is resent
        Assert.Equal(1, h.Maxio.Count(HttpMethod.Post, Subscriptions));

        h.Time.Advance(TimeSpan.FromSeconds(125));
        await reconciler.ReconcileOnceAsync(CancellationToken.None);

        var claim = await h.NewDb().SubscriptionSignups.SingleAsync();
        Assert.Equal(SubscriptionSignupStatus.Completed, claim.Status);
        Assert.Equal(9006, claim.BillingSubscriptionId);
        var tokens = h.Maxio.BodiesOf(HttpMethod.Post, Subscriptions)
            .Select(b => Root(b).GetProperty("uniqueness_token").GetString()).Distinct();
        Assert.Single(tokens);
    }
}
