using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Billing;

[TestClass]
public class SubscriptionBillingServiceTests
{
    private const string CustomersPath = "/customers.json";
    private const string SubscriptionsPath = "/subscriptions.json";
    private const string LookupPath = "/customers/lookup.json";

    private static bool Is(HttpRequestMessage request, HttpMethod method, string path) =>
        request.Method == method && Uri.UnescapeDataString(request.RequestUri!.AbsolutePath) == path;

    private static bool IsCustomerSubscriptionsList(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.Ordinal)
                                         && request.RequestUri.AbsolutePath.StartsWith("/customers/", StringComparison.Ordinal);

    private static string Reference(string userName = "shopper@example.test") => CustomerReference.ForUser(userName);

    // ------------------------------------------------------------------ plans listing and paging cap

    [TestMethod]
    public async Task Plans_ListsOfferedPlans_AndSkipsArchivedAndCardRequiredProducts()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.AddProduct("old-test", "Old", 100, archived: true);
        harness.Maxio.AddProduct("card-test", "Card", 100, requireCreditCard: true);

        var catalog = await harness.NewService().GetPlansAsync(default);

        Assert.IsFalse(catalog.IsPartial);
        CollectionAssert.AreEqual(new[] { "pro-test", "basic-test" }, catalog.Plans.Select(p => p.Handle).ToArray());
        var pro = catalog.Plans[0];
        Assert.AreEqual("Pro (test)", pro.Name);
        Assert.AreEqual(29900L, pro.Price.AmountInCents);
        Assert.AreEqual(299.00m, pro.Price.Amount);
        Assert.AreEqual(1, pro.Interval!.Length);
        Assert.AreEqual("month", pro.Interval.Unit);

        var request = harness.Maxio.Requests.Single();
        StringAssert.Contains(request.Query, "page=1");
        StringAssert.Contains(request.Query, "per_page=200");
    }

    [TestMethod]
    public async Task Plans_WalksPagesUntilAShortPage()
    {
        var harness = new BillingTestHarness(new FakeMaxio());
        for (var i = 0; i < 250; i++) harness.Maxio.AddProduct($"plan-{i}", $"Plan {i}", 100);

        var catalog = await harness.NewService().GetPlansAsync(default);

        Assert.IsFalse(catalog.IsPartial);
        Assert.AreEqual(250, catalog.Plans.Count);
        Assert.AreEqual(2, harness.Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task Plans_StopsAtThePageCap_AndReportsIsPartial()
    {
        var harness = new BillingTestHarness(new FakeMaxio());
        var total = SubscriptionBillingService.PlanPageSize * SubscriptionBillingService.MaxPlanPages + 1;
        for (var i = 0; i < total; i++) harness.Maxio.AddProduct($"plan-{i}", $"Plan {i}", 100);

        var catalog = await harness.NewService().GetPlansAsync(default);

        Assert.IsTrue(catalog.IsPartial);
        Assert.AreEqual(SubscriptionBillingService.PlanPageSize * SubscriptionBillingService.MaxPlanPages, catalog.Plans.Count);
        Assert.AreEqual(SubscriptionBillingService.MaxPlanPages, harness.Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task Plans_UnknownProductFamily_MapsTo502()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.Intercept = (_, _) => Task.FromResult<HttpResponseMessage?>(
            FakeMaxio.Json(HttpStatusCode.NotFound, JsonValue.Create("Product family not found")!));

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(() => harness.NewService().GetPlansAsync(default));

        Assert.AreEqual(502, ex.StatusCode);
    }

    // ------------------------------------------------------------------ invariant: invalid plan -> 400, no write

    [TestMethod]
    public async Task Subscribe_PlanNotOffered_Returns400_AndMakesNoClaimAndNoWrite()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.AddProduct("archived-test", "Archived", 100, archived: true);

        foreach (var handle in new[] { "no-such-plan", "archived-test", "PRO-TEST" })
        {
            var ex = await Assert.ThrowsExceptionAsync<BillingException>(
                () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), handle, default));
            Assert.AreEqual(400, ex.StatusCode);
        }

        Assert.IsFalse(harness.Maxio.Requests.Any(r => r.Method != HttpMethod.Get), "no write may reach Maxio");
        Assert.IsFalse(harness.Maxio.Requests.Any(r => r.Path != $"/product_families/handle:{FakeMaxio.FamilyHandle}/products.json"),
            "only the plan listing is read");
        using var db = harness.NewContext();
        Assert.AreEqual(0, await db.BillingCustomerLinks.CountAsync());
        Assert.AreEqual(0, await db.SubscriptionEnrollments.CountAsync());
    }

    [TestMethod]
    public async Task Subscribe_MissingPlanHandle_Returns400_WithoutCallingMaxio()
    {
        var harness = new BillingTestHarness();

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "  ", default));

        Assert.AreEqual(400, ex.StatusCode);
        Assert.AreEqual(0, harness.Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task Subscribe_HandleBeyondAPartialPlanList_Returns503_NotA400()
    {
        var harness = new BillingTestHarness(new FakeMaxio());
        var total = SubscriptionBillingService.PlanPageSize * SubscriptionBillingService.MaxPlanPages;
        for (var i = 0; i < total; i++) harness.Maxio.AddProduct($"plan-{i}", $"Plan {i}", 100);
        harness.Maxio.AddProduct("beyond-cap", "Beyond", 100);

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "beyond-cap", default));

        Assert.AreEqual(503, ex.StatusCode);
        Assert.IsFalse(harness.Maxio.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    // ------------------------------------------------------------------ customer create and the happy path

    [TestMethod]
    public async Task Subscribe_NewShopper_CreatesCustomerThenSubscription()
    {
        var harness = new BillingTestHarness();

        var result = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Created, result.Outcome);
        var dto = result.Subscription;
        Assert.IsNotNull(dto.SubscriptionId);
        Assert.AreEqual("pro-test", dto.PlanHandle);
        Assert.AreEqual("Pro (test)", dto.PlanName);
        Assert.AreEqual(299.00m, dto.Price.Amount);
        Assert.AreEqual(29900L, dto.Price.AmountInCents);
        Assert.AreEqual("USD", dto.Price.Currency);
        Assert.AreEqual("month", dto.Interval!.Unit);
        Assert.AreEqual("active", dto.State);
        Assert.AreEqual(DateTimeOffset.Parse("2026-11-20T10:00:00Z"), dto.NextBillingAt);
        Assert.IsNotNull(dto.CreatedAt);

        var customerPost = harness.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == CustomersPath);
        var customer = JsonNode.Parse(customerPost.Body!)!["customer"]!;
        Assert.AreEqual(Reference(), (string?)customer["reference"]);
        Assert.AreEqual("shopper@example.test", (string?)customer["email"]);
        Assert.AreEqual("shopper", (string?)customer["first_name"]);
        Assert.AreEqual("Customer", (string?)customer["last_name"]);

        var subscriptionPost = harness.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == SubscriptionsPath);
        var (customerId, productHandle, reference) = FakeMaxio.CreatedSubscription(subscriptionPost.Body);
        Assert.AreEqual(harness.Maxio.CustomersByReference[Reference()], customerId);
        Assert.AreEqual("pro-test", productHandle);
        Assert.AreEqual($"{Reference()}:pro-test", reference);

        using var db = harness.NewContext();
        var link = await db.BillingCustomerLinks.SingleAsync();
        Assert.AreEqual(BillingClaimState.Completed, link.State);
        Assert.AreEqual(customerId, link.ProviderCustomerId);
        var enrollment = await db.SubscriptionEnrollments.SingleAsync();
        Assert.AreEqual(BillingClaimState.Completed, enrollment.State);
        Assert.AreEqual(dto.SubscriptionId, enrollment.ProviderSubscriptionId);
    }

    [TestMethod]
    public async Task Subscribe_CustomerAlreadyAtMaxio_IsAdoptedWithoutCreate()
    {
        var harness = new BillingTestHarness();
        var existingId = harness.Maxio.AddCustomer(Reference());

        var result = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "basic-test", default);

        Assert.AreEqual(SubscribeOutcome.Created, result.Outcome);
        Assert.AreEqual(0, harness.Maxio.Count(HttpMethod.Post, CustomersPath));
        using var db = harness.NewContext();
        Assert.AreEqual(existingId, (await db.BillingCustomerLinks.SingleAsync()).ProviderCustomerId);
    }

    [TestMethod]
    public async Task Subscribe_SamePlanTwice_ReturnsTheExistingSubscription_WithoutASecondWrite()
    {
        var harness = new BillingTestHarness();
        var first = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        var second = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Existing, second.Outcome);
        Assert.AreEqual(first.Subscription.SubscriptionId, second.Subscription.SubscriptionId);
        Assert.AreEqual(1, harness.Maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.AreEqual(1, harness.Maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [TestMethod]
    public async Task Subscribe_LostLocalStore_AdoptsTheSubscriptionCarryingTheReference()
    {
        // Simulates a restart of the in-memory store: Maxio already has the customer and the subscription.
        var harness = new BillingTestHarness();
        var customerId = harness.Maxio.AddCustomer(Reference());
        var existing = harness.Maxio.AddSubscription(customerId, "pro-test", $"{Reference()}:pro-test");

        var result = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Existing, result.Outcome);
        Assert.AreEqual(existing["id"]!.GetValue<int>(), result.Subscription.SubscriptionId);
        Assert.AreEqual(0, harness.Maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.AreEqual(0, harness.Maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    // ------------------------------------------------------------------ duplicate claims under concurrency

    [TestMethod]
    public async Task Subscribe_ConcurrentDuplicate_SecondCallerIsRefusedByTheClaim_WithoutASecondPost()
    {
        var harness = new BillingTestHarness();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Maxio.Intercept = async (request, _) =>
        {
            if (Is(request, HttpMethod.Post, SubscriptionsPath))
            {
                arrived.TrySetResult();
                await release.Task;
            }

            return null;
        };

        var first = Task.Run(() => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default));
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default));
        Assert.AreEqual(409, ex.StatusCode);

        release.SetResult();
        var created = await BillingTestHarness.WithTimeout(first);
        Assert.AreEqual(SubscribeOutcome.Created, created.Outcome);

        var again = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);
        Assert.AreEqual(SubscribeOutcome.Existing, again.Outcome);
        Assert.AreEqual(created.Subscription.SubscriptionId, again.Subscription.SubscriptionId);
        Assert.AreEqual(1, harness.Maxio.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.AreEqual(1, harness.Maxio.Subscriptions.Count);
    }

    [TestMethod]
    public async Task EnsureCustomer_ConcurrentDuplicate_SecondCallerIsRefusedByTheClaim_WithoutASecondPost()
    {
        var harness = new BillingTestHarness();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Maxio.Intercept = async (request, _) =>
        {
            if (Is(request, HttpMethod.Post, CustomersPath))
            {
                arrived.TrySetResult();
                await release.Task;
            }

            return null;
        };

        var first = Task.Run(() => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default));
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "basic-test", default));
        Assert.AreEqual(409, ex.StatusCode);

        release.SetResult();
        Assert.AreEqual(SubscribeOutcome.Created, (await BillingTestHarness.WithTimeout(first)).Outcome);
        Assert.AreEqual(1, harness.Maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.AreEqual(1, harness.Maxio.CustomersByReference.Count);
    }

    // ------------------------------------------------------------------ 422 handling

    [TestMethod]
    public async Task CreateSubscription_422_SurfacesMaxioErrors_AndReleasesTheClaim()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.Intercept = (request, _) => Task.FromResult(Is(request, HttpMethod.Post, SubscriptionsPath)
            ? FakeMaxio.Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Product is not available for signup") })
            : null);

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default));

        Assert.AreEqual(422, ex.StatusCode);
        CollectionAssert.AreEqual(new[] { "Product is not available for signup" }, ex.Errors.ToArray());
        using (var db = harness.NewContext())
        {
            Assert.AreEqual(0, await db.SubscriptionEnrollments.CountAsync(), "a definitive refusal releases the claim");
            Assert.AreEqual(BillingClaimState.Completed, (await db.BillingCustomerLinks.SingleAsync()).State);
        }

        harness.Maxio.Intercept = null;
        var retry = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);
        Assert.AreEqual(SubscribeOutcome.Created, retry.Outcome);
    }

    [TestMethod]
    public async Task CreateCustomer_422_SurfacesMaxioErrors_ReleasesTheClaim_AndNeverSubscribes()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.Intercept = (request, _) => Task.FromResult(Is(request, HttpMethod.Post, CustomersPath)
            ? FakeMaxio.Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Email address: is invalid") })
            : null);

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default));

        Assert.AreEqual(422, ex.StatusCode);
        CollectionAssert.AreEqual(new[] { "Email address: is invalid" }, ex.Errors.ToArray());
        Assert.AreEqual(0, harness.Maxio.Count(HttpMethod.Post, SubscriptionsPath));
        using var db = harness.NewContext();
        Assert.AreEqual(0, await db.BillingCustomerLinks.CountAsync());
    }

    [TestMethod]
    public async Task CreateCustomer_422ForADuplicateReference_AdoptsTheExistingCustomer()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.Intercept = (request, body) =>
        {
            if (!Is(request, HttpMethod.Post, CustomersPath)) return Task.FromResult<HttpResponseMessage?>(null);
            harness.Maxio.AddCustomer(FakeMaxio.CreatedCustomerReference(body)); // someone else created it meanwhile
            return Task.FromResult<HttpResponseMessage?>(FakeMaxio.Json(HttpStatusCode.UnprocessableEntity,
                new JsonObject { ["errors"] = new JsonArray("Reference must be unique") }));
        };

        var result = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Created, result.Outcome);
        Assert.AreEqual(1, harness.Maxio.CustomersByReference.Count);
    }

    // ------------------------------------------------------------------ unknown outcomes: connection failure on each write

    [TestMethod]
    public async Task CreateCustomer_ConnectionFailsAfterTheWriteLanded_IsSettledByReference()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.Intercept = (request, body) =>
        {
            if (Is(request, HttpMethod.Post, CustomersPath))
            {
                harness.Maxio.AddCustomer(FakeMaxio.CreatedCustomerReference(body));
                throw BillingTestHarness.ConnectionReset();
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };

        var result = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Created, result.Outcome);
        Assert.AreEqual(1, harness.Maxio.Count(HttpMethod.Post, CustomersPath), "the SDK never resends the POST");
        Assert.IsTrue(harness.Maxio.Count(HttpMethod.Get, LookupPath) >= 2, "settled by re-reading the reference");
        using var db = harness.NewContext();
        var link = await db.BillingCustomerLinks.SingleAsync();
        Assert.AreEqual(BillingClaimState.Completed, link.State);
        Assert.AreEqual(harness.Maxio.CustomersByReference[Reference()], link.ProviderCustomerId);
    }

    [TestMethod]
    public async Task CreateCustomer_ConnectionFailsBeforeTheWriteLanded_IsRecordedUnknown_ThenSettledByTheNextRequest()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.Intercept = (request, _) => Is(request, HttpMethod.Post, CustomersPath)
            ? throw BillingTestHarness.ConnectionReset()
            : Task.FromResult<HttpResponseMessage?>(null);

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default));

        Assert.AreEqual(503, ex.StatusCode);
        using (var db = harness.NewContext())
        {
            Assert.AreEqual(BillingClaimState.Unknown, (await db.BillingCustomerLinks.SingleAsync()).State);
        }

        harness.Maxio.Intercept = null;
        var retry = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Created, retry.Outcome);
        Assert.AreEqual(1, harness.Maxio.CustomersByReference.Count);
        using (var db = harness.NewContext())
        {
            Assert.AreEqual(BillingClaimState.Completed, (await db.BillingCustomerLinks.SingleAsync()).State);
        }
    }

    [TestMethod]
    public async Task CreateSubscription_ConnectionFailsAfterTheWriteLanded_IsSettledByReference()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.Intercept = (request, body) =>
        {
            if (Is(request, HttpMethod.Post, SubscriptionsPath))
            {
                var (customerId, handle, reference) = FakeMaxio.CreatedSubscription(body);
                harness.Maxio.AddSubscription(customerId, handle, reference);
                throw BillingTestHarness.ConnectionReset();
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };

        var result = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Created, result.Outcome);
        Assert.AreEqual(harness.Maxio.Subscriptions.Keys.Single(), result.Subscription.SubscriptionId);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual(1, harness.Maxio.Count(HttpMethod.Post, SubscriptionsPath), "the SDK never resends the POST");
        using var db = harness.NewContext();
        Assert.AreEqual(BillingClaimState.Completed, (await db.SubscriptionEnrollments.SingleAsync()).State);
    }

    [TestMethod]
    public async Task CreateSubscription_ConnectionFailsAndTheReReadFails_IsRecordedUnknown_ThenMySubscriptionsSettlesIt()
    {
        var harness = new BillingTestHarness();
        var posted = false;
        var listingDown = true;
        harness.Maxio.Intercept = (request, body) =>
        {
            if (Is(request, HttpMethod.Post, SubscriptionsPath))
            {
                var (customerId, handle, reference) = FakeMaxio.CreatedSubscription(body);
                harness.Maxio.AddSubscription(customerId, handle, reference);
                posted = true;
                throw BillingTestHarness.ConnectionReset();
            }

            if (posted && listingDown && IsCustomerSubscriptionsList(request))
            {
                // 400 is not retried by the SDK, so the re-read fails fast.
                return Task.FromResult<HttpResponseMessage?>(FakeMaxio.Json(HttpStatusCode.BadRequest, new JsonObject()));
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };

        var result = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Unknown, result.Outcome);
        Assert.AreEqual(SubscriptionDto.UnknownState, result.Subscription.State);
        Assert.AreEqual("pro-test", result.Subscription.PlanHandle);
        using (var db = harness.NewContext())
        {
            Assert.AreEqual(BillingClaimState.Unknown, (await db.SubscriptionEnrollments.SingleAsync()).State);
        }

        listingDown = false;
        var mine = await harness.NewService().GetMySubscriptionsAsync(BillingTestHarness.Caller(), default);

        Assert.AreEqual(1, mine.Count);
        Assert.AreEqual("active", mine[0].State);
        Assert.AreEqual(harness.Maxio.Subscriptions.Keys.Single(), mine[0].SubscriptionId);
        using (var db = harness.NewContext())
        {
            Assert.AreEqual(BillingClaimState.Completed, (await db.SubscriptionEnrollments.SingleAsync()).State);
        }

        var again = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);
        Assert.AreEqual(SubscribeOutcome.Existing, again.Outcome);
        Assert.AreEqual(1, harness.Maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [TestMethod]
    public async Task CreateSubscription_ConnectionFailsBeforeTheWriteLanded_IsRecordedUnknown_ThenTheRetryCreatesIt()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.Intercept = (request, _) => Is(request, HttpMethod.Post, SubscriptionsPath)
            ? throw BillingTestHarness.ConnectionReset()
            : Task.FromResult<HttpResponseMessage?>(null);

        var first = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);
        Assert.AreEqual(SubscribeOutcome.Unknown, first.Outcome);

        var mine = await harness.NewService().GetMySubscriptionsAsync(BillingTestHarness.Caller(), default);
        Assert.AreEqual(SubscriptionDto.UnknownState, mine.Single().State, "an unconfirmed subscribe is shown, not hidden");

        harness.Maxio.Intercept = null;
        var retry = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Created, retry.Outcome);
        Assert.AreEqual(1, harness.Maxio.Subscriptions.Count);
    }

    // ------------------------------------------------------------------ other provider failures and reads

    [TestMethod]
    public async Task CustomerLookup_401_MapsTo502_AndReleasesTheClaim()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.Intercept = (request, _) => Task.FromResult(Is(request, HttpMethod.Get, LookupPath)
            ? FakeMaxio.Json(HttpStatusCode.Unauthorized, new JsonObject())
            : null);

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default));

        Assert.AreEqual(502, ex.StatusCode);
        Assert.IsFalse(harness.Maxio.Requests.Any(r => r.Method == HttpMethod.Post));
        using var db = harness.NewContext();
        Assert.AreEqual(0, await db.BillingCustomerLinks.CountAsync());
    }

    [TestMethod]
    public async Task MySubscriptions_NoCustomerAtMaxio_ReturnsEmpty()
    {
        var harness = new BillingTestHarness();

        var mine = await harness.NewService().GetMySubscriptionsAsync(BillingTestHarness.Caller(), default);

        Assert.AreEqual(0, mine.Count);
        Assert.IsFalse(harness.Maxio.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task MySubscriptions_ListsOnlyTheCallersSubscriptions()
    {
        var harness = new BillingTestHarness();
        await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);
        await harness.NewService().SubscribeAsync(BillingTestHarness.Caller("other@example.test"), "basic-test", default);

        var mine = await harness.NewService().GetMySubscriptionsAsync(BillingTestHarness.Caller(), default);

        Assert.AreEqual(1, mine.Count);
        Assert.AreEqual("pro-test", mine[0].PlanHandle);
        Assert.AreEqual(299.00m, mine[0].Price.Amount);
    }

    [TestMethod]
    public void CustomerReference_IsStableCaseInsensitiveAndShort()
    {
        var a = CustomerReference.ForUser("Shopper@Example.test");
        var b = CustomerReference.ForUser("shopper@example.test ");

        Assert.AreEqual(a, b);
        Assert.IsTrue(a.StartsWith("eshop-", StringComparison.Ordinal));
        Assert.IsTrue(a.Length <= 40);
        Assert.IsFalse(a.Contains("shopper", StringComparison.OrdinalIgnoreCase));
    }
}
