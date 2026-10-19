using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingGatewayTests
{
    private const string SubscriptionJson = """
        { "subscription": { "id": 42, "state": "active", "reference": "eshop-sub-abc", "currency": "USD",
          "next_assessment_at": "2026-11-19T00:00:00Z", "customer": { "id": 7 },
          "product": { "id": 1, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } } }
        """;
    private const string CustomerJson = """{ "customer": { "id": 7, "reference": "eshop-buyer@example.com", "email": "buyer@example.com" } }""";

    private static (MaxioBillingGateway Gateway, StubHandler Handler) GatewayWith(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var settings = new MaxioSettings
        {
            ApiKey = "unit-test-key",
            BaseUrl = "https://maxio.test",
            ProductFamilyHandle = "eshop-subscribe",
            AttemptTimeoutSeconds = 5,
            CallBudgetSeconds = 10
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler),
            MaxioServiceCollectionExtensions.BuildClientOptions(settings, NullLoggerFactory.Instance));
        return (new MaxioBillingGateway(client, Options.Create(settings), NullLogger<MaxioBillingGateway>.Instance), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static bool Is(HttpRequestMessage request, HttpMethod method, string path) =>
        request.Method == method && request.RequestUri!.AbsolutePath == path;

    [Fact]
    public async Task GetPlans_QueriesConfiguredFamilyByHandle_AndMapsPlans()
    {
        var (gateway, handler) = GatewayWith(_ => Json(HttpStatusCode.OK, """
            [ { "product": { "id": 1, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
              { "product": { "id": 2, "handle": "old-plan", "name": "Old", "price_in_cents": 100, "archived_at": "2025-01-01T00:00:00Z" } } ]
            """));

        var catalog = await gateway.GetPlansAsync(CancellationToken.None);

        Assert.Contains("handle:eshop-subscribe", Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.AbsolutePath));
        Assert.Contains("per_page=200", handler.LastRequest!.RequestUri!.Query);
        var plan = Assert.Single(catalog.Plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(catalog.IsTruncated);
    }

    [Fact]
    public async Task GetPlans_StopsAtPageCap_AndReportsTruncation()
    {
        var page = "[" + string.Join(",", Enumerable.Range(1, MaxioBillingGateway.PlanPageSize)
            .Select(i => $$"""{ "product": { "id": {{i}}, "handle": "p{{i}}", "name": "P", "price_in_cents": 1 } }""")) + "]";
        var (gateway, handler) = GatewayWith(_ => Json(HttpStatusCode.OK, page));

        var catalog = await gateway.GetPlansAsync(CancellationToken.None);

        Assert.True(catalog.IsTruncated);
        Assert.Equal(MaxioBillingGateway.MaxPlanPages, handler.Requests.Count);
    }

    [Fact]
    public async Task FindCustomerByReference_404_ReturnsNull()
    {
        var (gateway, _) = GatewayWith(_ => Json(HttpStatusCode.NotFound, "{}"));

        Assert.Null(await gateway.FindCustomerByReferenceAsync("eshop-nobody", CancellationToken.None));
    }

    [Fact]
    public async Task FindCustomerByReference_UnreadableSuccess_IsAFailureNotAnAbsence()
    {
        var (gateway, _) = GatewayWith(_ => Json(HttpStatusCode.OK, """{ "customer": "not-an-object" }"""));

        await Assert.ThrowsAnyAsync<BillingProviderException>(
            () => gateway.FindCustomerByReferenceAsync("eshop-buyer@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task CreateCustomer_TransportFailure_SettlesByReference()
    {
        var (gateway, handler) = GatewayWith(request =>
            Is(request, HttpMethod.Post, "/customers.json")
                ? throw new HttpRequestException("connection reset")
                : Json(HttpStatusCode.OK, CustomerJson));

        var customer = await gateway.CreateCustomerAsync(
            new("eshop-buyer@example.com", "Buyer", "Example", "buyer@example.com"), CancellationToken.None);

        Assert.Equal(7, customer.Id);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post)); // the write was never resent
        Assert.Contains(handler.Requests, r => Is(r, HttpMethod.Get, "/customers/lookup.json")
                                               && r.RequestUri!.Query.Contains("reference="));
    }

    [Fact]
    public async Task CreateSubscription_TransportFailure_SettlesByReference()
    {
        var (gateway, handler) = GatewayWith(request =>
            Is(request, HttpMethod.Post, "/subscriptions.json")
                ? throw new HttpRequestException("connection reset")
                : Json(HttpStatusCode.OK, SubscriptionJson));

        var subscription = await gateway.CreateSubscriptionAsync(7, "eshop-pro", "eshop-sub-abc", CancellationToken.None);

        Assert.Equal(42, subscription.Id);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
        Assert.Contains(handler.Requests, r => Is(r, HttpMethod.Get, "/subscriptions/lookup.json"));
    }

    [Fact]
    public async Task CreateSubscription_TransportFailureAndLookupFailure_IsOutcomeUnknown()
    {
        var (gateway, _) = GatewayWith(request =>
            Is(request, HttpMethod.Post, "/subscriptions.json")
                ? throw new HttpRequestException("connection reset")
                : Json(HttpStatusCode.Unauthorized, "{}"));

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(
            () => gateway.CreateSubscriptionAsync(7, "eshop-pro", "eshop-sub-abc", CancellationToken.None));
    }

    [Fact]
    public async Task CreateSubscription_TransportFailureAndNothingFound_IsADefiniteFailure()
    {
        var (gateway, _) = GatewayWith(request =>
            Is(request, HttpMethod.Post, "/subscriptions.json")
                ? throw new HttpRequestException("connection reset")
                : Json(HttpStatusCode.NotFound, ""));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => gateway.CreateSubscriptionAsync(7, "eshop-pro", "eshop-sub-abc", CancellationToken.None));
        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task CreateSubscription_SendsHandleCustomerAndReference()
    {
        var (gateway, handler) = GatewayWith(_ => Json(HttpStatusCode.Created, SubscriptionJson));

        var subscription = await gateway.CreateSubscriptionAsync(7, "eshop-pro", "eshop-sub-abc", CancellationToken.None);

        Assert.Equal("active", subscription.State);
        Assert.True(subscription.IsLive);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingAt);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", handler.LastBody);
        Assert.Contains("\"customer_id\":7", handler.LastBody);
        Assert.Contains("\"reference\":\"eshop-sub-abc\"", handler.LastBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", handler.LastBody);
    }

    [Fact]
    public async Task CreateSubscription_422_IsRejectedWithProviderMessages()
    {
        var (gateway, _) = GatewayWith(_ => Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Product must be active."] }"""));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => gateway.CreateSubscriptionAsync(7, "eshop-pro", "eshop-sub-abc", CancellationToken.None));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.Contains("Product must be active.", ex.ProviderMessages);
    }

    [Fact]
    public async Task Unauthorized_IsOurMisconfiguration_NotTheCallers()
    {
        var (gateway, _) = GatewayWith(_ => Json(HttpStatusCode.Unauthorized, "{}"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => gateway.ListCustomerSubscriptionsAsync(7, CancellationToken.None));

        Assert.Equal(BillingFailureKind.Misconfigured, ex.Kind);
    }

    [Fact]
    public async Task FindSubscriptionByReference_404_ReturnsNull()
    {
        var (gateway, _) = GatewayWith(_ => Json(HttpStatusCode.NotFound, ""));

        Assert.Null(await gateway.FindSubscriptionByReferenceAsync("eshop-sub-missing", CancellationToken.None));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> Bodies { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
        public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // Buffer now: the SDK disposes the request content after each attempt.
            Bodies.Add(request.Content is null ? null : request.Content.ReadAsStringAsync(cancellationToken).Result);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
