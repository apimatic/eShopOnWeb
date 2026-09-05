using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string ProProductJson = """
        {
          "product": {
            "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900,
            "interval": 1, "interval_unit": "month", "require_credit_card": false, "request_credit_card": true
          }
        }
        """;

    private sealed class SequentialStubHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Json)> _responses = new();
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> RequestBodies { get; } = new();

        public void Enqueue(HttpStatusCode status, string json) => _responses.Enqueue((status, json));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            // The SDK disposes the request (and its content) once SendAsync returns, so the body
            // must be captured here rather than re-read from the request afterward.
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
            }

            var (status, json) = _responses.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private static (MaxioSubscriptionService Service, SequentialStubHandler Handler) CreateService()
    {
        var handler = new SequentialStubHandler();
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return (new MaxioSubscriptionService(client, options), handler);
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsMappedPlan()
    {
        var (service, handler) = CreateService();
        handler.Enqueue(HttpStatusCode.OK, $"[{ProProductJson}]");

        var plans = await service.ListPlansAsync(default);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequiresPaymentMethod);
    }

    [Fact]
    public async Task ListPlansAsync_ExcludesArchivedProducts()
    {
        var (service, handler) = CreateService();
        const string archived = """
            {
              "product": { "id": 3, "name": "Old Plan", "handle": "old-plan", "price_in_cents": 100,
                "archived_at": "2020-01-01T00:00:00Z" }
            }
            """;
        handler.Enqueue(HttpStatusCode.OK, $"[{ProProductJson},{archived}]");

        var plans = await service.ListPlansAsync(default);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlanHandle_ThrowsValidationExceptionWithoutCallingCustomerApis()
    {
        var (service, handler) = CreateService();
        handler.Enqueue(HttpStatusCode.OK, $"[{ProProductJson}]");

        await Assert.ThrowsAsync<MaxioValidationException>(() =>
            service.SubscribeAsync("user-1", "user1@test.com", "User", "One", "does-not-exist", default));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubscribeAsync_PlanRequiresCreditCard_ThrowsValidationExceptionBeforeAnyCustomerCall()
    {
        var (service, handler) = CreateService();
        const string cardRequiredProduct = """
            {
              "product": { "id": 9, "name": "Card Plan", "handle": "card-plan", "price_in_cents": 999,
                "require_credit_card": true }
            }
            """;
        handler.Enqueue(HttpStatusCode.OK, $"[{cardRequiredProduct}]");

        await Assert.ThrowsAsync<MaxioValidationException>(() =>
            service.SubscribeAsync("user-1", "user1@test.com", "User", "One", "card-plan", default));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubscribeAsync_CustomerAlreadySubscribedToPlan_ThrowsDuplicateExceptionWithoutCreatingSubscription()
    {
        var (service, handler) = CreateService();
        handler.Enqueue(HttpStatusCode.OK, $"[{ProProductJson}]");
        handler.Enqueue(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "user-1" } }""");
        handler.Enqueue(HttpStatusCode.OK, """
            [ { "subscription": { "id": 777, "state": "active",
                "product": { "handle": "eshop-pro", "name": "Pro Plan" } } } ]
            """);

        await Assert.ThrowsAsync<DuplicateException>(() =>
            service.SubscribeAsync("user-1", "user1@test.com", "User", "One", "eshop-pro", default));

        // list-plans + read-customer + list-subscriptions only — no fourth (create-subscription) call
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task SubscribeAsync_NewCustomerNoExistingSubscription_CreatesSubscriptionUsingRemittanceCollection()
    {
        var (service, handler) = CreateService();
        handler.Enqueue(HttpStatusCode.OK, $"[{ProProductJson}]"); // list plans
        handler.Enqueue(HttpStatusCode.NotFound, """{ "errors": ["not found"] }"""); // customer lookup miss
        handler.Enqueue(HttpStatusCode.OK, """{ "customer": { "id": 42, "reference": "user-1" } }"""); // create customer
        handler.Enqueue(HttpStatusCode.OK, "[]"); // no existing subscriptions
        handler.Enqueue(HttpStatusCode.OK, """
            { "subscription": { "id": 900, "state": "active", "next_assessment_at": "2026-11-01T00:00:00Z",
              "product_price_in_cents": 29900, "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }
            """); // create subscription

        var result = await service.SubscribeAsync("user-1", "user1@test.com", "User", "One", "eshop-pro", default);

        Assert.Equal("active", result.State);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal("eshop-pro", result.PlanHandle);

        Assert.Equal(HttpMethod.Post, handler.Requests[^1].Method);
        var body = handler.RequestBodies[^1];
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.Contains("\"customer_reference\":\"user-1\"", body);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_UnknownCustomer_ReturnsEmptyList()
    {
        var (service, handler) = CreateService();
        handler.Enqueue(HttpStatusCode.NotFound, """{ "errors": ["not found"] }""");

        var result = await service.ListSubscriptionsForCustomerAsync("nobody", default);

        Assert.Empty(result);
    }
}
