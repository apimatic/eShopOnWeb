using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string ProductsRoute = "GET /product_families/handle:eshop-subscribe/products.json";
    private const string SiteRoute = "GET /site.json";
    private const string CustomerLookupRoute = "GET /customers/lookup.json";
    private const string CreateCustomerRoute = "POST /customers.json";
    private const string CustomerSubscriptionsRoute = "GET /customers/900/subscriptions.json";
    private const string CreateSubscriptionRoute = "POST /subscriptions.json";

    private static readonly SubscriberIdentity Shopper = new()
    {
        UserId = "demouser@microsoft.com",
        Email = "demouser@microsoft.com"
    };

    private const string SiteJson = """
        {"site":{"id":1,"name":"Test","subdomain":"acme","currency":"USD","relationship_invoicing_enabled":true,
        "default_payment_collection_method":"automatic","test":true}}
        """;

    private const string ProductsJson = """
        [{"product":{"id":7,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,
        "interval_unit":"month","require_credit_card":false,"archived_at":null,
        "product_family":{"id":3,"name":"eShop Subscribe","handle":"eshop-subscribe"}}},
        {"product":{"id":8,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,
        "interval_unit":"month","require_credit_card":false,"archived_at":null,
        "product_family":{"id":3,"name":"eShop Subscribe","handle":"eshop-subscribe"}}}]
        """;

    private const string CustomerJson = """
        {"customer":{"id":900,"reference":"eshoponweb-demouser@microsoft.com","email":"demouser@microsoft.com",
        "first_name":"Demouser","last_name":"eShopOnWeb"}}
        """;

    private static string SubscriptionJson(long id, string state, string reference, string productHandle) =>
        "{\"subscription\":{" +
        $"\"id\":{id},\"state\":\"{state}\",\"reference\":\"{reference}\"," +
        "\"product_price_in_cents\":29900," +
        "\"current_period_started_at\":\"2026-09-06T12:00:00-05:00\"," +
        "\"current_period_ends_at\":\"2026-10-06T12:00:00-05:00\"," +
        "\"next_assessment_at\":\"2026-10-06T12:00:00-05:00\"," +
        "\"activated_at\":\"2026-09-06T12:00:00-05:00\"," +
        "\"created_at\":\"2026-09-06T12:00:00-05:00\"," +
        "\"customer\":{\"id\":900,\"reference\":\"eshoponweb-demouser@microsoft.com\"}," +
        $"\"product\":{{\"id\":7,\"name\":\"Pro Plan\",\"handle\":\"{productHandle}\"," +
        "\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}}";

    private static FakeMaxioHandler CatalogHandler() => new FakeMaxioHandler()
        .Map(SiteRoute, HttpStatusCode.OK, SiteJson)
        .Map(ProductsRoute, HttpStatusCode.OK, ProductsJson);

    private static MaxioSubscriptionBillingService Build(FakeMaxioHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe"
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
        var client = new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);

        return new MaxioSubscriptionBillingService(client, options, new MemoryCache(new MemoryCacheOptions()),
            new SubscriberKeyedLock(), NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListsPlansFromTheConfiguredProductFamilyCheapestFirst()
    {
        var service = Build(CatalogHandler());

        var plans = await service.ListPlansAsync();

        Assert.Collection(plans,
            plan => Assert.Equal("basic-plan", plan.Handle),
            plan => Assert.Equal("eshop-pro", plan.Handle));
        Assert.Equal(29900, plans[1].PriceInCents);
        Assert.Equal("USD", plans[1].Currency);
        Assert.False(plans[1].RequiresPaymentMethod);
    }

    [Fact]
    public async Task CachesThePlanCatalogAcrossCalls()
    {
        var handler = CatalogHandler();
        var service = Build(handler);

        await service.ListPlansAsync();
        await service.ListPlansAsync();

        Assert.Equal(1, handler.CountOf(ProductsRoute));
    }

    [Fact]
    public async Task RejectsAPlanThatIsNotInTheCatalog()
    {
        var service = Build(CatalogHandler());

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(Shopper, "no-such-plan"));
    }

    [Fact]
    public async Task CreatesTheCustomerAndTheSubscriptionOnAFirstSubscribe()
    {
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.NotFound, """{"errors":["Customer not found"]}""")
            .Map(CreateCustomerRoute, HttpStatusCode.Created, CustomerJson)
            .Map(CustomerSubscriptionsRoute, HttpStatusCode.OK, "[]")
            .Map(CreateSubscriptionRoute, HttpStatusCode.Created,
                SubscriptionJson(1, "active", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro"));

        var enrollment = await Build(handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(enrollment.AlreadyEnrolled);
        Assert.False(enrollment.CustomerAlreadyExisted);
        Assert.Equal(900, enrollment.Customer.Id);
        Assert.Equal("active", enrollment.Subscription.State);
        Assert.Equal("eshop-pro", enrollment.Subscription.PlanHandle);
        Assert.Equal(299m, enrollment.Subscription.Price);
        Assert.NotNull(enrollment.Subscription.NextBillingAt);
        Assert.Equal(1, handler.CountOf(CreateCustomerRoute));
        Assert.Equal(1, handler.CountOf(CreateSubscriptionRoute));
    }

    [Fact]
    public async Task BillsByRemittanceWhenThePlanNeedsNoPaymentMethod()
    {
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson)
            .Map(CustomerSubscriptionsRoute, HttpStatusCode.OK, "[]")
            .Map(CreateSubscriptionRoute, HttpStatusCode.Created,
                SubscriptionJson(1, "active", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro"));

        await Build(handler).SubscribeAsync(Shopper, "eshop-pro");

        var body = handler.RequestBodies[handler.Calls.IndexOf(CreateSubscriptionRoute)];
        using var document = JsonDocument.Parse(body);
        var subscription = document.RootElement.GetProperty("subscription");

        // Left on the site default of "automatic", Maxio would demand a payment profile at signup.
        Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());
        Assert.Equal("eshop-pro", subscription.GetProperty("product_handle").GetString());
        Assert.Equal(900, subscription.GetProperty("customer_id").GetInt64());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("uniqueness_token").GetString()));
    }

    [Fact]
    public async Task ReusesAnExistingCustomerInsteadOfCreatingASecondOne()
    {
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson)
            .Map(CustomerSubscriptionsRoute, HttpStatusCode.OK, "[]")
            .Map(CreateSubscriptionRoute, HttpStatusCode.Created,
                SubscriptionJson(1, "active", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro"));

        var enrollment = await Build(handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(enrollment.CustomerAlreadyExisted);
        Assert.Equal(0, handler.CountOf(CreateCustomerRoute));
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson)
            .Map(CustomerSubscriptionsRoute, HttpStatusCode.OK,
                $"[{SubscriptionJson(42, "active", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro")}]");

        var enrollment = await Build(handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(enrollment.AlreadyEnrolled);
        Assert.Equal(42, enrollment.Subscription.Id);
        Assert.Equal(0, handler.CountOf(CreateSubscriptionRoute));
    }

    [Fact]
    public async Task ADoubleClickedSubscribeCreatesOnlyOneSubscription()
    {
        var created = 0;
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson)
            .Map(CreateSubscriptionRoute, _ =>
            {
                Interlocked.Increment(ref created);
                return FakeMaxioHandler.Respond(HttpStatusCode.Created,
                    SubscriptionJson(7, "active", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro"));
            });

        // The customer has nothing until the first create lands, then has the new subscription -
        // exactly what the second click of a double-click sees.
        handler.Map(CustomerSubscriptionsRoute, _ => FakeMaxioHandler.Respond(HttpStatusCode.OK,
            Volatile.Read(ref created) == 0
                ? "[]"
                : $"[{SubscriptionJson(7, "active", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro")}]"));

        var service = Build(handler);

        var results = await Task.WhenAll(
            service.SubscribeAsync(Shopper, "eshop-pro"),
            service.SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Equal(1, created);
        Assert.All(results, result => Assert.Equal(7, result.Subscription.Id));
        Assert.Single(results.Where(result => !result.AlreadyEnrolled));
        Assert.Single(results.Where(result => result.AlreadyEnrolled));
    }

    [Fact]
    public async Task ACanceledSubscriptionDoesNotBlockReSubscribingAndGetsAFreshReference()
    {
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson)
            .Map(CustomerSubscriptionsRoute, HttpStatusCode.OK,
                $"[{SubscriptionJson(42, "canceled", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro")}]")
            .Map(CreateSubscriptionRoute, HttpStatusCode.Created,
                SubscriptionJson(43, "active", "eshoponweb-demouser@microsoft.com:eshop-pro:2", "eshop-pro"));

        var enrollment = await Build(handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(enrollment.AlreadyEnrolled);
        Assert.Equal(43, enrollment.Subscription.Id);

        var body = handler.RequestBodies[handler.Calls.IndexOf(CreateSubscriptionRoute)];
        using var document = JsonDocument.Parse(body);

        // The old reference is taken, so the new enrollment must not collide with it.
        Assert.Equal("eshoponweb-demouser@microsoft.com:eshop-pro:2",
            document.RootElement.GetProperty("subscription").GetProperty("reference").GetString());
    }

    [Fact]
    public async Task ADuplicateTokenThatCreatedSomethingReturnsThatSubscription()
    {
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson)
            .Map(CreateSubscriptionRoute, HttpStatusCode.Conflict,
                """{"errors":["DuplicatePrevention::DuplicateSubmissionError"]}""");

        var attempt = 0;
        handler.Map(CustomerSubscriptionsRoute, _ => FakeMaxioHandler.Respond(HttpStatusCode.OK,
            attempt++ == 0
                ? "[]"
                : $"[{SubscriptionJson(55, "active", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro")}]"));

        var enrollment = await Build(handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(enrollment.AlreadyEnrolled);
        Assert.Equal(55, enrollment.Subscription.Id);
        Assert.Equal(1, handler.CountOf(CreateSubscriptionRoute));
    }

    [Fact]
    public async Task ADuplicateTokenThatCreatedNothingIsRetriedWithAFreshToken()
    {
        var attempts = 0;
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson)
            .Map(CustomerSubscriptionsRoute, HttpStatusCode.OK, "[]")
            .Map(CreateSubscriptionRoute, _ => ++attempts == 1
                ? FakeMaxioHandler.Respond(HttpStatusCode.Conflict,
                    """{"errors":["DuplicatePrevention::DuplicateSubmissionError"]}""")
                : FakeMaxioHandler.Respond(HttpStatusCode.Created,
                    SubscriptionJson(60, "active", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro")));

        var enrollment = await Build(handler).SubscribeAsync(Shopper, "eshop-pro");

        // A stale token from an attempt that failed outright must not lock the shopper out for an hour.
        Assert.False(enrollment.AlreadyEnrolled);
        Assert.Equal(60, enrollment.Subscription.Id);
        Assert.Equal(2, attempts);

        var first = JsonDocument.Parse(handler.RequestBodies[handler.Calls.IndexOf(CreateSubscriptionRoute)]);
        var second = JsonDocument.Parse(handler.RequestBodies[handler.Calls.LastIndexOf(CreateSubscriptionRoute)]);
        Assert.NotEqual(first.RootElement.GetProperty("uniqueness_token").GetString(),
            second.RootElement.GetProperty("uniqueness_token").GetString());
    }

    [Fact]
    public async Task AConcurrentCustomerCreateFallsBackToTheCustomerThatWon()
    {
        var lookups = 0;
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, _ => lookups++ == 0
                ? FakeMaxioHandler.Respond(HttpStatusCode.NotFound, """{"errors":["Customer not found"]}""")
                : FakeMaxioHandler.Respond(HttpStatusCode.OK, CustomerJson))
            .Map(CreateCustomerRoute, HttpStatusCode.UnprocessableEntity,
                """{"errors":{"reference":"must be unique"}}""")
            .Map(CustomerSubscriptionsRoute, HttpStatusCode.OK, "[]")
            .Map(CreateSubscriptionRoute, HttpStatusCode.Created,
                SubscriptionJson(1, "active", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro"));

        var enrollment = await Build(handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(enrollment.CustomerAlreadyExisted);
        Assert.Equal(900, enrollment.Customer.Id);
    }

    [Fact]
    public async Task AShopperWithNoBillingCustomerHasNoSubscriptions()
    {
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.NotFound, """{"errors":["Customer not found"]}""");

        var subscriptions = await Build(handler).ListSubscriptionsAsync(Shopper);

        Assert.Empty(subscriptions);
        Assert.Equal(0, handler.CountOf(CustomerSubscriptionsRoute));
    }

    [Fact]
    public async Task ListsTheShopperSubscriptionsNewestFirst()
    {
        var older = SubscriptionJson(1, "canceled", "eshoponweb-demouser@microsoft.com:basic-plan", "basic-plan")
            .Replace("\"created_at\":\"2026-09-06T12:00:00-05:00\"", "\"created_at\":\"2026-01-06T12:00:00-05:00\"");

        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson)
            .Map(CustomerSubscriptionsRoute, HttpStatusCode.OK,
                $"[{older},{SubscriptionJson(2, "active", "eshoponweb-demouser@microsoft.com:eshop-pro", "eshop-pro")}]");

        var subscriptions = await Build(handler).ListSubscriptionsAsync(Shopper);

        Assert.Equal(new long[] { 2, 1 }, subscriptions.Select(subscription => subscription.Id));
        Assert.True(subscriptions[0].IsLive);
        Assert.False(subscriptions[1].IsLive);
    }

    [Fact]
    public async Task SurfacesTheBillingSystemErrorMessages()
    {
        var handler = CatalogHandler()
            .Map(CustomerLookupRoute, HttpStatusCode.OK, CustomerJson)
            .Map(CustomerSubscriptionsRoute, HttpStatusCode.OK, "[]")
            .Map(CreateSubscriptionRoute, HttpStatusCode.UnprocessableEntity,
                """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var exception = await Assert.ThrowsAsync<BillingApiException>(
            () => Build(handler).SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.True(exception.IsCallerFault);
        Assert.Contains("No payment method was on file", exception.Message);
    }

    [Fact]
    public async Task ReportsAMissingProductFamilyAsAConfigurationProblem()
    {
        var handler = new FakeMaxioHandler()
            .Map(SiteRoute, HttpStatusCode.OK, SiteJson)
            .Map(ProductsRoute, HttpStatusCode.NotFound, """{"errors":["Product family not found"]}""");

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Build(handler).ListPlansAsync());

        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }
}
