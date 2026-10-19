using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionServiceTests
{
    private const string Family = "test-family";
    private static readonly Subscriber Shopper = new("shopper@example.com", "shopper@example.com");
    private const string CustomerReference = "eshop-shopper@example.com";

    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly string _databaseName = Guid.NewGuid().ToString();

    // ------------------------------------------------------------ plans

    [Fact]
    public async Task GetPlans_MapsActiveProductsOfConfiguredFamily()
    {
        var handler = new StubHandler(request => request.Path() switch
        {
            "/product_families/handle:test-family/products.json" => Json(HttpStatusCode.OK, ProductsJson(
                Product("eshop-pro", "Pro Plan", 29900),
                Product("old-plan", "Old", 100, archived: true),
                Product("basic-plan", "Basic Plan", 2900))),
            _ => Unexpected(request)
        });

        var catalog = await CreateService(handler).GetPlansAsync();

        Assert.True(catalog.IsComplete);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        Assert.Equal(29900, catalog.Plans[0].PriceInCents);
        Assert.Equal("month", catalog.Plans[0].IntervalUnit);
        Assert.Contains("per_page=200", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task GetPlans_FlagsTruncatedCatalogWhenPageCapIsHit()
    {
        var page = 0;
        var handler = new StubHandler(request =>
        {
            page++;
            var products = Enumerable.Range(0, MaxioSubscriptionService.PlanPageSize)
                .Select(i => Product($"plan-{page}-{i}", "Plan", 100)).ToArray();
            return Json(HttpStatusCode.OK, ProductsJson(products));
        });

        var catalog = await CreateService(handler).GetPlansAsync();

        Assert.False(catalog.IsComplete);
        Assert.Equal(MaxioSubscriptionService.MaxPlanPages, handler.Requests.Count);
    }

    // ------------------------------------------------------------ subscribe

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription()
    {
        var maxio = new FakeMaxio();
        var service = CreateService(maxio.Handler);

        var outcome = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeStatus.Created, outcome.Status);
        Assert.Equal(maxio.SubscriptionId, outcome.Subscription!.Id);
        Assert.Equal("active", outcome.Subscription.State);
        Assert.Equal(29900, outcome.Subscription.PriceInCents);
        Assert.NotNull(outcome.Subscription.NextBillingAt);
        Assert.Equal(1, maxio.CustomerCreates);
        Assert.Equal(1, maxio.SubscriptionCreates);
        Assert.Contains($"\"reference\":\"{CustomerReference}\"", maxio.CustomerCreateBody);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", maxio.SubscriptionCreateBody);
        Assert.Contains($"\"customer_id\":{maxio.CustomerId}", maxio.SubscriptionCreateBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", maxio.SubscriptionCreateBody);

        await using var db = CreateContext();
        var enrollment = await db.SubscriptionEnrollments.SingleAsync();
        Assert.Equal(BillingClaimState.Active, enrollment.State);
        Assert.Equal(maxio.SubscriptionId, enrollment.ProviderSubscriptionId);
        Assert.Equal(enrollment.SubscriptionReference, outcome.SubscriptionReference);
        Assert.Contains($"\"reference\":\"{enrollment.SubscriptionReference}\"", maxio.SubscriptionCreateBody);
    }

    [Fact]
    public async Task Subscribe_OnLegacyStatementsSite_InvoicesWithInvoiceCollectionMethod()
    {
        var maxio = new FakeMaxio { ExistingCustomer = true, RelationshipInvoicing = false };

        await CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.Contains("\"payment_collection_method\":\"invoice\"", maxio.SubscriptionCreateBody);
    }

    [Fact]
    public async Task Subscribe_Twice_ReturnsExistingWithoutSecondCreate()
    {
        var maxio = new FakeMaxio();

        var first = await CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro");
        var second = await CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeStatus.Created, first.Status);
        Assert.Equal(SubscribeStatus.AlreadySubscribed, second.Status);
        Assert.Equal(first.Subscription!.Id, second.Subscription!.Id);
        Assert.Equal(1, maxio.CustomerCreates);
        Assert.Equal(1, maxio.SubscriptionCreates);
    }

    [Fact]
    public async Task Subscribe_WhileAnotherRequestHoldsTheClaim_IsRejectedBeforeReachingMaxio()
    {
        var maxio = new FakeMaxio { ExistingCustomer = true };
        await using (var db = CreateContext())
        {
            db.SubscriptionEnrollments.Add(new SubscriptionEnrollment(CustomerReference, "eshop-pro", _clock.GetUtcNow()));
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<BillingException>(() => CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Conflict, ex.Kind);
        Assert.Equal(0, maxio.SubscriptionCreates);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_IsInvalidAndWritesNothing()
    {
        var maxio = new FakeMaxio();

        var ex = await Assert.ThrowsAsync<BillingException>(() => CreateService(maxio.Handler).SubscribeAsync(Shopper, "no-such-plan"));

        Assert.Equal(BillingFailureKind.InvalidRequest, ex.Kind);
        Assert.Equal(0, maxio.CustomerCreates);
        Assert.Equal(0, maxio.SubscriptionCreates);
    }

    [Fact]
    public async Task Subscribe_ExistingLiveSubscriptionAtMaxio_IsAdoptedNotDuplicated()
    {
        // Local store lost (e.g. in-memory database restarted) but Maxio already holds the subscription.
        var maxio = new FakeMaxio { ExistingCustomer = true, ExistingSubscription = true };

        var outcome = await CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeStatus.AlreadySubscribed, outcome.Status);
        Assert.Equal("external", outcome.SubscriptionReference);
        Assert.Equal(0, maxio.CustomerCreates);
        Assert.Equal(0, maxio.SubscriptionCreates);
    }

    [Fact]
    public async Task Subscribe_ProviderValidationError_IsRejectedAndReleasesTheClaim()
    {
        var maxio = new FakeMaxio { ExistingCustomer = true, CreateSubscriptionStatus = HttpStatusCode.UnprocessableEntity };

        var ex = await Assert.ThrowsAsync<BillingException>(() => CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Contains("Product must be active", ex.Errors);
        await using var db = CreateContext();
        Assert.Empty(db.SubscriptionEnrollments);
    }

    [Fact]
    public async Task Subscribe_ConnectionFailsOnCreate_SettlesByReferenceWithoutResending()
    {
        var maxio = new FakeMaxio { ExistingCustomer = true, CreateSubscriptionThrowsAfterLanding = true };

        var outcome = await CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeStatus.Created, outcome.Status);
        Assert.Equal(maxio.SubscriptionId, outcome.Subscription!.Id);
        Assert.Equal(1, maxio.SubscriptionCreates);
        Assert.Equal(1, maxio.SubscriptionLookups);
        Assert.Equal(outcome.SubscriptionReference, maxio.LastLookupReference);

        await using var db = CreateContext();
        Assert.Equal(BillingClaimState.Active, (await db.SubscriptionEnrollments.SingleAsync()).State);
    }

    [Fact]
    public async Task Subscribe_ConnectionFailsAndLookupCannotConfirm_ReturnsPendingAndSweeperSettlesLater()
    {
        var maxio = new FakeMaxio { ExistingCustomer = true, CreateSubscriptionThrowsAfterLanding = true, LookupHidesSubscription = true };

        var outcome = await CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeStatus.Pending, outcome.Status);
        await using (var db = CreateContext())
        {
            Assert.Equal(BillingClaimState.Unknown, (await db.SubscriptionEnrollments.SingleAsync()).State);
        }

        // A retry inside the lease must not resend the create.
        var retry = await Assert.ThrowsAsync<BillingException>(() => CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro"));
        Assert.Equal(BillingFailureKind.Conflict, retry.Kind);
        Assert.Equal(1, maxio.SubscriptionCreates);

        // Once the lease has passed, the sweeper re-reads Maxio by reference and records the subscription.
        maxio.LookupHidesSubscription = false;
        _clock.Advance(MaxioSubscriptionService.ClaimLease + TimeSpan.FromSeconds(1));
        var settled = await CreateService(maxio.Handler).SettleUnresolvedClaimsAsync();

        Assert.Equal(1, settled);
        await using (var db = CreateContext())
        {
            var enrollment = await db.SubscriptionEnrollments.SingleAsync();
            Assert.Equal(BillingClaimState.Active, enrollment.State);
            Assert.Equal(maxio.SubscriptionId, enrollment.ProviderSubscriptionId);
        }

        Assert.Equal(1, maxio.SubscriptionCreates);
    }

    [Fact]
    public async Task Subscribe_ConnectionFailsOnCustomerCreate_SettlesByCustomerReference()
    {
        var maxio = new FakeMaxio { CreateCustomerThrowsAfterLanding = true };

        var outcome = await CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeStatus.Created, outcome.Status);
        Assert.Equal(1, maxio.CustomerCreates);
        await using var db = CreateContext();
        var customer = await db.BillingCustomers.SingleAsync();
        Assert.Equal(BillingClaimState.Active, customer.State);
        Assert.Equal(maxio.CustomerId, customer.ProviderCustomerId);
    }

    [Fact]
    public async Task Subscribe_CustomerLookupFails_IsUnavailableAndNeverCreatesACustomer()
    {
        var maxio = new FakeMaxio { CustomerLookupStatus = HttpStatusCode.InternalServerError };

        var ex = await Assert.ThrowsAsync<BillingException>(() => CreateService(maxio.Handler).SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.Equal(0, maxio.CustomerCreates);
        await using var db = CreateContext();
        Assert.Empty(db.BillingCustomers);
    }

    // ------------------------------------------------------------ my subscriptions

    [Fact]
    public async Task GetSubscriptions_UnknownCustomer_ReturnsEmpty()
    {
        var maxio = new FakeMaxio();

        var subscriptions = await CreateService(maxio.Handler).GetSubscriptionsAsync(Shopper);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsPlanPriceStateAndNextBillingDate()
    {
        var maxio = new FakeMaxio { ExistingCustomer = true, ExistingSubscription = true };

        var subscriptions = await CreateService(maxio.Handler).GetSubscriptionsAsync(Shopper);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal("active", subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingAt);
    }

    // ------------------------------------------------------------ options

    [Fact]
    public void OptionsValidator_RequiresApiKeyAndSiteAndFamily()
    {
        var result = new MaxioOptionsValidator().Validate(null, new MaxioOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:ApiKey"));
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:Subdomain"));
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public async Task BaseUrl_WhenSet_IsUsedVerbatimInsteadOfSubdomain()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[]"));
        var options = new MaxioOptions { ApiKey = "k", Subdomain = "ignored", ProductFamilyHandle = Family, BaseUrl = "http://billing.example.test:8080" };

        await CreateService(handler, options).GetPlansAsync();

        Assert.Equal("billing.example.test", handler.Requests[0].RequestUri!.Host);
        Assert.Equal(8080, handler.Requests[0].RequestUri!.Port);
        Assert.True(new MaxioOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public async Task Subdomain_IsUsedForTheSiteHost()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[]"));
        var options = new MaxioOptions { ApiKey = "k", Subdomain = "acme", ProductFamilyHandle = Family };

        await CreateService(handler, options).GetPlansAsync();

        Assert.Equal("acme.chargify.com", handler.Requests[0].RequestUri!.Host);
        Assert.Equal("Basic", handler.Requests[0].Headers.Authorization!.Scheme);
    }

    // ------------------------------------------------------------ plumbing

    private MaxioSubscriptionService CreateService(StubHandler handler, MaxioOptions? options = null)
    {
        options ??= new MaxioOptions { ApiKey = "test-key", BaseUrl = "https://maxio.test", ProductFamilyHandle = Family };
        var client = MaxioBillingServiceCollectionExtensions.CreateClient(
            new HttpClient(handler), options, NullLoggerFactory.Instance, TimeProvider.System);
        return new MaxioSubscriptionService(
            client,
            new BillingClaimStore(CreateContext(), _clock),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    private CatalogContext CreateContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    private static string Product(string handle, string name, long priceInCents, bool archived = false) => $$"""
        {"product":{"id":{{Math.Abs(handle.GetHashCode() % 100000)}},"handle":"{{handle}}","name":"{{name}}","price_in_cents":{{priceInCents}},
        "interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":{{(archived ? "\"2026-01-01T00:00:00Z\"" : "null")}},
        "product_family":{"handle":"{{Family}}"} } }
        """;

    private static string ProductsJson(params string[] products) => "[" + string.Join(",", products) + "]";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Unexpected(HttpRequestMessage request) =>
        throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");

    /// <summary>An in-process stand-in for the Maxio routes the service uses.</summary>
    private sealed class FakeMaxio
    {
        public int CustomerId { get; } = 4242;
        public int SubscriptionId { get; } = 9001;
        public bool ExistingCustomer { get; set; }
        public bool ExistingSubscription { get; set; }
        public bool CreateSubscriptionThrowsAfterLanding { get; set; }
        public bool CreateCustomerThrowsAfterLanding { get; set; }
        public bool LookupHidesSubscription { get; set; }
        public bool RelationshipInvoicing { get; set; } = true;
        public HttpStatusCode? CreateSubscriptionStatus { get; set; }
        public HttpStatusCode? CustomerLookupStatus { get; set; }
        public int CustomerCreates { get; private set; }
        public int SubscriptionCreates { get; private set; }
        public int SubscriptionLookups { get; private set; }
        public string? CustomerCreateBody { get; private set; }
        public string? SubscriptionCreateBody { get; private set; }
        public string? LastLookupReference { get; private set; }
        private string? _subscriptionReference;

        public StubHandler Handler => new(Respond);

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.Path();
            var body = request.Content?.ReadAsStringAsync().Result;

            if (path == $"/product_families/handle:{Family}/products.json")
                return Json(HttpStatusCode.OK, ProductsJson(Product("eshop-pro", "Pro Plan", 29900), Product("basic-plan", "Basic Plan", 2900)));

            if (path == "/site.json")
                return Json(HttpStatusCode.OK, $$"""{"site":{"id":1,"subdomain":"test","relationship_invoicing_enabled":{{(RelationshipInvoicing ? "true" : "false")}}} }""");

            if (path == "/customers/lookup.json")
            {
                if (CustomerLookupStatus is { } status)
                    return Json(status, "{}");
                return ExistingCustomer ? Json(HttpStatusCode.OK, CustomerJson()) : Json(HttpStatusCode.NotFound, "");
            }

            if (path == "/customers.json" && request.Method == HttpMethod.Post)
            {
                CustomerCreates++;
                CustomerCreateBody = body;
                ExistingCustomer = true;
                if (CreateCustomerThrowsAfterLanding)
                    throw new HttpRequestException("connection reset after the customer was created");
                return Json(HttpStatusCode.Created, CustomerJson());
            }

            if (path == $"/customers/{CustomerId}/subscriptions.json")
                return Json(HttpStatusCode.OK, ExistingSubscription ? $"[{SubscriptionJson()}]" : "[]");

            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
            {
                SubscriptionCreates++;
                SubscriptionCreateBody = body;
                if (CreateSubscriptionStatus is { } status)
                    return Json(status, """{"errors":["Product must be active"]}""");

                _subscriptionReference = System.Text.Json.JsonDocument.Parse(body!).RootElement
                    .GetProperty("subscription").GetProperty("reference").GetString();
                ExistingSubscription = true;
                if (CreateSubscriptionThrowsAfterLanding)
                    throw new HttpRequestException("connection reset after the subscription was created");
                return Json(HttpStatusCode.Created, SubscriptionJson());
            }

            if (path == "/subscriptions/lookup.json")
            {
                SubscriptionLookups++;
                LastLookupReference = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["reference"];
                return ExistingSubscription && !LookupHidesSubscription && LastLookupReference == _subscriptionReference
                    ? Json(HttpStatusCode.OK, SubscriptionJson())
                    : Json(HttpStatusCode.NotFound, "");
            }

            if (path == $"/subscriptions/{SubscriptionId}.json")
                return ExistingSubscription ? Json(HttpStatusCode.OK, SubscriptionJson()) : Json(HttpStatusCode.NotFound, "");

            return Unexpected(request);
        }

        private string CustomerJson() =>
            $$"""{"customer":{"id":{{CustomerId}},"reference":"{{CustomerReference}}","email":"shopper@example.com","first_name":"shopper","last_name":"eShop customer"} }""";

        private string SubscriptionJson() => $$"""
            {"subscription":{"id":{{SubscriptionId}},"state":"active","reference":"{{_subscriptionReference ?? "external"}}",
            "product_price_in_cents":29900,"currency":"USD","current_period_ends_at":"2026-11-19T00:00:00Z",
            "next_assessment_at":"2026-11-19T00:00:00Z","created_at":"2026-10-19T12:00:00Z",
            "product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"product_family":{"handle":"{{Family}}"} } } }
            """;
    }
}

internal static class HttpRequestMessageTestExtensions
{
    public static string Path(this HttpRequestMessage request) => Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
