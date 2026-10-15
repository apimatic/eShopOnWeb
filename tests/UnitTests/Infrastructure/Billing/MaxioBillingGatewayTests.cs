using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>Drives the real SDK client through a stub HttpMessageHandler — no network.</summary>
public class MaxioBillingGatewayTests
{
    private const string SubscriptionJson = """
        {"subscription":{"id":4242,"state":"active","reference":"eshop-sub-abc","product_price_in_cents":29900,"currency":"USD",
         "next_assessment_at":"2026-11-15T00:00:00Z","current_period_ends_at":"2026-11-15T00:00:00Z",
         "product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","interval":1,"interval_unit":"month"}}}
        """;

    private static readonly NewBillingSubscription NewSubscription = new("eshop-pro", 501, "eshop-sub-abc");

    [Fact]
    public async Task CreateSubscription_SendsHandleCustomerReferenceAndCollectionMethod()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, SubscriptionJson));

        var created = await CreateGateway(handler).CreateSubscriptionAsync(NewSubscription);

        Assert.Equal(4242, created.Id);
        Assert.Equal("active", created.State);
        Assert.Equal("eshop-pro", created.PlanHandle);
        Assert.Equal(29900, created.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), created.NextBillingAt);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith("/subscriptions.json", handler.LastRequest.RequestUri!.AbsolutePath);
        var body = handler.LastBody!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":501", body);
        Assert.Contains("\"reference\":\"eshop-sub-abc\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task CreateSubscription_TransportFailure_ThrowsOutcomeUnknown_AndSendsOnce()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(() => CreateGateway(handler, RetryOptions.Default()).CreateSubscriptionAsync(NewSubscription));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CreateSubscription_ServerError_IsAnUnknownOutcome()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(() => CreateGateway(handler).CreateSubscriptionAsync(NewSubscription));
    }

    [Fact]
    public async Task CreateSubscription_UnreadableSuccessBody_IsAnUnknownOutcome()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """{"subscription":"not-an-object"}"""));

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(() => CreateGateway(handler).CreateSubscriptionAsync(NewSubscription));
    }

    [Fact]
    public async Task CreateSubscription_422_IsRejectedWithProviderErrors()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file"]}"""));

        var ex = await Assert.ThrowsAsync<BillingRequestRejectedException>(() => CreateGateway(handler).CreateSubscriptionAsync(NewSubscription));

        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.Equal(new[] { "No payment method was on file" }, ex.Errors);
    }

    [Fact]
    public async Task CreateSubscription_401_IsOurCredentials_NotTheCallersFault()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"));

        var ex = await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => CreateGateway(handler).CreateSubscriptionAsync(NewSubscription));

        Assert.Equal(401, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task CreateCustomer_422_CarriesCustomerErrors()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference: must be unique"]}"""));

        var ex = await Assert.ThrowsAsync<BillingRequestRejectedException>(() =>
            CreateGateway(handler).CreateCustomerAsync(new NewBillingCustomer("eshop-user-1", "a@b.test", "A", "B")));

        Assert.Contains("Reference: must be unique", ex.Errors);
    }

    [Fact]
    public async Task CreateCustomer_SendsReferenceAndRequiredFields()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """{"customer":{"id":501,"reference":"eshop-user-1"}}"""));

        var created = await CreateGateway(handler).CreateCustomerAsync(new NewBillingCustomer("eshop-user-1", "a@b.test", "A", "B"));

        Assert.Equal(501, created.Id);
        Assert.Contains("\"reference\":\"eshop-user-1\"", handler.LastBody);
        Assert.Contains("\"email\":\"a@b.test\"", handler.LastBody);
    }

    [Fact]
    public async Task FindCustomerByReference_404_IsNull()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));

        Assert.Null(await CreateGateway(handler).FindCustomerByReferenceAsync("eshop-user-1"));
    }

    [Fact]
    public async Task FindSubscriptionByReference_404_IsNull_AndQueriesByReference()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, ""));

        Assert.Null(await CreateGateway(handler).FindSubscriptionByReferenceAsync("eshop-sub-abc"));
        Assert.Contains("reference=eshop-sub-abc", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task FindSubscriptionByReference_ConnectionFailure_IsUnavailable_NotAbsent()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("dns failure"));

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => CreateGateway(handler).FindSubscriptionByReferenceAsync("eshop-sub-abc"));
    }

    [Fact]
    public async Task ListPlans_ReadsFamilyByHandle_AndSkipsArchived()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            [{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
             {"product":{"id":2,"handle":"old-plan","name":"Old","price_in_cents":100,"archived_at":"2025-01-01T00:00:00Z"}}]
            """));

        var catalog = await CreateGateway(handler).ListPlansAsync();

        var plan = Assert.Single(catalog.Plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.False(catalog.Truncated);
        Assert.Contains("handle%3Aeshop-family", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ListPlans_StopsAtPageCap_AndReportsTruncated()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, MaxioBillingGateway.PlanPageSize)
            .Select(i => "{\"product\":{\"id\":" + i + ",\"handle\":\"plan-" + i + "\",\"name\":\"Plan " + i + "\",\"price_in_cents\":100}}")) + "]";
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, fullPage));

        var catalog = await CreateGateway(handler).ListPlansAsync();

        Assert.True(catalog.Truncated);
        Assert.Equal(MaxioBillingGateway.MaxPlanPages, handler.Requests.Count);
    }

    private static MaxioBillingGateway CreateGateway(StubHandler handler, RetryOptions? retry = null)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = retry ?? RetryOptions.Disabled(),
            Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = "eshop-family",
            PlanCacheSeconds = 0
        });
        return new MaxioBillingGateway(client, settings, new MemoryCache(new MemoryCacheOptions()), NullLogger<MaxioBillingGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

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
            Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
