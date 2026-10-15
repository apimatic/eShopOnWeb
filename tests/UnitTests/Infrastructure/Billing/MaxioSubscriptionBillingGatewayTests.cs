using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingGatewayTests
{
    private const string BaseUrl = "https://maxio.example.test";
    private const string SubscriptionReference = "eshop-abc-test-family";
    private const string CustomerReference = "eshop-abc";

    private static readonly string SubscriptionJson = """
        {"subscription":{"id":9001,"state":"active","reference":"eshop-abc-test-family","product_price_in_cents":29900,
         "current_period_ends_at":"2026-11-15T12:00:00Z","next_assessment_at":"2026-11-15T12:00:00Z",
         "product":{"id":7,"handle":"pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}
        """;

    private static (MaxioSubscriptionBillingGateway Gateway, StubHandler Handler) CreateGateway(
        Func<HttpRequestMessage, HttpResponseMessage> responder, Action<MaxioSettings>? configure = null)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family",
            BaseUrl = BaseUrl,
            RequestTimeoutSeconds = 5,
            AttemptTimeoutSeconds = 5
        };
        configure?.Invoke(settings);
        var handler = new StubHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler),
            MaxioServiceCollectionExtensions.BuildClientOptions(settings, NullLoggerFactory.Instance, TimeProvider.System));
        var gateway = new MaxioSubscriptionBillingGateway(client, Options.Create(settings),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<MaxioSubscriptionBillingGateway>.Instance);
        return (gateway, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static bool Is(HttpRequestMessage request, HttpMethod method, string pathFragment) =>
        request.Method == method && Uri.UnescapeDataString(request.RequestUri!.AbsolutePath).Contains(pathFragment);

    [Fact]
    public async Task ListPlans_ReadsConfiguredFamilyByHandle_AtBaseUrlOverride_AndSkipsArchived()
    {
        var (gateway, handler) = CreateGateway(_ => Json(HttpStatusCode.OK, """
            [{"product":{"id":1,"handle":"pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
             {"product":{"id":2,"handle":"old","name":"Old","archived_at":"2025-01-01T00:00:00Z"}}]
            """));

        var catalog = await gateway.ListPlansAsync(default);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("maxio.example.test", request.RequestUri!.Host);
        Assert.Contains("/product_families/handle:test-family/products.json", Uri.UnescapeDataString(request.RequestUri.AbsolutePath));
        Assert.Contains("per_page=200", request.RequestUri.Query);
        Assert.True(catalog.IsComplete);
        var plan = Assert.Single(catalog.Plans);
        Assert.Equal(new SubscriptionPlan("pro", "Pro Plan", null, 29900, 1, "month"), plan);
    }

    [Fact]
    public async Task ListPlans_EveryPageFull_StopsAtCap_AndReportsIncomplete()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(0, MaxioSubscriptionBillingGateway.PlanPageSize)
            .Select(i => "{\"product\":{\"id\":" + i + ",\"handle\":\"p" + i + "\",\"name\":\"P" + i + "\"}}")) + "]";
        var (gateway, handler) = CreateGateway(_ => Json(HttpStatusCode.OK, fullPage));

        var catalog = await gateway.ListPlansAsync(default);

        Assert.False(catalog.IsComplete);
        Assert.Equal(MaxioSubscriptionBillingGateway.MaxPlanPages, handler.Requests.Count);
    }

    [Fact]
    public async Task ListPlans_UnknownFamily_IsUnavailable()
    {
        var (gateway, _) = CreateGateway(_ => Json(HttpStatusCode.NotFound, "\"Not found\""));

        await Assert.ThrowsAsync<BillingUnavailableException>(() => gateway.ListPlansAsync(default));
    }

    [Fact]
    public async Task FindCustomer_NotFound_ReturnsNull_ButUnauthorizedIsUnavailable()
    {
        var (missing, _) = CreateGateway(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await missing.FindCustomerIdAsync(CustomerReference, default));

        var (unauthorized, _) = CreateGateway(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<BillingUnavailableException>(() => unauthorized.FindCustomerIdAsync(CustomerReference, default));
    }

    [Fact]
    public async Task EnsureCustomer_CreatesWithReference_WhenLookupMisses()
    {
        var (gateway, handler) = CreateGateway(request =>
            Is(request, HttpMethod.Get, "/customers/lookup.json") ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json(HttpStatusCode.Created, """{"customer":{"id":501,"reference":"eshop-abc"}}"""));

        var id = await gateway.EnsureCustomerAsync(new BillingCustomer(CustomerReference, "a@b.test", "a", "eShopOnWeb"), default);

        Assert.Equal(501, id);
        Assert.Contains("\"reference\":\"eshop-abc\"", handler.Bodies.Last());
        Assert.Contains("\"email\":\"a@b.test\"", handler.Bodies.Last());
    }

    [Fact]
    public async Task CreateCustomer_ConnectionDropped_SettlesByReadingCustomerByReference()
    {
        var lookups = 0;
        var (gateway, handler) = CreateGateway(request =>
        {
            if (Is(request, HttpMethod.Post, "/customers.json"))
            {
                throw new HttpRequestException("connection reset");
            }
            // First lookup (before the create) misses; the settling lookup finds the customer that landed.
            return ++lookups == 1
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(HttpStatusCode.OK, """{"customer":{"id":501,"reference":"eshop-abc"}}""");
        });

        var id = await gateway.EnsureCustomerAsync(new BillingCustomer(CustomerReference, "a@b.test", "a", "eShopOnWeb"), default);

        Assert.Equal(501, id);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task CreateCustomer_ConnectionDropped_AndNotFound_IsOutcomeUnknown()
    {
        var (gateway, _) = CreateGateway(request => Is(request, HttpMethod.Post, "/customers.json")
            ? throw new HttpRequestException("connection reset")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(() =>
            gateway.EnsureCustomerAsync(new BillingCustomer(CustomerReference, "a@b.test", "a", "eShopOnWeb"), default));
    }

    [Fact]
    public async Task CreateSubscription_SendsHandleCustomerAndReference_AndMapsResult()
    {
        var (gateway, handler) = CreateGateway(_ => Json(HttpStatusCode.Created, SubscriptionJson));

        var subscription = await gateway.CreateSubscriptionAsync(501, "pro", SubscriptionReference, default);

        var body = handler.Bodies.Single();
        Assert.Contains("\"product_handle\":\"pro\"", body);
        Assert.Contains("\"customer_id\":501", body);
        Assert.Contains($"\"reference\":\"{SubscriptionReference}\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.Equal(9001, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("pro", subscription.PlanHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero), subscription.NextBillingAt);
    }

    [Fact]
    public async Task CreateSubscription_ConnectionDropped_SettlesByFindSubscription()
    {
        var (gateway, handler) = CreateGateway(request => Is(request, HttpMethod.Post, "/subscriptions.json")
            ? throw new HttpRequestException("connection reset")
            : Json(HttpStatusCode.OK, SubscriptionJson));

        var subscription = await gateway.CreateSubscriptionAsync(501, "pro", SubscriptionReference, default);

        Assert.Equal(9001, subscription.Id);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));   // never resent
        var lookup = handler.Requests.Last();
        Assert.Contains("/subscriptions/lookup.json", lookup.RequestUri!.AbsolutePath);
        Assert.Contains(SubscriptionReference, Uri.UnescapeDataString(lookup.RequestUri.Query));
    }

    [Fact]
    public async Task CreateSubscription_ConnectionDropped_AndNotFound_IsOutcomeUnknown()
    {
        var (gateway, _) = CreateGateway(request => Is(request, HttpMethod.Post, "/subscriptions.json")
            ? throw new HttpRequestException("connection reset")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(() =>
            gateway.CreateSubscriptionAsync(501, "pro", SubscriptionReference, default));
    }

    [Fact]
    public async Task CreateSubscription_Unprocessable_IsRejectedWithProviderMessages()
    {
        var (gateway, _) = CreateGateway(request => Is(request, HttpMethod.Post, "/subscriptions.json")
            ? Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Product must be active"]}""")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<BillingRejectedException>(() =>
            gateway.CreateSubscriptionAsync(501, "pro", SubscriptionReference, default));
        Assert.Contains("Product must be active", ex.Message);
    }

    [Fact]
    public async Task FindSubscription_NotFound_IsNull_ButUnreadableSuccessIsNotTreatedAsAbsent()
    {
        var (missing, _) = CreateGateway(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await missing.FindSubscriptionByReferenceAsync(SubscriptionReference, default));

        var (garbled, _) = CreateGateway(_ => Json(HttpStatusCode.OK, """{"subscription":"not-an-object"}"""));
        await Assert.ThrowsAsync<BillingUnavailableException>(() => garbled.FindSubscriptionByReferenceAsync(SubscriptionReference, default));
    }

    [Fact]
    public async Task ListSubscriptionsForCustomer_MapsEverySubscription()
    {
        var (gateway, handler) = CreateGateway(_ => Json(HttpStatusCode.OK, $"[{SubscriptionJson}]"));

        var subscriptions = await gateway.ListSubscriptionsForCustomerAsync(501, default);

        Assert.Contains("/customers/501/subscriptions.json", handler.Requests.Single().RequestUri!.AbsolutePath);
        Assert.Equal(9001, Assert.Single(subscriptions).Id);
    }

    [Fact]
    public void Settings_ReportMissingKeysByName()
    {
        var problems = new MaxioSettings().Validate();

        Assert.Contains(problems, p => p.Contains("Maxio:ApiKey"));
        Assert.Contains(problems, p => p.Contains("Maxio:Subdomain"));
        Assert.Contains(problems, p => p.Contains("Maxio:ProductFamilyHandle"));
        Assert.Empty(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = BaseUrl }.Validate());
    }

    [Fact]
    public void ClientOptions_DeriveSiteFromSubdomain_WhenNoBaseUrl()
    {
        var options = MaxioServiceCollectionExtensions.BuildClientOptions(
            new MaxioSettings { ApiKey = "k", Subdomain = "my-site", ProductFamilyHandle = "f" },
            NullLoggerFactory.Instance, TimeProvider.System);

        Assert.Equal("my-site", options.Server.Production.Us.Site);
        Assert.False(options.Logging.LogRequestBody);
        Assert.NotNull(options.Logging.LoggerFactory);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string?> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // Read the body now: the SDK disposes request content after each attempt.
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            var response = _responder(request);
            response.RequestMessage = request;
            return response;
        }
    }
}
