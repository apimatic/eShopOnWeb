using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingServiceTests
{
    private const string ProductsJson =
        "[{\"product\":{\"id\":1,\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false}}," +
        "{\"product\":{\"id\":2,\"handle\":\"basic-plan\",\"name\":\"Basic Plan\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false}}]";

    private static string SubscriptionJson(int id, string state, string handle, long cents) =>
        $"{{\"subscription\":{{\"id\":{id},\"state\":\"{state}\",\"reference\":\"u1:{handle}\"," +
        $"\"product_price_in_cents\":{cents},\"current_period_ends_at\":\"2026-10-10T00:00:00+00:00\"," +
        $"\"customer\":{{\"id\":555}}," +
        $"\"product\":{{\"handle\":\"{handle}\",\"name\":\"Pro Plan\",\"price_in_cents\":{cents},\"interval\":1,\"interval_unit\":\"month\"}}}}}}";

    private static readonly BillingAppUser User = new("u1", "e@example.com", "E", "X");

    private static MaxioBillingService CreateService(StubHttpMessageHandler handler)
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "sub", ProductFamilyHandle = "fam" };
        // Disable retries so a stubbed error status returns immediately rather than after backoff.
        var options = new MaxioAdvancedBillingClientOptions { Retry = RetryOptions.Disabled() };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        return new MaxioBillingService(client, Options.Create(settings), NullLogger<MaxioBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static bool Get(HttpRequestMessage r, string suffix) =>
        r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith(suffix);

    private static bool Post(HttpRequestMessage r, string suffix) =>
        r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith(suffix);

    [Fact]
    public async Task GetPlans_MapsProductsToDtos()
    {
        var handler = new StubHttpMessageHandler(r =>
            Get(r, "/products.json") ? Json(HttpStatusCode.OK, ProductsJson) : Json(HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task Subscribe_WhenNoCustomerOrSubscription_CreatesBoth_AndReturnsActive()
    {
        var handler = new StubHttpMessageHandler(r =>
        {
            if (Get(r, "/products.json")) return Json(HttpStatusCode.OK, ProductsJson);
            if (Get(r, "/customers/lookup.json")) return Json(HttpStatusCode.NotFound, "{}");
            if (Post(r, "/customers.json")) return Json(HttpStatusCode.Created, "{\"customer\":{\"id\":555}}");
            if (Get(r, "/subscriptions/lookup.json")) return Json(HttpStatusCode.NotFound, "{}");
            if (Post(r, "/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson(999, "active", "eshop-pro", 29900));
            return Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(User, new SubscribeRequest { PlanHandle = "eshop-pro" });

        Assert.Equal(999, result.SubscriptionId);
        Assert.Equal("active", result.State);
        Assert.Equal(555, result.CustomerId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(299m, result.Price);
        Assert.NotNull(result.NextBillingDate);
        // Both writes were issued exactly once.
        Assert.Equal(1, handler.Requests.Count(r => Post(r, "/customers.json")));
        Assert.Equal(1, handler.Requests.Count(r => Post(r, "/subscriptions.json")));
        // The subscription create body carries the remittance collection method (no card capture).
        var subBody = handler.Bodies[handler.Requests.FindIndex(r => Post(r, "/subscriptions.json"))];
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subBody);
        Assert.Contains("\"customer_id\":555", subBody);
        Assert.Contains("\"reference\":\"u1:eshop-pro\"", subBody);
    }

    [Fact]
    public async Task Subscribe_WhenSubscriptionAlreadyExists_IsIdempotent_AndDoesNotCreate()
    {
        var handler = new StubHttpMessageHandler(r =>
        {
            if (Get(r, "/products.json")) return Json(HttpStatusCode.OK, ProductsJson);
            if (Get(r, "/customers/lookup.json")) return Json(HttpStatusCode.OK, "{\"customer\":{\"id\":555}}");
            if (Get(r, "/subscriptions/lookup.json")) return Json(HttpStatusCode.OK, SubscriptionJson(999, "active", "eshop-pro", 29900));
            return Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(User, new SubscribeRequest { PlanHandle = "eshop-pro" });

        Assert.Equal(999, result.SubscriptionId);
        // No writes at all — the existing customer and subscription were reused.
        Assert.DoesNotContain(handler.Requests, r => Post(r, "/customers.json"));
        Assert.DoesNotContain(handler.Requests, r => Post(r, "/subscriptions.json"));
    }

    [Fact]
    public async Task GetPlans_WhenProviderReturns500_ThrowsBillingExceptionWithStatus()
    {
        var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.InternalServerError, "boom"));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.GetPlansAsync());

        Assert.Equal(500, ex.StatusCode);
    }

    [Fact]
    public async Task Subscribe_WhenCreateReturns422_SurfacesProviderMessage()
    {
        var handler = new StubHttpMessageHandler(r =>
        {
            if (Get(r, "/products.json")) return Json(HttpStatusCode.OK, ProductsJson);
            if (Get(r, "/customers/lookup.json")) return Json(HttpStatusCode.OK, "{\"customer\":{\"id\":555}}");
            if (Get(r, "/subscriptions/lookup.json")) return Json(HttpStatusCode.NotFound, "{}");
            if (Post(r, "/subscriptions.json"))
                return Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"No payment method was on file\"]}");
            return Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(User, new SubscribeRequest { PlanHandle = "eshop-pro" }));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("No payment method was on file", ex.Message);
    }

    [Fact]
    public async Task GetMySubscriptions_WhenNoCustomer_ReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler(r =>
            Get(r, "/customers/lookup.json") ? Json(HttpStatusCode.NotFound, "{}") : Json(HttpStatusCode.InternalServerError, "{}"));
        var service = CreateService(handler);

        var result = await service.GetMySubscriptionsAsync(User);

        Assert.Empty(result);
    }
}
