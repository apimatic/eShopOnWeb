using System.Diagnostics;
using System.Net;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string Buyer = "shopper@example.com";
    private static readonly string CustomerReference = MaxioSubscriptionBillingService.CustomerReferenceFor(Buyer);

    private readonly FakeMaxioHandler _maxio = new();
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly ManualTimeProvider _time = new();

    private MaxioSubscriptionBillingService CreateService(MaxioBillingTimeouts? timeouts = null)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "offline-test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family",
            BaseUrl = "https://maxio.test"
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(_maxio),
            MaxioServiceCollectionExtensions.CreateClientOptions(settings, NullLoggerFactory.Instance));

        return new MaxioSubscriptionBillingService(
            client,
            new SubscriptionEnrollmentStore(NewContext()),
            Options.Create(settings),
            timeouts ?? new MaxioBillingTimeouts(),
            new MemoryCache(new MemoryCacheOptions()),
            _time,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    private async Task<SubscriptionEnrollment?> StoredEnrollmentAsync()
    {
        await using var db = NewContext();
        return await db.SubscriptionEnrollments.AsNoTracking().SingleOrDefaultAsync();
    }

    private int SubscriptionCreates => _maxio.Count(HttpMethod.Post, "/subscriptions.json");

    // ------------------------------------------------------------------ plans

    [Fact]
    public async Task ListPlans_ReadsConfiguredFamilyByHandle_AndSkipsArchivedProducts()
    {
        var catalog = await CreateService().ListPlansAsync();

        Assert.Equal("test-family", catalog.ProductFamilyHandle);
        Assert.False(catalog.Truncated);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        var pro = catalog.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);

        var request = Assert.Single(_maxio.Requests);
        Assert.Equal("/product_families/handle:test-family/products.json", request.Path);
        Assert.Equal("maxio.test", request.Uri.Host);
        Assert.Contains("per_page=200", request.Uri.Query);
    }

    [Fact]
    public async Task ListPlans_WhenFamilyExceedsPageCap_ReturnsPartialListMarkedTruncated()
    {
        _maxio.Products.Clear();
        for (var i = 0; i < MaxioSubscriptionBillingService.PlansPageSize * MaxioSubscriptionBillingService.MaxPlanPages + 5; i++)
        {
            _maxio.Products.Add(new FakeProduct(1000 + i, $"plan-{i}", $"Plan {i}", 100));
        }

        var catalog = await CreateService().ListPlansAsync();

        Assert.True(catalog.Truncated);
        Assert.Equal(MaxioSubscriptionBillingService.PlansPageSize * MaxioSubscriptionBillingService.MaxPlanPages, catalog.Plans.Count);
        Assert.Equal(MaxioSubscriptionBillingService.MaxPlanPages, _maxio.Requests.Count);
    }

    [Fact]
    public async Task ListPlans_WhenMaxioRejectsCredentials_ReportsProviderFailure()
    {
        _maxio.Intercept = (_, _, _) => Task.FromResult<HttpResponseMessage?>(FakeMaxioHandler.Json(HttpStatusCode.Unauthorized, "{}"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => CreateService().ListPlansAsync());

        Assert.Equal(401, ex.ProviderStatusCode);
        Assert.Equal("Maxio rejected the configured credentials.", ex.Message);
    }

    [Fact]
    public async Task ListPlans_WhenMaxioHangs_FailsWithinBudget_SayingMaxioDidNotRespond()
    {
        _maxio.Intercept = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        };
        var service = CreateService(new MaxioBillingTimeouts { RequestBudget = TimeSpan.FromMilliseconds(300) });

        var stopwatch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => service.ListPlansAsync());

        Assert.True(ex.TimedOut);
        Assert.Contains("Maxio did not respond", ex.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task ListPlans_WhenMaxioIsUnreachable_RetriesTheReadThenReportsItCouldNotBeReached()
    {
        _maxio.Intercept = (_, _, _) => throw new HttpRequestException("connection refused");

        var ex = await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => CreateService().ListPlansAsync());

        Assert.False(ex.TimedOut);
        Assert.Contains("Maxio could not be reached", ex.Message);
        Assert.Equal(3, _maxio.Requests.Count); // a GET: first attempt + 2 SDK retries
    }

    [Fact]
    public async Task Subscribe_WhenCreateFails_TheSdkNeverResendsThePost()
    {
        _maxio.Intercept = (request, _, _) =>
            request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/subscriptions.json"
                ? Task.FromResult<HttpResponseMessage?>(FakeMaxioHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"))
                : Task.FromResult<HttpResponseMessage?>(null);

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => CreateService().SubscribeAsync(Buyer, "eshop-pro"));

        Assert.Equal(1, SubscriptionCreates);
        Assert.Equal(1, _maxio.Count(HttpMethod.Get, "/subscriptions/lookup.json")); // reconciled instead
    }

    [Fact]
    public void DefaultBudgets_KeepEveryRequestUnderThirtySeconds()
    {
        var defaults = new MaxioBillingTimeouts();

        Assert.True(defaults.RequestBudget + defaults.ReconciliationBudget < TimeSpan.FromSeconds(30));
    }

    // ------------------------------------------------------------------ subscribe: happy path & idempotency

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_AndRecordsTheEnrollment()
    {
        var result = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("Pro Plan", result.Subscription.PlanName);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(DateTimeOffset.Parse("2026-11-06T10:00:00Z"), result.Subscription.NextBillingAt);

        var customerBody = _maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/customers.json").JsonBody!["customer"];
        Assert.Equal(CustomerReference, customerBody.Text("reference"));
        Assert.Equal(Buyer, customerBody.Text("email"));

        var subscriptionBody = _maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json").JsonBody!["subscription"];
        Assert.Equal("eshop-pro", subscriptionBody.Text("product_handle"));
        Assert.Equal("remittance", subscriptionBody.Text("payment_collection_method"));
        Assert.Equal(_maxio.Customers.Single().Id, subscriptionBody!["customer_id"]!.GetValue<int>());
        Assert.StartsWith("eshop-sub-", subscriptionBody.Text("reference"));

        var enrollment = await StoredEnrollmentAsync();
        Assert.NotNull(enrollment);
        Assert.Equal(SubscriptionEnrollmentStatus.Completed, enrollment!.Status);
        Assert.Equal(result.Subscription.SubscriptionId, enrollment.BillingSubscriptionId);
        Assert.Equal(subscriptionBody.Text("reference"), enrollment.SubscriptionReference);
    }

    [Fact]
    public async Task Subscribe_RepeatedForSamePlan_ReturnsExistingSubscription_WithoutSecondCreate()
    {
        var first = await CreateService().SubscribeAsync(Buyer, "eshop-pro");
        var second = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.SubscriptionId, second.Subscription.SubscriptionId);
        Assert.Equal(1, SubscriptionCreates);
        Assert.Single(_maxio.Customers);
    }

    [Fact]
    public async Task Subscribe_DoubleClick_OnlyOneCreateReachesMaxio()
    {
        var createEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCreate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _maxio.Intercept = async (request, _, ct) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/subscriptions.json")
            {
                createEntered.TrySetResult();
                await releaseCreate.Task.WaitAsync(ct);
            }

            return null;
        };

        // Separate services and DbContexts = two concurrent HTTP requests.
        var firstClick = CreateService().SubscribeAsync(Buyer, "eshop-pro");
        await createEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<SubscriptionConflictException>(() => CreateService().SubscribeAsync(Buyer, "eshop-pro"));

        releaseCreate.SetResult();
        var result = await firstClick;

        Assert.True(result.Created);
        Assert.Equal(1, SubscriptionCreates);
        Assert.Single(_maxio.Subscriptions);
    }

    [Fact]
    public async Task Subscribe_ToAnotherPlanWhileSubscribed_IsAConflict()
    {
        await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        var ex = await Assert.ThrowsAsync<SubscriptionConflictException>(() => CreateService().SubscribeAsync(Buyer, "basic-plan"));

        Assert.Contains("eshop-pro", ex.Message);
        Assert.Equal(1, SubscriptionCreates);
    }

    [Theory]
    [InlineData("no-such-plan")]
    [InlineData("old-plan")] // archived
    public async Task Subscribe_ToAPlanNotOffered_IsRejectedBeforeAnyWrite(string planHandle)
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => CreateService().SubscribeAsync(Buyer, planHandle));

        Assert.DoesNotContain(_maxio.Requests, r => r.Method == HttpMethod.Post);
        Assert.Null(await StoredEnrollmentAsync());
    }

    [Fact]
    public async Task Subscribe_ReusesExistingCustomerFoundByReference()
    {
        var existing = _maxio.AddCustomer(CustomerReference, Buyer);

        await CreateService().SubscribeAsync(Buyer, "basic-plan");

        Assert.Equal(0, _maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(existing.Id, _maxio.Subscriptions.Single().CustomerId);
    }

    [Fact]
    public async Task Subscribe_AfterLocalStateWasLost_AdoptsTheLiveMaxioSubscription()
    {
        var customer = _maxio.AddCustomer(CustomerReference, Buyer);
        var live = _maxio.AddSubscription(customer.Id, "eshop-pro", "eshop-sub-from-an-earlier-run");

        var result = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(live.Id, result.Subscription.SubscriptionId);
        Assert.Equal(0, SubscriptionCreates);
        Assert.Equal(SubscriptionEnrollmentStatus.Completed, (await StoredEnrollmentAsync())!.Status);
    }

    [Fact]
    public async Task Subscribe_AfterTheSubscriptionWasCanceled_CreatesANewOne()
    {
        var first = await CreateService().SubscribeAsync(Buyer, "eshop-pro");
        _maxio.Subscriptions.Single().State = "canceled";

        var second = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.True(second.Created);
        Assert.NotEqual(first.Subscription.SubscriptionId, second.Subscription.SubscriptionId);
        Assert.Equal(2, SubscriptionCreates);
    }

    // ------------------------------------------------------------------ subscribe: provider refusals

    [Fact]
    public async Task Subscribe_WhenMaxioRejectsTheSubscription_SurfacesItsErrors_AndReleasesTheClaim()
    {
        _maxio.Intercept = (request, _, _) => Task.FromResult(
            request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/subscriptions.json"
                ? FakeMaxioHandler.Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file for the $299.00 balance"]}""")
                : null);

        var ex = await Assert.ThrowsAsync<BillingRequestRejectedException>(() => CreateService().SubscribeAsync(Buyer, "eshop-pro"));

        Assert.Equal(new[] { "No payment method was on file for the $299.00 balance" }, ex.Errors);
        Assert.Null(await StoredEnrollmentAsync());

        // Nothing is left behind blocking a corrected retry.
        _maxio.Intercept = null;
        Assert.True((await CreateService().SubscribeAsync(Buyer, "eshop-pro")).Created);
    }

    [Fact]
    public async Task Subscribe_WhenCustomerCreateRacesAnotherProcess_UsesTheWinningCustomer()
    {
        // Lookup misses, then the create collides with a customer created concurrently elsewhere (same reference).
        var lookups = 0;
        _maxio.Intercept = (request, _, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/customers/lookup.json" && Interlocked.Increment(ref lookups) == 1)
            {
                _maxio.AddCustomer(CustomerReference, Buyer);
                return Task.FromResult<HttpResponseMessage?>(FakeMaxioHandler.Json(HttpStatusCode.NotFound, "{}"));
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };

        var result = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.True(result.Created);
        Assert.Single(_maxio.Customers);
    }

    // ------------------------------------------------------------------ subscribe: unknown outcomes

    [Fact]
    public async Task Subscribe_WhenCreateConnectionDrops_ButMaxioCreatedIt_ReconcilesByReference()
    {
        _maxio.Intercept = (request, body, _) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/subscriptions.json")
            {
                _maxio.CreateSubscriptionDirectly(body!); // Maxio acted ...
                throw new HttpRequestException("connection reset"); // ... but the reply never arrived
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };

        var result = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(_maxio.Subscriptions.Single().Id, result.Subscription.SubscriptionId);
        Assert.Equal(1, _maxio.Count(HttpMethod.Get, "/subscriptions/lookup.json"));
        Assert.Equal(SubscriptionEnrollmentStatus.Completed, (await StoredEnrollmentAsync())!.Status);
    }

    [Fact]
    public async Task Subscribe_WhenCreateTimesOut_ButMaxioCreatedIt_ReconcilesByReference()
    {
        _maxio.Intercept = async (request, body, ct) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/subscriptions.json")
            {
                _maxio.CreateSubscriptionDirectly(body!);
                await Task.Delay(Timeout.Infinite, ct); // Maxio acted but never answered
            }

            return null;
        };
        var service = CreateService(new MaxioBillingTimeouts
        {
            RequestBudget = TimeSpan.FromMilliseconds(500),
            ReconciliationBudget = TimeSpan.FromSeconds(5)
        });

        var result = await service.SubscribeAsync(Buyer, "eshop-pro");

        Assert.True(result.Created);
        Assert.Single(_maxio.Subscriptions);
        Assert.Equal(SubscriptionEnrollmentStatus.Completed, (await StoredEnrollmentAsync())!.Status);
    }

    [Fact]
    public async Task Subscribe_WhenCreateConnectionFails_AndNothingIsFound_ReportsMaxioDidNotRespond_AndKeepsClaimPending()
    {
        _maxio.Intercept = (request, _, _) =>
            request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/subscriptions.json"
                ? throw new HttpRequestException("connection reset")
                : Task.FromResult<HttpResponseMessage?>(null);

        var ex = await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => CreateService().SubscribeAsync(Buyer, "eshop-pro"));

        Assert.True(ex.TimedOut);
        Assert.Contains("Maxio did not", ex.Message);
        var pending = await StoredEnrollmentAsync();
        Assert.Equal(SubscriptionEnrollmentStatus.Pending, pending!.Status);

        // A retry while the outcome may still land is refused rather than risking a duplicate.
        await Assert.ThrowsAsync<SubscriptionConflictException>(() => CreateService().SubscribeAsync(Buyer, "eshop-pro"));
        Assert.Equal(1, SubscriptionCreates);

        // Once the claim is stale and Maxio still holds nothing for its reference, it is released and retried.
        _maxio.Intercept = null;
        _time.Advance(new MaxioBillingTimeouts().StaleClaimAge + TimeSpan.FromSeconds(1));
        var retried = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.True(retried.Created);
        Assert.Equal(2, SubscriptionCreates);
        Assert.Single(_maxio.Subscriptions);
    }

    [Fact]
    public async Task Subscribe_WithStalePendingClaim_WhoseCreateLanded_SettlesItInsteadOfCreatingAgain()
    {
        _maxio.Intercept = (request, body, _) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/subscriptions.json")
            {
                throw new HttpRequestException("connection reset");
            }

            if (request.RequestUri!.AbsolutePath == "/subscriptions/lookup.json")
            {
                throw new HttpRequestException("still unreachable");
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };
        await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => CreateService().SubscribeAsync(Buyer, "eshop-pro"));
        var pending = (await StoredEnrollmentAsync())!;

        // The create had actually landed at Maxio with the reference we sent.
        var landed = _maxio.AddSubscription(_maxio.Customers.Single().Id, "eshop-pro", pending.SubscriptionReference);
        _maxio.Intercept = null;
        _time.Advance(new MaxioBillingTimeouts().StaleClaimAge + TimeSpan.FromSeconds(1));

        var result = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(landed.Id, result.Subscription.SubscriptionId);
        Assert.Equal(1, SubscriptionCreates);
        Assert.Equal(SubscriptionEnrollmentStatus.Completed, (await StoredEnrollmentAsync())!.Status);
    }

    [Fact]
    public async Task Subscribe_WhenCreateCustomerConnectionDrops_ButCustomerExists_ContinuesWithIt()
    {
        _maxio.Intercept = (request, body, _) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/customers.json")
            {
                _maxio.AddCustomer(CustomerReference, Buyer); // created, reply lost
                throw new HttpRequestException("connection reset");
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };

        var result = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.True(result.Created);
        Assert.Single(_maxio.Customers);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(2, _maxio.Count(HttpMethod.Get, "/customers/lookup.json"));
    }

    [Fact]
    public async Task Subscribe_WhenMaxioHangsBeforeAnyWrite_FailsWithinBudget_AndReleasesTheClaim()
    {
        var service = CreateService(new MaxioBillingTimeouts { RequestBudget = TimeSpan.FromMilliseconds(400) });
        await service.ListPlansAsync(); // warm the plan cache, then Maxio stops answering
        _maxio.Intercept = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        };

        var stopwatch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => service.SubscribeAsync(Buyer, "eshop-pro"));

        Assert.True(ex.TimedOut);
        Assert.Contains("Maxio did not respond", ex.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
        Assert.Null(await StoredEnrollmentAsync());
    }

    // ------------------------------------------------------------------ my subscriptions

    [Fact]
    public async Task ListSubscriptions_ForShopperWithoutMaxioCustomer_IsEmpty()
    {
        var subscriptions = await CreateService().ListSubscriptionsAsync(Buyer);

        Assert.Empty(subscriptions);
        Assert.DoesNotContain(_maxio.Requests, r => r.Path.EndsWith("/subscriptions.json"));
    }

    [Fact]
    public async Task ListSubscriptions_ReadsTheShoppersSubscriptionsFromMaxio()
    {
        var customer = _maxio.AddCustomer(CustomerReference, Buyer);
        var other = _maxio.AddCustomer("eshop:someone-else@example.com", "someone-else@example.com");
        _maxio.AddSubscription(customer.Id, "basic-plan", "r1");
        _maxio.AddSubscription(other.Id, "eshop-pro", "r2");

        var subscriptions = await CreateService().ListSubscriptionsAsync(Buyer);

        var only = Assert.Single(subscriptions);
        Assert.Equal("basic-plan", only.PlanHandle);
        Assert.Equal(29m, only.Price);
        Assert.Equal("USD", only.Currency);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
