#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Exercises <see cref="MaxioBillingService"/> against the SDK's HttpClient seam (no network).
/// </summary>
public class MaxioBillingServiceTests
{
    private static readonly BillingCustomer Customer = new()
    {
        Reference = "eshop-user-demo",
        Email = "demo@example.com",
        FirstName = "demo",
        LastName = "eShopOnWeb"
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (MaxioBillingService Service, StubHttpMessageHandler Handler) CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "key", Password = "x" }
        });
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "test",
            ProductFamilyHandle = "test-family"
        });
        var logger = Substitute.For<IAppLogger<MaxioBillingService>>();
        return (new MaxioBillingService(client, settings, logger), handler);
    }

    private const string ProProductJson =
        "{\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\",\"description\":\"Pro\"," +
        "\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}";

    private const string ArchivedProductJson =
        "{\"product\":{\"handle\":\"legacy\",\"name\":\"Legacy\",\"price_in_cents\":100," +
        "\"archived_at\":\"2020-01-01T00:00:00Z\"}}";

    private static HttpResponseMessage Route(HttpRequestMessage req,
        string? products = null, string? customerLookup = null, string? customerCreate = null,
        string? subscriptionLookup = null, string? subscriptionCreate = null)
    {
        var path = req.RequestUri!.AbsolutePath;
        if (req.Method == HttpMethod.Get && path.EndsWith("/products.json", StringComparison.OrdinalIgnoreCase))
            return Json(HttpStatusCode.OK, products ?? "[]");
        if (req.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json", StringComparison.OrdinalIgnoreCase))
            return customerLookup is null ? Json(HttpStatusCode.NotFound, "{}") : Json(HttpStatusCode.OK, customerLookup);
        if (req.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.OrdinalIgnoreCase))
            return Json(HttpStatusCode.Created, customerCreate ?? "{\"customer\":{\"id\":123,\"reference\":\"eshop-user-demo\"}}");
        if (req.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json", StringComparison.OrdinalIgnoreCase))
            return subscriptionLookup is null ? Json(HttpStatusCode.NotFound, "{}") : Json(HttpStatusCode.OK, subscriptionLookup);
        if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.OrdinalIgnoreCase))
            return subscriptionCreate is null
                ? Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"invalid\"]}")
                : Json(HttpStatusCode.Created, subscriptionCreate);
        return Json(HttpStatusCode.NotFound, "{}");
    }

    [Fact]
    public async Task GetPlansAsync_MapsProductsAndFiltersArchived()
    {
        var (service, _) = CreateService(req =>
            Route(req, products: $"[{ProProductJson},{ArchivedProductJson}]"));

        var plans = await service.GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_ThrowsBeforeAnyWrite()
    {
        var (service, handler) = CreateService(req => Route(req, products: $"[{ProProductJson}]"));

        await Assert.ThrowsAsync<UnknownPlanException>(() => service.SubscribeAsync(Customer, "does-not-exist"));

        Assert.Equal(0, handler.CountRequests(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, handler.CountRequests(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_ExistingSubscription_IsIdempotent_NoCreate()
    {
        var existing =
            "{\"subscription\":{\"id\":555,\"state\":\"active\",\"reference\":\"eshop-sub-eshop-user-demo-eshop-pro\"," +
            "\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\"},\"product_price_in_cents\":29900," +
            "\"current_period_ends_at\":\"2026-10-22T00:00:00Z\"}}";
        var (service, handler) = CreateService(req => Route(req,
            products: $"[{ProProductJson}]",
            customerLookup: "{\"customer\":{\"id\":123,\"reference\":\"eshop-user-demo\"}}",
            subscriptionLookup: existing));

        var result = await service.SubscribeAsync(Customer, "eshop-pro");

        Assert.Equal(555, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero), result.NextBillingDate);
        Assert.Equal(0, handler.CountRequests(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, handler.CountRequests(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenAbsent()
    {
        var created =
            "{\"subscription\":{\"id\":777,\"state\":\"active\",\"reference\":\"eshop-sub-eshop-user-demo-eshop-pro\"," +
            "\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\"},\"product_price_in_cents\":29900," +
            "\"current_period_ends_at\":\"2026-10-22T00:00:00Z\"}}";
        var (service, handler) = CreateService(req => Route(req,
            products: $"[{ProProductJson}]",
            customerLookup: null,        // 404 → must create the customer
            subscriptionLookup: null,    // 404 → must create the subscription
            subscriptionCreate: created));

        var result = await service.SubscribeAsync(Customer, "eshop-pro");

        Assert.Equal(777, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_CreateRejected_AndNoRace_ThrowsBillingException()
    {
        // Plan valid, customer exists, no existing subscription, create returns 422, reconcile find still 404.
        var (service, _) = CreateService(req => Route(req,
            products: $"[{ProProductJson}]",
            customerLookup: "{\"customer\":{\"id\":123,\"reference\":\"eshop-user-demo\"}}",
            subscriptionLookup: null,
            subscriptionCreate: null));   // POST → 422

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Customer, "eshop-pro"));
        Assert.Equal(BillingErrorKind.InvalidRequest, ex.Kind);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_NoCustomer_ReturnsEmpty()
    {
        var (service, handler) = CreateService(req => Route(req, customerLookup: null)); // 404

        var result = await service.GetSubscriptionsAsync(Customer);

        Assert.Empty(result);
        Assert.Equal(0, handler.CountRequests(HttpMethod.Get, "/subscriptions.json"));
    }
}
