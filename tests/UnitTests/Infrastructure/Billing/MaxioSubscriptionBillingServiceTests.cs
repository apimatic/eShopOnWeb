using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;
using static Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.MaxioTestHost;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string UserName = "demouser@microsoft.com";
    private const string CustomerSubscriptionsPath = "/customers/42/subscriptions.json";
    private static readonly Subscriber Demo = new(UserName, UserName);
    private static readonly string Reference = MaxioSubscriptionBillingService.BuildSubscriptionReference(UserName);

    private static string? QueryValue(HttpRequestMessage request, string key) =>
        request.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts[0] == key)
            .Select(parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty)
            .FirstOrDefault();

    private static async Task<SubscriptionEnrollment?> EnrollmentIn(Microsoft.EntityFrameworkCore.DbContextOptions<CatalogContext> db)
    {
        await using var check = new CatalogContext(db);
        return check.SubscriptionEnrollments.SingleOrDefault();
    }

    // ------------------------------------------------------------------ plans

    [Fact]
    public async Task ListPlans_WalksPagesUntilShortPage_ExcludesArchived_AndIsComplete()
    {
        var fullPage = Enumerable.Range(1, MaxioSubscriptionBillingService.PlanPageSize)
            .Select(i => Product($"plan-{i}", 100 * i, archived: i == 3))
            .ToArray();
        var handler = new StubHandler((req, _) =>
            QueryValue(req, "page") == "1"
                ? StubHandler.Json(HttpStatusCode.OK, Products(fullPage))
                : StubHandler.Json(HttpStatusCode.OK, Products(Product("eshop-pro", 29900))));
        var service = Service(handler, NewDatabase());

        var result = await service.ListPlansAsync();

        Assert.True(result.IsComplete);
        Assert.Equal(MaxioSubscriptionBillingService.PlanPageSize, result.Plans.Count); // 200 - 1 archived + 1
        Assert.DoesNotContain(result.Plans, p => p.Handle == "plan-3");
        var pro = Assert.Single(result.Plans, p => p.Handle == "eshop-pro");
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal(2, handler.Count(HttpMethod.Get, ProductsPath));
        Assert.All(handler.Requests, r => Assert.Contains("per_page=200", r.Query));
    }

    [Fact]
    public async Task ListPlans_StopsAtPageCap_AndReportsIncomplete()
    {
        var fullPage = Products(Enumerable.Range(1, MaxioSubscriptionBillingService.PlanPageSize)
            .Select(i => Product($"plan-{i}", 100)).ToArray());
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, fullPage));
        var service = Service(handler, NewDatabase());

        var result = await service.ListPlansAsync();

        Assert.False(result.IsComplete);
        Assert.Equal(MaxioSubscriptionBillingService.MaxPlanPages, handler.Count(HttpMethod.Get, ProductsPath));
    }

    [Fact]
    public async Task ListPlans_ProviderUnreachable_IsTranslatedNotLeaked()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));
        var service = Service(handler, NewDatabase());

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.ListPlansAsync());

        Assert.Equal(SubscriptionBillingErrorKind.ProviderUnavailable, ex.Kind);
    }

    // ------------------------------------------------------------------ cross-operation invariant

    [Fact]
    public async Task Subscribe_HandleNotInConfiguredFamily_IsRefusedBeforeAnyClaimOrWrite()
    {
        var maxio = new FakeMaxio(UserName);
        var db = NewDatabase();

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => Service(maxio.Handler, db).SubscribeAsync(Demo, "some-other-family-plan"));

        Assert.Equal(SubscriptionBillingErrorKind.UnknownPlan, ex.Kind);
        Assert.DoesNotContain(maxio.Handler.Requests, r => r.Path != ProductsPath);
        Assert.Null(await EnrollmentIn(db));
    }

    // ------------------------------------------------------------------ create path

    [Fact]
    public async Task Subscribe_NothingAtMaxio_ChecksThenCreates_CustomerAndSubscription()
    {
        var maxio = new FakeMaxio(UserName) { CustomerExists = false };
        var db = NewDatabase();

        var result = await Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(9001, result.Subscription!.SubscriptionId);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);

        // Reconciliation reads happen before the one create.
        var sequence = maxio.Handler.Requests.Select(r => (r.Method.Method, r.Path)).ToList();
        var create = sequence.IndexOf(("POST", SubscriptionsPath));
        var find = sequence.IndexOf(("GET", SubscriptionLookupPath));
        var list = sequence.IndexOf(("GET", CustomerSubscriptionsPath));
        Assert.InRange(find, 0, create - 1);
        Assert.InRange(list, 0, create - 1);

        var customerBody = Assert.Single(maxio.Handler.Requests, r => r.Method == HttpMethod.Post && r.Path == CustomersPath).Body!;
        Assert.Contains($"\"reference\":\"{UserName}\"", customerBody);
        Assert.Contains($"\"email\":\"{UserName}\"", customerBody);
        var subscriptionBody = Assert.Single(maxio.Handler.Requests, r => r.Method == HttpMethod.Post && r.Path == SubscriptionsPath).Body!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody);
        Assert.Contains("\"customer_id\":42", subscriptionBody);
        Assert.Contains($"\"reference\":\"{Reference}\"", subscriptionBody);

        var enrollment = await EnrollmentIn(db);
        Assert.Equal(SubscriptionEnrollmentStatus.Active, enrollment!.Status);
        Assert.Equal(42, enrollment.BillingCustomerId);
        Assert.Equal(9001, enrollment.BillingSubscriptionId);
    }

    [Fact]
    public async Task Subscribe_SendsRemittanceCollection_SoNoPaymentMethodIsNeeded()
    {
        var maxio = new FakeMaxio(UserName);

        await Service(maxio.Handler, NewDatabase()).SubscribeAsync(Demo, "basic-plan");

        var body = maxio.Handler.Requests.Single(r => r.Path == SubscriptionsPath).Body!;
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task Subscribe_CreateCustomer422_ReReadsByReference_AndDoesNotCreateTwice()
    {
        var maxio = new FakeMaxio(UserName) { CustomerExists = false };
        var lookups = 0;
        var handler = new StubHandler((req, body) =>
        {
            var path = Uri.UnescapeDataString(req.RequestUri!.AbsolutePath);
            if (path == CustomersPath)
            {
                // A concurrent request created the customer first: Maxio refuses the duplicate reference.
                maxio.CustomerExists = true;
                return StubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference: must be unique - that value has been taken."]}""");
            }

            if (path == CustomerLookupPath)
            {
                Interlocked.Increment(ref lookups);
            }

            return Forward(maxio, req, body);
        });

        var result = await Service(handler, NewDatabase()).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, handler.Count(HttpMethod.Post, CustomersPath));
        Assert.Equal(2, lookups);
        Assert.Contains("\"customer_id\":42", handler.Requests.Single(r => r.Path == SubscriptionsPath).Body);
    }

    [Fact]
    public async Task Subscribe_CreateSubscription422_ReturnsProviderErrors_AndReleasesClaim()
    {
        var maxio = new FakeMaxio(UserName)
        {
            CreateSubscription = _ => StubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file for the $299.00 balance"]}""")
        };
        var db = NewDatabase();

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro"));

        Assert.Equal(SubscriptionBillingErrorKind.RejectedByProvider, ex.Kind);
        Assert.Equal(new[] { "No payment method was on file for the $299.00 balance" }, ex.ProviderErrors);
        Assert.Null(await EnrollmentIn(db));
    }

    // ------------------------------------------------------------------ reconciliation before create (claims lost on restart)

    [Fact]
    public async Task Subscribe_AfterRestart_SubscriptionFoundByReference_IsAlreadySubscribed_WithoutCreating()
    {
        var maxio = new FakeMaxio(UserName);
        var existingId = maxio.AddSubscription("eshop-pro", Reference);
        var freshStore = NewDatabase(); // the in-memory claim table did not survive the restart

        var result = await Service(maxio.Handler, freshStore).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(existingId, result.Subscription!.SubscriptionId);
        Assert.Equal(0, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath));
        var enrollment = await EnrollmentIn(freshStore);
        Assert.Equal(SubscriptionEnrollmentStatus.Active, enrollment!.Status);
        Assert.Equal(existingId, enrollment.BillingSubscriptionId);
    }

    [Fact]
    public async Task Subscribe_AfterRestart_LiveSubscriptionToOtherPlanInCustomerList_IsConflict_AndRecorded()
    {
        var maxio = new FakeMaxio(UserName);
        var existingId = maxio.AddSubscription("basic-plan", reference: null); // not findable by our reference
        var freshStore = NewDatabase();

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => Service(maxio.Handler, freshStore).SubscribeAsync(Demo, "eshop-pro"));

        Assert.Equal(SubscriptionBillingErrorKind.AlreadySubscribedToOtherPlan, ex.Kind);
        Assert.Contains("basic-plan", ex.Message);
        Assert.Equal(0, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath));
        var enrollment = await EnrollmentIn(freshStore);
        Assert.Equal(SubscriptionEnrollmentStatus.Active, enrollment!.Status);
        Assert.Equal("basic-plan", enrollment.PlanHandle);
        Assert.Equal(existingId, enrollment.BillingSubscriptionId);

        // Asking for the plan actually held now replays it.
        var replay = await Service(maxio.Handler, freshStore).SubscribeAsync(Demo, "basic-plan");
        Assert.Equal(SubscribeOutcome.AlreadySubscribed, replay.Outcome);
    }

    [Fact]
    public async Task Subscribe_AfterRestart_LiveSamePlanInCustomerList_IsAlreadySubscribed()
    {
        var maxio = new FakeMaxio(UserName);
        maxio.AddSubscription("basic-plan", reference: null, state: "some_future_state"); // undeclared state counts as live
        var sameId = maxio.AddSubscription("eshop-pro", reference: null);

        var result = await Service(maxio.Handler, NewDatabase()).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(sameId, result.Subscription!.SubscriptionId);
        Assert.Equal(0, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task Subscribe_EndedSubscriptionsAndOtherFamilies_AreIgnored_AndCreates()
    {
        var maxio = new FakeMaxio(UserName);
        maxio.AddSubscription("eshop-pro", reference: null, state: "canceled");
        maxio.AddSubscription("basic-plan", reference: null, state: "expired");
        maxio.AddSubscription("eshop-pro", reference: null, state: "failed_to_create");
        maxio.AddSubscription("unrelated-product", reference: null, state: "active");

        var result = await Service(maxio.Handler, NewDatabase()).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task Subscribe_PreCreateCheckFails_ReleasesClaim_AndNeverCreates()
    {
        var maxio = new FakeMaxio(UserName) { FindSubscriptionOverride = () => StubHandler.Empty(HttpStatusCode.InternalServerError) };
        var db = NewDatabase();

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro"));

        Assert.Equal(SubscriptionBillingErrorKind.ProviderError, ex.Kind);
        Assert.Equal(0, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.Null(await EnrollmentIn(db));
    }

    // ------------------------------------------------------------------ duplicate claims

    [Fact]
    public async Task InMemoryProvider_RefusesSecondClaimForSameUser_WithArgumentException()
    {
        // Proves what the claim's catch must handle under UseOnlyInMemoryDatabase: the EF Core
        // in-memory store refuses a duplicate primary key on SaveChanges with ArgumentException
        // (SQL Server raises DbUpdateException). The service catches both.
        var db = NewDatabase();
        var now = DateTimeOffset.UtcNow;
        await using (var first = new CatalogContext(db))
        {
            first.SubscriptionEnrollments.Add(new SubscriptionEnrollment(UserName, "eshop-pro", Reference, now));
            await first.SaveChangesAsync();
        }

        await using var second = new CatalogContext(db);
        second.SubscriptionEnrollments.Add(new SubscriptionEnrollment(UserName, "eshop-pro", Reference, now));
        var ex = await Record.ExceptionAsync(() => second.SaveChangesAsync());

        Assert.IsType<ArgumentException>(ex);
    }

    [Fact]
    public async Task Subscribe_Repeat_IsStoppedByClaim_AndReplaysExistingSubscription()
    {
        var maxio = new FakeMaxio(UserName);
        var db = NewDatabase();

        var first = await Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro");
        var second = await Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, first.Outcome);
        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(first.Subscription!.SubscriptionId, second.Subscription!.SubscriptionId);
        Assert.Equal(1, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.Equal(0, maxio.Handler.Count(HttpMethod.Post, CustomersPath));
    }

    [Fact]
    public async Task Subscribe_ConcurrentDoubleClick_OnlyOneCreateSubscriptionReachesMaxio()
    {
        using var firstCallInsidePost = new ManualResetEventSlim();
        using var releasePost = new ManualResetEventSlim();
        var maxio = new FakeMaxio(UserName);
        maxio.CreateSubscription = body =>
        {
            firstCallInsidePost.Set();
            releasePost.Wait(TimeSpan.FromSeconds(10));
            return StubHandler.Json(HttpStatusCode.Created, maxio.AddFromCreateBody(body));
        };
        var db = NewDatabase();

        var first = Task.Run(() => Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro"));
        Assert.True(firstCallInsidePost.Wait(TimeSpan.FromSeconds(10)));

        var second = await Assert.ThrowsAsync<SubscriptionBillingException>(() => Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro"));
        releasePost.Set();
        var firstResult = await first;

        Assert.Equal(SubscriptionBillingErrorKind.SubscriptionInProgress, second.Kind);
        Assert.Equal(SubscribeOutcome.Created, firstResult.Outcome);
        Assert.Equal(1, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task Subscribe_WhileAnotherCallHoldsTheClaim_IsRefusedAsInProgress_WithoutReachingMaxio()
    {
        var maxio = new FakeMaxio(UserName);
        var db = NewDatabase();
        var clock = new ManualTimeProvider();
        await using (var other = new CatalogContext(db))
        {
            other.SubscriptionEnrollments.Add(new SubscriptionEnrollment(UserName, "eshop-pro", Reference, clock.Now));
            await other.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => Service(maxio.Handler, db, clock).SubscribeAsync(Demo, "eshop-pro"));

        Assert.Equal(SubscriptionBillingErrorKind.SubscriptionInProgress, ex.Kind);
        Assert.DoesNotContain(maxio.Handler.Requests, r => r.Path != ProductsPath);
    }

    [Fact]
    public async Task Subscribe_DifferentPlanWhileActive_IsConflict()
    {
        var maxio = new FakeMaxio(UserName);
        var db = NewDatabase();
        await Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro");

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => Service(maxio.Handler, db).SubscribeAsync(Demo, "basic-plan"));

        Assert.Equal(SubscriptionBillingErrorKind.AlreadySubscribedToOtherPlan, ex.Kind);
        Assert.Equal(1, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath));
    }

    // ------------------------------------------------------------------ unknown outcomes

    [Fact]
    public async Task Subscribe_CreateCustomerConnectionFails_IsSettledByReadingCustomerByReference()
    {
        var maxio = new FakeMaxio(UserName) { CustomerExists = false };
        var handler = new StubHandler((req, body) =>
        {
            if (Uri.UnescapeDataString(req.RequestUri!.AbsolutePath) == CustomersPath)
            {
                maxio.CustomerExists = true; // the create landed before the connection dropped
                throw new HttpRequestException("connection reset");
            }

            return Forward(maxio, req, body);
        });

        var result = await Service(handler, NewDatabase()).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, handler.Count(HttpMethod.Post, CustomersPath)); // never resent
        Assert.Contains($"reference={UserName}", handler.Requests.Last(r => r.Path == CustomerLookupPath).Query);
        Assert.Contains("\"customer_id\":42", handler.Requests.Single(r => r.Path == SubscriptionsPath).Body);
    }

    [Fact]
    public async Task Subscribe_CreateCustomerConnectionFails_AndNotFound_ReleasesClaim_AndReportsUnavailable()
    {
        var maxio = new FakeMaxio(UserName) { CustomerExists = false };
        var handler = new StubHandler((req, body) =>
            Uri.UnescapeDataString(req.RequestUri!.AbsolutePath) == CustomersPath
                ? throw new HttpRequestException("connection reset")
                : Forward(maxio, req, body));
        var db = NewDatabase();

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => Service(handler, db).SubscribeAsync(Demo, "eshop-pro"));

        Assert.Equal(SubscriptionBillingErrorKind.ProviderUnavailable, ex.Kind);
        Assert.Equal(0, handler.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.Null(await EnrollmentIn(db));
    }

    [Fact]
    public async Task Subscribe_CreateSubscriptionConnectionFails_IsSettledViaFindSubscription()
    {
        var maxio = new FakeMaxio(UserName);
        maxio.CreateSubscription = body =>
        {
            maxio.AddFromCreateBody(body); // landed at Maxio...
            throw new HttpRequestException("connection reset"); // ...but the response was lost
        };
        var db = NewDatabase();

        var result = await Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(9001, result.Subscription!.SubscriptionId);
        Assert.Equal(1, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath)); // never resent
        Assert.Contains($"reference={Reference}", maxio.Handler.Requests.Last(r => r.Path == SubscriptionLookupPath).Query);
        Assert.Equal(SubscriptionEnrollmentStatus.Active, (await EnrollmentIn(db))!.Status);
    }

    [Fact]
    public async Task Subscribe_CreateSubscriptionConnectionFails_AndNothingFound_IsPending_ThenSettledOnNextCall()
    {
        var maxio = new FakeMaxio(UserName)
        {
            CreateSubscription = _ => throw new HttpRequestException("connection reset")
        };
        var db = NewDatabase();

        var first = await Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.PendingConfirmation, first.Outcome);
        Assert.Null(first.Subscription);
        Assert.Equal(SubscriptionEnrollmentStatus.Unknown, (await EnrollmentIn(db))!.Status);

        // The subscription shows up at Maxio; the next call settles it instead of creating another.
        var landedId = maxio.AddSubscription("eshop-pro", Reference);
        var second = await Service(maxio.Handler, db).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(landedId, second.Subscription!.SubscriptionId);
        Assert.Equal(1, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.Equal(SubscriptionEnrollmentStatus.Active, (await EnrollmentIn(db))!.Status);
    }

    [Fact]
    public async Task Subscribe_UnknownOutcomePastLease_WithNothingAtMaxio_IsReleasedAndRetried()
    {
        var attempts = 0;
        var maxio = new FakeMaxio(UserName);
        maxio.CreateSubscription = body => Interlocked.Increment(ref attempts) == 1
            ? throw new HttpRequestException("connection reset")
            : StubHandler.Json(HttpStatusCode.Created, maxio.AddFromCreateBody(body));
        var db = NewDatabase();
        var clock = new ManualTimeProvider();

        var first = await Service(maxio.Handler, db, clock).SubscribeAsync(Demo, "eshop-pro");
        clock.Now += MaxioSubscriptionBillingService.ClaimLease + TimeSpan.FromSeconds(1);
        var second = await Service(maxio.Handler, db, clock).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(SubscribeOutcome.PendingConfirmation, first.Outcome);
        Assert.Equal(SubscribeOutcome.Created, second.Outcome);
        Assert.Equal(2, maxio.Handler.Count(HttpMethod.Post, SubscriptionsPath));
    }

    // ------------------------------------------------------------------ my subscriptions

    [Fact]
    public async Task GetSubscriptions_NoCustomerYet_ReturnsEmpty()
    {
        var maxio = new FakeMaxio(UserName) { CustomerExists = false };

        var result = await Service(maxio.Handler, NewDatabase()).GetSubscriptionsAsync(Demo);

        Assert.Empty(result.Subscriptions);
        Assert.False(result.HasPendingSubscription);
    }

    [Fact]
    public async Task GetSubscriptions_ListsCustomerSubscriptionsFromMaxio()
    {
        var maxio = new FakeMaxio(UserName);
        maxio.AddSubscription("eshop-pro", Reference);

        var result = await Service(maxio.Handler, NewDatabase()).GetSubscriptionsAsync(Demo);

        var subscription = Assert.Single(result.Subscriptions);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal("active", subscription.State);
        Assert.NotNull(subscription.NextBillingAt);
    }

    [Fact]
    public async Task GetSubscriptions_ProviderRejectsCredentials_IsProviderError()
    {
        var handler = new StubHandler((_, _) => StubHandler.Empty(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => Service(handler, NewDatabase()).GetSubscriptionsAsync(Demo));

        Assert.Equal(SubscriptionBillingErrorKind.ProviderError, ex.Kind);
    }

    /// <summary>Lets a test intercept one route and hand every other request to the fake.</summary>
    private static HttpResponseMessage Forward(FakeMaxio maxio, HttpRequestMessage request, string? body) =>
        maxio.Respond(request, body);
}
