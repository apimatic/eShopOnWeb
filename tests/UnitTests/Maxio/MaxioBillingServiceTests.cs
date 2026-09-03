using System.Net;
using System.Net.Http;
using global::Maxio;
using global::Maxio.Core.Authentication.Basic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioBillingServiceTests
{
    private const string User = "demouser@microsoft.com";

    private static MaxioBillingService BuildService(RoutingStubHandler handler, MaxioSettings? settings = null)
    {
        var client = new MaxioClient(new HttpClient(handler), new MaxioClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        });

        settings ??= new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe",
            PaymentCollectionMethod = "remittance"
        };

        return new MaxioBillingService(client, Options.Create(settings), NullLogger<MaxioBillingService>.Instance);
    }

    // Routes a request to the right canned response by verb + path, mirroring the SDK's real routes.
    private static HttpResponseMessage Route(HttpRequestMessage req,
        string? familiesJson = null,
        string? productsJson = null,
        HttpResponseMessage? readCustomer = null,
        string? createCustomerJson = null,
        HttpResponseMessage? findSubscription = null,
        HttpResponseMessage? createSubscription = null,
        string? listSubscriptionsJson = null)
    {
        var path = req.RequestUri!.AbsolutePath;
        var method = req.Method;

        if (method == HttpMethod.Get && path.Contains("/product_families/") && path.EndsWith("/products.json"))
            return RoutingStubHandler.Json(HttpStatusCode.OK, productsJson ?? "[]");
        if (method == HttpMethod.Get && path.EndsWith("/product_families.json"))
            return RoutingStubHandler.Json(HttpStatusCode.OK, familiesJson ?? "[]");
        if (method == HttpMethod.Get && path.EndsWith("/customers/lookup.json"))
            return readCustomer ?? RoutingStubHandler.Empty(HttpStatusCode.NotFound);
        if (method == HttpMethod.Post && path.EndsWith("/customers.json"))
            return RoutingStubHandler.Json(HttpStatusCode.Created, createCustomerJson ?? """{"customer":{"id":555,"reference":"user"}}""");
        if (method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
            return findSubscription ?? RoutingStubHandler.Empty(HttpStatusCode.NotFound);
        if (method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            return createSubscription ?? RoutingStubHandler.Json(HttpStatusCode.Created, """{"subscription":{"id":999,"state":"active"}}""");
        if (method == HttpMethod.Get && path.Contains("/customers/") && path.EndsWith("/subscriptions.json"))
            return RoutingStubHandler.Json(HttpStatusCode.OK, listSubscriptionsJson ?? "[]");

        return RoutingStubHandler.Empty(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAvailablePlans_ResolvesFamilyIdAndMapsProducts()
    {
        var families = """[{"product_family":{"id":123,"handle":"eshop-subscribe","name":"eShop"}}]""";
        var products = """
        [{"product":{"id":7,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}},
         {"product":{"id":8,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":"2020-01-01T00:00:00Z"}}]
        """;
        var handler = new RoutingStubHandler(req => Route(req, familiesJson: families, productsJson: products));
        var service = BuildService(handler);

        var plans = await service.GetAvailablePlansAsync();

        // Archived product is filtered out; the surviving plan is mapped.
        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(299m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequiresPaymentMethod);

        // The products call must target the numeric family id, not the handle.
        Assert.Equal(1, handler.CountCalls(HttpMethod.Get, "/product_families/123/products.json"));
    }

    [Fact]
    public async Task Subscribe_WhenSubscriptionAlreadyExists_IsIdempotent_AndDoesNotCreate()
    {
        var customer = """{"customer":{"id":555,"reference":"demouser@microsoft.com"}}""";
        var existing = """{"subscription":{"id":94159212,"state":"active","reference":"eshop:demouser@microsoft.com:eshop-pro","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"current_period_ends_at":"2026-10-03T00:00:00Z"}}""";
        var handler = new RoutingStubHandler(req => Route(req,
            readCustomer: RoutingStubHandler.Json(HttpStatusCode.OK, customer),
            findSubscription: RoutingStubHandler.Json(HttpStatusCode.OK, existing)));
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(new SubscriberIdentity(User), "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(94159212, result.SubscriptionId);
        Assert.Equal("active", result.State);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), result.NextBillingDate);
        // Idempotent: no customer and no subscription were created.
        Assert.Equal(0, handler.CountCalls(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, handler.CountCalls(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task Subscribe_WhenNew_CreatesCustomerThenSubscription_WithExpectedBody()
    {
        var created = """{"subscription":{"id":999,"state":"active","reference":"eshop:demouser@microsoft.com:eshop-pro","product":{"handle":"eshop-pro"},"product_price_in_cents":29900,"current_period_ends_at":"2026-10-03T00:00:00Z"}}""";
        var handler = new RoutingStubHandler(req => Route(req,
            readCustomer: RoutingStubHandler.Empty(HttpStatusCode.NotFound),
            findSubscription: RoutingStubHandler.Empty(HttpStatusCode.NotFound),
            createSubscription: RoutingStubHandler.Json(HttpStatusCode.Created, created)));
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(new SubscriberIdentity(User), "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(999, result.SubscriptionId);
        Assert.Equal(1, handler.CountCalls(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountCalls(HttpMethod.Post, "/subscriptions.json"));

        var body = handler.BodyOf(HttpMethod.Post, "/subscriptions.json");
        Assert.NotNull(body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_reference\":\"demouser@microsoft.com\"", body);
        Assert.Contains("\"reference\":\"eshop:demouser@microsoft.com:eshop-pro\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task Subscribe_WhenProviderRejects422_ThrowsInvalidRequestWithMessage()
    {
        var error = """{"errors":["No payment method was on file for the $299.00 balance"]}""";
        var handler = new RoutingStubHandler(req => Route(req,
            readCustomer: RoutingStubHandler.Json(HttpStatusCode.OK, """{"customer":{"id":555,"reference":"demouser@microsoft.com"}}"""),
            findSubscription: RoutingStubHandler.Empty(HttpStatusCode.NotFound),
            createSubscription: RoutingStubHandler.Json(HttpStatusCode.UnprocessableEntity, error)));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync(new SubscriberIdentity(User), "eshop-pro"));

        Assert.Equal(MaxioBillingFailureKind.InvalidRequest, ex.Kind);
        Assert.Contains("No payment method", ex.Message);
    }

    [Fact]
    public async Task Subscribe_WhenCustomerLookupBodyIsUnparseable_DoesNotCreateCustomer()
    {
        // A 200 with a malformed body must NOT be treated as "customer absent" — no spurious create.
        var handler = new RoutingStubHandler(req => Route(req,
            readCustomer: RoutingStubHandler.Json(HttpStatusCode.OK, "not-json")));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync(new SubscriberIdentity(User), "eshop-pro"));

        Assert.Equal(MaxioBillingFailureKind.ProviderUnavailable, ex.Kind);
        Assert.Equal(0, handler.CountCalls(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, handler.CountCalls(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task GetSubscriptions_WhenNoCustomer_ReturnsEmpty_WithoutListing()
    {
        var handler = new RoutingStubHandler(req => Route(req,
            readCustomer: RoutingStubHandler.Empty(HttpStatusCode.NotFound)));
        var service = BuildService(handler);

        var subscriptions = await service.GetSubscriptionsAsync(new SubscriberIdentity(User));

        Assert.Empty(subscriptions);
        Assert.Equal(0, handler.Calls.Count(c => c.Path.Contains("/customers/") && c.Path.EndsWith("/subscriptions.json")));
    }

    [Fact]
    public async Task GetSubscriptions_MapsCustomerSubscriptions()
    {
        var customer = """{"customer":{"id":555,"reference":"demouser@microsoft.com"}}""";
        var subs = """[{"subscription":{"id":94159212,"state":"active","reference":"eshop:demouser@microsoft.com:eshop-pro","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"current_period_ends_at":"2026-10-03T00:00:00Z","currency":"USD"}}]""";
        var handler = new RoutingStubHandler(req => Route(req,
            readCustomer: RoutingStubHandler.Json(HttpStatusCode.OK, customer),
            listSubscriptionsJson: subs));
        var service = BuildService(handler);

        var result = await service.GetSubscriptionsAsync(new SubscriberIdentity(User));

        var sub = Assert.Single(result);
        Assert.Equal(94159212, sub.SubscriptionId);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.Equal("active", sub.State);
        Assert.Equal("USD", sub.Currency);
        Assert.Equal(29900, sub.PriceInCents);
    }
}
