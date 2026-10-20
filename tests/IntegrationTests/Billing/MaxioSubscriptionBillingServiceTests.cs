#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string ProductsRoute = "GET /product_families/handle%3Atest-family/products.json";
    private const string SiteRoute = "GET /site.json";
    private const string CustomerLookupRoute = "GET /customers/lookup.json";
    private const string CreateCustomerRoute = "POST /customers.json";
    private const string SubscriptionLookupRoute = "GET /subscriptions/lookup.json";
    private const string CreateSubscriptionRoute = "POST /subscriptions.json";

    private const string ProductsJson = """
        [
          { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false } },
          { "product": { "id": 2, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false } },
          { "product": { "id": 3, "name": "Old Plan", "handle": "old-plan", "price_in_cents": 100, "archived_at": "2024-01-01T00:00:00Z" } }
        ]
        """;

    private const string CustomerJson = """{ "customer": { "id": 42, "email": "demouser@microsoft.com" } }""";

    private static readonly BillingBuyer Buyer = new("demouser@microsoft.com", "demouser@microsoft.com");

    private readonly MaxioStub _stub = new();
    private readonly ManualClock _clock = new(DateTimeOffset.Parse("2026-10-20T10:00:00Z"));
    private readonly MaxioSiteSettingsCache _siteSettings = new();
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public MaxioSubscriptionBillingServiceTests()
    {
        _stub.On(ProductsRoute, HttpStatusCode.OK, ProductsJson)
             .On(SiteRoute, HttpStatusCode.OK, """{ "site": { "id": 1, "relationship_invoicing_enabled": true } }""")
             .On(CustomerLookupRoute, HttpStatusCode.NotFound, "{}")
             .On(CreateCustomerRoute, HttpStatusCode.Created, CustomerJson)
             .On(SubscriptionLookupRoute, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))
             .On(CreateSubscriptionRoute, HttpStatusCode.Created, SubscriptionJson(7, "eshop-pro"));
    }

    [Fact]
    public async Task ListsOfferedPlansExcludingArchivedProducts()
    {
        var catalog = await CreateService().ListPlansAsync();

        Assert.False(catalog.IsTruncated);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        var pro = catalog.Plans[0];
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task FlagsPlanListAsTruncatedWhenThePageCapIsHit()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, 200).Select(i =>
            $$"""{ "product": { "id": {{i}}, "name": "P{{i}}", "handle": "p-{{i}}" } }""")) + "]";
        _stub.On(ProductsRoute, HttpStatusCode.OK, fullPage);
        var options = DefaultOptions();
        options.MaxPlanPages = 2;

        var catalog = await CreateService(options: options).ListPlansAsync();

        Assert.True(catalog.IsTruncated);
        Assert.Equal(400, catalog.Plans.Count);
        Assert.Equal(2, _stub.Count(ProductsRoute));
    }

    [Fact]
    public async Task UsesBaseUrlVerbatimWhenConfigured()
    {
        var seen = default(Uri);
        _stub.On(ProductsRoute, request =>
        {
            seen = request.RequestUri;
            return Task.FromResult(MaxioStub.Json(HttpStatusCode.OK, ProductsJson));
        });

        await CreateService().ListPlansAsync();

        Assert.Equal("maxio.test", seen!.Host);
        Assert.Contains("per_page=200", seen.Query);
    }

    [Fact]
    public async Task RejectsAPlanOutsideTheConfiguredFamilyWithoutWriting()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotOfferedException>(() => CreateService().SubscribeAsync(Buyer, "api-call"));

        Assert.Equal(0, _stub.Count(CreateCustomerRoute));
        Assert.Equal(0, _stub.Count(CreateSubscriptionRoute));
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWithDeterministicReferences()
    {
        var result = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.Equal(SubscribeStatus.Created, result.Status);
        Assert.Equal(7, result.Subscription!.Id);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingAt);

        var customerBody = _stub.LastBody(CreateCustomerRoute)!;
        Assert.Contains($"\"reference\":\"{BillingReferences.ForCustomer(Buyer.UserName)}\"", customerBody);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", customerBody);

        var subscriptionBody = _stub.LastBody(CreateSubscriptionRoute)!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody);
        Assert.Contains("\"customer_id\":42", subscriptionBody);
        Assert.Contains($"\"reference\":\"{result.Reference}\"", subscriptionBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionBody);
        Assert.Equal(BillingReferences.ForSubscription(Buyer.UserName, "eshop-pro"), result.Reference);
    }

    [Fact]
    public async Task UsesInvoiceCollectionOnLegacyStatementsSites()
    {
        _stub.On(SiteRoute, HttpStatusCode.OK, """{ "site": { "id": 1, "relationship_invoicing_enabled": false } }""");

        await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.Contains("\"payment_collection_method\":\"invoice\"", _stub.LastBody(CreateSubscriptionRoute));
    }

    [Fact]
    public async Task ReusesAnExistingCustomerFoundByReference()
    {
        _stub.On(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson);

        var result = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.Equal(SubscribeStatus.Created, result.Status);
        Assert.Equal(0, _stub.Count(CreateCustomerRoute));
    }

    [Fact]
    public async Task RepeatedSubscribeReturnsTheSameSubscriptionWithoutASecondCreate()
    {
        _stub.On("GET /subscriptions/7.json", HttpStatusCode.OK, SubscriptionJson(7, "eshop-pro"));

        var first = await CreateService().SubscribeAsync(Buyer, "eshop-pro");
        var second = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.Equal(SubscribeStatus.Created, first.Status);
        Assert.Equal(SubscribeStatus.AlreadySubscribed, second.Status);
        Assert.Equal(first.Subscription!.Id, second.Subscription!.Id);
        Assert.Equal(1, _stub.Count(CreateSubscriptionRoute));
        Assert.Equal(1, _stub.Count(CreateCustomerRoute));
    }

    [Fact]
    public async Task ConcurrentSubscribesReachMaxioOnlyOnce()
    {
        var release = new TaskCompletionSource();
        _stub.On(CreateSubscriptionRoute, async _ =>
        {
            await release.Task;
            return MaxioStub.Json(HttpStatusCode.Created, SubscriptionJson(7, "eshop-pro"));
        });
        _stub.On(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson);

        var first = CreateService().SubscribeAsync(Buyer, "eshop-pro");
        await WaitUntil(() => _stub.Count(CreateSubscriptionRoute) == 1);
        var second = await CreateService().SubscribeAsync(Buyer, "eshop-pro");
        release.SetResult();
        var firstResult = await first;

        Assert.Equal(SubscribeStatus.Created, firstResult.Status);
        Assert.Equal(SubscribeStatus.Pending, second.Status);
        Assert.Null(second.Subscription);
        Assert.Equal(1, _stub.Count(CreateSubscriptionRoute));
    }

    [Fact]
    public async Task SettlesADroppedCreateSubscriptionByLookingItUp()
    {
        var created = false;
        _stub.On(CreateSubscriptionRoute, _ =>
        {
            created = true; // Maxio acted, then the connection died before the response arrived.
            throw new HttpRequestException("connection reset");
        });
        _stub.On(SubscriptionLookupRoute, _ => Task.FromResult(created
            ? MaxioStub.Json(HttpStatusCode.OK, SubscriptionJson(7, "eshop-pro"))
            : new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.Equal(SubscribeStatus.Created, result.Status);
        Assert.Equal(7, result.Subscription!.Id);
        Assert.Equal(1, _stub.Count(CreateSubscriptionRoute));
        Assert.Equal(BillingClaimState.Completed, (await LoadClaim(result.Reference)).State);
    }

    [Fact]
    public async Task UnsettledCreateIsPendingAndIsNotResentUntilItsLeaseExpires()
    {
        _stub.On(CreateSubscriptionRoute, _ => throw new HttpRequestException("connection reset"));

        var first = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.Equal(SubscribeStatus.Pending, first.Status);
        Assert.Equal(BillingClaimState.Unknown, (await LoadClaim(first.Reference)).State);

        // Inside the lease: no second write.
        _clock.Advance(TimeSpan.FromSeconds(30));
        var second = await CreateService().SubscribeAsync(Buyer, "eshop-pro");
        Assert.Equal(SubscribeStatus.Pending, second.Status);
        Assert.Equal(1, _stub.Count(CreateSubscriptionRoute));

        // After the lease: look up first (not found), then create once more.
        _clock.Advance(TimeSpan.FromMinutes(2));
        _stub.On(CreateSubscriptionRoute, HttpStatusCode.Created, SubscriptionJson(7, "eshop-pro"));
        var third = await CreateService().SubscribeAsync(Buyer, "eshop-pro");
        Assert.Equal(SubscribeStatus.Created, third.Status);
        Assert.Equal(2, _stub.Count(CreateSubscriptionRoute));
    }

    [Fact]
    public async Task SettlesADroppedCreateCustomerByLookingItUp()
    {
        var created = false;
        _stub.On(CreateCustomerRoute, _ =>
        {
            created = true;
            throw new HttpRequestException("connection reset");
        });
        _stub.On(CustomerLookupRoute, _ => Task.FromResult(created
            ? MaxioStub.Json(HttpStatusCode.OK, CustomerJson)
            : MaxioStub.Json(HttpStatusCode.NotFound, "{}")));

        var result = await CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.Equal(SubscribeStatus.Created, result.Status);
        Assert.Equal(1, _stub.Count(CreateCustomerRoute));
        Assert.Contains("\"customer_id\":42", _stub.LastBody(CreateSubscriptionRoute));
    }

    [Fact]
    public async Task ARefusedSubscriptionIsReportedAndReleasedForARetry()
    {
        _stub.On(CreateSubscriptionRoute, HttpStatusCode.UnprocessableEntity,
            """{ "errors": ["No payment method was on file for the $299.00 balance"] }""");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => CreateService().SubscribeAsync(Buyer, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.Contains("No payment method was on file for the $299.00 balance", ex.Errors);
        Assert.Equal(BillingClaimState.Released,
            (await LoadClaim(BillingReferences.ForSubscription(Buyer.UserName, "eshop-pro"))).State);

        _stub.On(CreateSubscriptionRoute, HttpStatusCode.Created, SubscriptionJson(7, "eshop-pro"));
        var retry = await CreateService().SubscribeAsync(Buyer, "eshop-pro");
        Assert.Equal(SubscribeStatus.Created, retry.Status);
    }

    [Fact]
    public async Task OurRejectedCredentialsSurfaceAsUnavailableNotAsTheCallersFault()
    {
        _stub.On(ProductsRoute, HttpStatusCode.Unauthorized, "{}");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => CreateService().ListPlansAsync());

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.Equal(401, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task AnUnreachableProviderSurfacesAsUnavailable()
    {
        _stub.On(ProductsRoute, _ => throw new HttpRequestException("no route to host"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => CreateService().ListPlansAsync());

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task ListsOnlyTheBuyersOwnSubscriptions()
    {
        _stub.On(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson);
        _stub.On("GET /customers/42/subscriptions.json", HttpStatusCode.OK,
            "[" + SubscriptionJson(7, "eshop-pro") + "," + SubscriptionJson(8, "basic-plan") + "]");

        var subscriptions = await CreateService().ListSubscriptionsAsync(Buyer);

        Assert.Equal(new[] { 7, 8 }, subscriptions.Select(s => s.Id).OrderBy(i => i));
        Assert.Equal(0, _stub.Count(CreateCustomerRoute));
    }

    [Fact]
    public async Task ListIsEmptyForABuyerWithNoMaxioCustomer()
    {
        var subscriptions = await CreateService().ListSubscriptionsAsync(Buyer);

        Assert.Empty(subscriptions);
        Assert.Equal(0, _stub.Count(CreateCustomerRoute));
    }

    [Fact]
    public async Task TheStoreRefusesASecondClaimForTheSameReference()
    {
        var first = new BillingClaimStore(NewContext(), _clock, NullLogger<BillingClaimStore>.Instance);
        var second = new BillingClaimStore(NewContext(), _clock, NullLogger<BillingClaimStore>.Instance);

        var a = await first.TryAcquireAsync("ref-1", BillingClaimKind.Subscription, "buyer", "eshop-pro", CancellationToken.None);
        var b = await second.TryAcquireAsync("ref-1", BillingClaimKind.Subscription, "buyer", "eshop-pro", CancellationToken.None);

        Assert.True(a.Acquired);
        Assert.False(b.Acquired);
        Assert.Equal(a.Claim.ClaimToken, b.Claim.ClaimToken);
    }

    [Fact]
    public async Task OnlyOneRequestCanTakeOverAnExpiredClaim()
    {
        var owner = new BillingClaimStore(NewContext(), _clock, NullLogger<BillingClaimStore>.Instance);
        var (_, claim) = await owner.TryAcquireAsync("ref-2", BillingClaimKind.Subscription, "buyer", "eshop-pro", CancellationToken.None);
        await owner.MarkUnknownAsync(claim);

        var storeA = new BillingClaimStore(NewContext(), _clock, NullLogger<BillingClaimStore>.Instance);
        var storeB = new BillingClaimStore(NewContext(), _clock, NullLogger<BillingClaimStore>.Instance);
        var (_, seenByA) = await storeA.TryAcquireAsync("ref-2", BillingClaimKind.Subscription, "buyer", "eshop-pro", CancellationToken.None);
        var (_, seenByB) = await storeB.TryAcquireAsync("ref-2", BillingClaimKind.Subscription, "buyer", "eshop-pro", CancellationToken.None);

        Assert.True(await storeA.TryTakeOverAsync(seenByA, CancellationToken.None));
        Assert.False(await storeB.TryTakeOverAsync(seenByB, CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "site", "family", null, false)]
    [InlineData("key", null, "family", null, false)]
    [InlineData("key", "site", null, null, false)]
    [InlineData("key", "site", "family", "not a url", false)]
    [InlineData("key", null, "family", "https://maxio.test", true)]
    [InlineData("key", "site", "family", null, true)]
    public void ValidatesRequiredSettings(string? apiKey, string? subdomain, string? family, string? baseUrl, bool valid)
    {
        var options = new MaxioOptions { ApiKey = apiKey, Subdomain = subdomain, ProductFamilyHandle = family, BaseUrl = baseUrl };

        var result = new MaxioOptionsValidator().Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void ValidationFailuresNameTheKeyButNeverEchoAValue()
    {
        var options = new MaxioOptions { ApiKey = "super-secret-value", Subdomain = null, ProductFamilyHandle = null };

        var result = new MaxioOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Maxio:ProductFamilyHandle", result.FailureMessage);
        Assert.DoesNotContain("super-secret-value", result.FailureMessage);
    }

    private MaxioSubscriptionBillingService CreateService(MaxioOptions? options = null)
    {
        var settings = options ?? DefaultOptions();
        var clientOptions = MaxioBillingServiceCollectionExtensions.CreateClientOptions(settings, NullLoggerFactory.Instance, TimeProvider.System);
        var client = new MaxioAdvancedBillingClient(new HttpClient(_stub, disposeHandler: false), clientOptions);
        var claims = new BillingClaimStore(NewContext(), _clock, NullLogger<BillingClaimStore>.Instance);
        return new MaxioSubscriptionBillingService(client, claims, _siteSettings, Options.Create(settings), _clock,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static MaxioOptions DefaultOptions() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "test-family",
        BaseUrl = "https://maxio.test"
    };

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    private async Task<BillingClaim> LoadClaim(string reference)
    {
        using var db = NewContext();
        return await db.BillingClaims.AsNoTracking().SingleAsync(c => c.Reference == reference);
    }

    private static string SubscriptionJson(int id, string handle) => $$"""
        {
          "subscription": {
            "id": {{id}},
            "state": "active",
            "product_price_in_cents": 29900,
            "currency": "USD",
            "reference": "{{BillingReferences.ForSubscription(Buyer.UserName, handle)}}",
            "current_period_ends_at": "2026-11-20T10:00:00Z",
            "next_assessment_at": "2026-11-20T10:00:00Z",
            "created_at": "2026-10-20T10:00:00Z",
            "product": { "id": 1, "handle": "{{handle}}", "name": "Plan {{handle}}" },
            "customer": { "id": 42 }
          }
        }
        """;

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }
        Assert.True(condition());
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
