using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionBillingServiceTests
{
    private static readonly BillingUser User = new("demouser@microsoft.com", "demouser@microsoft.com");

    private sealed class Harness
    {
        public required FakeMaxio Maxio { get; init; }
        public required string DatabaseName { get; init; }
        public required MaxioSettings Settings { get; init; }

        /// <summary>A fresh service per call, as each HTTP request gets its own scope and DbContext.</summary>
        public MaxioSubscriptionBillingService NewService()
        {
            var client = MaxioServiceCollectionExtensions.CreateClient(new HttpClient(Maxio), Settings, NullLoggerFactory.Instance);
            return new MaxioSubscriptionBillingService(client, new BillingClaimStore(NewContext()), Options.Create(Settings),
                new MemoryCache(new MemoryCacheOptions()), TimeProvider.System, NullLogger<MaxioSubscriptionBillingService>.Instance);
        }

        public CatalogContext NewContext() =>
            new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(DatabaseName).Options);
    }

    private static Harness NewHarness() => new()
    {
        Maxio = new FakeMaxio(),
        DatabaseName = Guid.NewGuid().ToString(),
        Settings = new MaxioSettings
        {
            ApiKey = "test-api-key-not-a-secret",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family",
            AttemptTimeout = TimeSpan.FromSeconds(5),
            RequestBudget = TimeSpan.FromSeconds(10)
        }
    };

    [TestMethod]
    public async Task ListsActivePlansOfTheConfiguredFamily()
    {
        var h = NewHarness();

        var catalog = await h.NewService().GetPlansAsync();

        CollectionAssert.AreEqual(new[] { "basic", "pro" }, catalog.Plans.Select(p => p.Handle).ToArray());
        Assert.IsFalse(catalog.Truncated);
        var request = h.Maxio.Requests.Single();
        Assert.AreEqual("/product_families/handle:test-family/products.json", Uri.UnescapeDataString(request.Path));
        StringAssert.Contains(request.Query, "per_page=200");
    }

    [TestMethod]
    public async Task FlagsThePlanListAsTruncatedAtThePageCap()
    {
        var h = NewHarness();
        h.Maxio.RepeatProductsOnEveryPage = true;

        var catalog = await h.NewService().GetPlansAsync();

        Assert.IsTrue(catalog.Truncated);
        Assert.AreEqual(MaxioSubscriptionBillingService.MAX_PLAN_PAGES, h.Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionOnce()
    {
        var h = NewHarness();

        var first = await h.NewService().SubscribeAsync(User, "pro");
        var second = await h.NewService().SubscribeAsync(User, "pro");

        Assert.AreEqual(SubscribeOutcome.Created, first.Outcome);
        Assert.AreEqual("active", first.Subscription!.State);
        Assert.AreEqual(29900, first.Subscription.PriceInCents);
        Assert.IsNotNull(first.Subscription.NextBillingAt);
        Assert.AreEqual(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.AreEqual(first.Subscription.SubscriptionId, second.Subscription!.SubscriptionId);
        Assert.AreEqual(1, h.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));

        var sent = h.Maxio.Requests.Single(r => r.Path == "/subscriptions.json").Body!;
        StringAssert.Contains(sent, "\"product_handle\":\"pro\"");
        StringAssert.Contains(sent, $"\"reference\":\"{first.Reference}\"");
        StringAssert.Contains(sent, "\"payment_collection_method\":\"remittance\"");
        StringAssert.Contains(sent, $"\"customer_id\":{h.Maxio.Customers.Values.Single()}");
    }

    [TestMethod]
    public async Task AdoptsProviderStateWhenLocalClaimsWereLost()
    {
        var h = NewHarness();
        var created = await h.NewService().SubscribeAsync(User, "pro");

        // Simulate a restart of the in-memory store: Maxio still holds the customer and subscription.
        var restarted = new Harness { Maxio = h.Maxio, DatabaseName = Guid.NewGuid().ToString(), Settings = h.Settings };
        var again = await restarted.NewService().SubscribeAsync(User, "pro");

        Assert.AreEqual(SubscribeOutcome.AlreadySubscribed, again.Outcome);
        Assert.AreEqual(created.Subscription!.SubscriptionId, again.Subscription!.SubscriptionId);
        Assert.AreEqual(1, h.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task SecondCallerIsRefusedWhileTheFirstHoldsTheClaim()
    {
        var h = NewHarness();
        await h.NewService().SubscribeAsync(User, "basic"); // creates the customer
        var reference = BillingReferences.Subscription(BillingReferences.Customer(User.UserName), "pro");
        await using (var db = h.NewContext())
        {
            db.BillingClaims.Add(new BillingClaim(BillingReferences.SubscriptionClaimKey(reference), BillingClaimKind.Subscription, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsExceptionAsync<SubscriptionBillingException>(() => h.NewService().SubscribeAsync(User, "pro"));

        Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.IsFalse(h.Maxio.Subscriptions.ContainsKey(reference));
    }

    [TestMethod]
    public async Task StoreRefusesASecondClaimForTheSameKey()
    {
        var h = NewHarness();
        var first = new BillingClaimStore(h.NewContext());
        var second = new BillingClaimStore(h.NewContext());

        Assert.IsTrue(await first.TryClaimAsync(new BillingClaim("subscription:x", BillingClaimKind.Subscription, DateTimeOffset.UtcNow)));
        Assert.IsFalse(await second.TryClaimAsync(new BillingClaim("subscription:x", BillingClaimKind.Subscription, DateTimeOffset.UtcNow)));
    }

    [TestMethod]
    public async Task OnlyOneCallerCanTakeOverAStaleClaim()
    {
        var h = NewHarness();
        await using (var db = h.NewContext())
        {
            db.BillingClaims.Add(new BillingClaim("subscription:y", BillingClaimKind.Subscription, DateTimeOffset.UtcNow.AddHours(-1)));
            await db.SaveChangesAsync();
        }
        var a = new BillingClaimStore(h.NewContext());
        var b = new BillingClaimStore(h.NewContext());
        var claimA = (await a.FindAsync("subscription:y"))!;
        var claimB = (await b.FindAsync("subscription:y"))!;

        Assert.IsTrue(await a.TryTakeOverAsync(claimA, DateTimeOffset.UtcNow));
        Assert.IsFalse(await b.TryTakeOverAsync(claimB, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public async Task ConcurrentSubscribesReachTheProviderOnce()
    {
        var h = NewHarness();
        await h.NewService().SubscribeAsync(User, "basic"); // customer exists, so both race on the subscription claim

        var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            try { return (await h.NewService().SubscribeAsync(User, "pro")).Outcome.ToString(); }
            catch (SubscriptionBillingException e) { return e.StatusCode.ToString(); }
        })).ToArray();
        var outcomes = await Task.WhenAll(attempts);

        Assert.AreEqual(1, outcomes.Count(o => o == nameof(SubscribeOutcome.Created)), string.Join(",", outcomes));
        Assert.IsTrue(outcomes.All(o => o is nameof(SubscribeOutcome.Created) or nameof(SubscribeOutcome.AlreadySubscribed) or nameof(HttpStatusCode.Conflict)));
        Assert.AreEqual(1, h.Maxio.Requests.Count(r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json" && r.Body!.Contains("\"product_handle\":\"pro\"")));
    }

    [TestMethod]
    public async Task RejectsAHandleOutsideTheConfiguredFamilyBeforeAnyWrite()
    {
        var h = NewHarness();

        var archived = await Assert.ThrowsExceptionAsync<SubscriptionBillingException>(() => h.NewService().SubscribeAsync(User, "legacy"));
        var unknown = await Assert.ThrowsExceptionAsync<SubscriptionBillingException>(() => h.NewService().SubscribeAsync(User, "api-call"));

        Assert.AreEqual(HttpStatusCode.BadRequest, archived.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.IsFalse(h.Maxio.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task CustomerCreatedDespiteALostConnectionIsAdoptedNotDuplicated()
    {
        var h = NewHarness();
        h.Maxio.DropConnectionAfterCreatingCustomer = true;

        var result = await h.NewService().SubscribeAsync(User, "pro");

        Assert.AreEqual(SubscribeOutcome.Created, result.Outcome);
        Assert.AreEqual(1, h.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, h.Maxio.Customers.Count);
        StringAssert.Contains(h.Maxio.Requests.Single(r => r.Path == "/subscriptions.json").Body!,
            $"\"customer_id\":{h.Maxio.Customers.Values.Single()}");
    }

    [TestMethod]
    public async Task ConnectionLostAfterTheWriteLandedIsSettledAsCreated()
    {
        var h = NewHarness();
        h.Maxio.DropConnectionAfterCreatingSubscription = true;

        var result = await h.NewService().SubscribeAsync(User, "pro");

        Assert.AreEqual(SubscribeOutcome.Created, result.Outcome);
        Assert.IsNotNull(result.Subscription);
        Assert.AreEqual(1, h.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        await using var db = h.NewContext();
        var claim = await db.BillingClaims.SingleAsync(c => c.Key == BillingReferences.SubscriptionClaimKey(result.Reference));
        Assert.AreEqual(BillingClaimStatus.Completed, claim.Status);
    }

    [TestMethod]
    public async Task UnconfirmableWriteIsReportedPendingAndSettledByTheNextRequest()
    {
        var h = NewHarness();
        h.Maxio.DropConnectionAfterCreatingSubscription = true;
        h.Maxio.HideSubscriptionsFromLookup = true;

        var pending = await h.NewService().SubscribeAsync(User, "pro");

        Assert.AreEqual(SubscribeOutcome.Pending, pending.Outcome);
        await using (var db = h.NewContext())
        {
            var claim = await db.BillingClaims.SingleAsync(c => c.Key == BillingReferences.SubscriptionClaimKey(pending.Reference));
            Assert.AreEqual(BillingClaimStatus.Unknown, claim.Status);
        }

        h.Maxio.HideSubscriptionsFromLookup = false;
        h.Maxio.DropConnectionAfterCreatingSubscription = false;
        var settled = await h.NewService().SubscribeAsync(User, "pro");

        Assert.AreEqual(SubscribeOutcome.AlreadySubscribed, settled.Outcome);
        Assert.AreEqual(1, h.Maxio.Requests.Count(r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json" && r.Body!.Contains(pending.Reference)));
    }

    [TestMethod]
    public async Task ConnectionLostBeforeTheWriteIsRetriedSafelyLater()
    {
        var h = NewHarness();
        h.Maxio.DropConnectionBeforeCreatingSubscription = true;

        var pending = await h.NewService().SubscribeAsync(User, "pro");
        Assert.AreEqual(SubscribeOutcome.Pending, pending.Outcome);

        h.Maxio.DropConnectionBeforeCreatingSubscription = false;
        var created = await h.NewService().SubscribeAsync(User, "pro");

        Assert.AreEqual(SubscribeOutcome.Created, created.Outcome);
        Assert.AreEqual(1, h.Maxio.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ProviderRejectionReleasesTheClaimAndSurfacesTheErrors()
    {
        var h = NewHarness();
        h.Maxio.RejectSubscriptionWith = new[] { "No payment method was on file" };

        var ex = await Assert.ThrowsExceptionAsync<SubscriptionBillingException>(() => h.NewService().SubscribeAsync(User, "pro"));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        CollectionAssert.AreEqual(new[] { "No payment method was on file" }, ex.Errors.ToArray());
        await using var db = h.NewContext();
        Assert.IsFalse(await db.BillingClaims.AnyAsync(c => c.Kind == BillingClaimKind.Subscription));

        h.Maxio.RejectSubscriptionWith = null;
        Assert.AreEqual(SubscribeOutcome.Created, (await h.NewService().SubscribeAsync(User, "pro")).Outcome);
    }

    [TestMethod]
    public async Task ListsOnlyTheCallersSubscriptions()
    {
        var h = NewHarness();
        Assert.AreEqual(0, (await h.NewService().GetSubscriptionsAsync(User)).Count);
        Assert.AreEqual(0, h.Maxio.Count(HttpMethod.Post, "/customers.json"), "reading must not create a customer");

        await h.NewService().SubscribeAsync(User, "pro");
        await h.NewService().SubscribeAsync(new BillingUser("someone.else@example.com", "someone.else@example.com"), "basic");

        var mine = await h.NewService().GetSubscriptionsAsync(User);

        Assert.AreEqual(1, mine.Count);
        Assert.AreEqual("pro", mine[0].PlanHandle);
    }

    [TestMethod]
    public async Task RejectedCredentialsAreAProviderOutageNotACallerError()
    {
        var h = NewHarness();
        h.Maxio.FailEverythingWith = HttpStatusCode.Unauthorized;

        var ex = await Assert.ThrowsExceptionAsync<SubscriptionBillingException>(() => h.NewService().GetPlansAsync());

        Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, ex.ProviderStatusCode);
    }

    [TestMethod]
    public void SettingsValidationNamesTheMissingKey()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings { ApiKey = " ", ProductFamilyHandle = "f", Subdomain = "s" });

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "Maxio:ApiKey");
        Assert.IsTrue(new MaxioSettingsValidator().Validate(null, new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://example.test" }).Succeeded);
    }
}
