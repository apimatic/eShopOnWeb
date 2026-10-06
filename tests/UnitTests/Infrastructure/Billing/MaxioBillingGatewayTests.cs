using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// Drives the real Maxio SDK through a stubbed HttpMessageHandler: no network, but the request the SDK
/// builds and the response it parses are both real.
/// </summary>
public class MaxioBillingGatewayTests
{
    private const string BaseUrl = "https://maxio.test";

    private static MaxioSettings Settings(Action<MaxioSettings>? configure = null)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "unit-test-key",
            Subdomain = "unit-test-site",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = BaseUrl,
            MaxReadRetries = 0,
            AttemptTimeoutSeconds = 1,
            SettleBudgetSeconds = 1,
            PlanCacheSeconds = 0
        };
        configure?.Invoke(settings);
        return settings;
    }

    private static MaxioBillingGateway Gateway(StubHandler handler, MaxioSettings? settings = null)
    {
        settings ??= Settings();
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler),
            MaxioBillingServiceCollectionExtensions.CreateClientOptions(settings, NullLoggerFactory.Instance));
        return new MaxioBillingGateway(client, Options.Create(settings), new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioBillingGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string SubscriptionJson = """
        {"subscription":{"id":500,"state":"active","reference":"eshop-sub-u","product_price_in_cents":29900,
         "currency":"USD","next_assessment_at":"2026-11-06T08:19:31+05:00","current_period_ends_at":"2026-11-06T08:19:31+05:00",
         "customer":{"id":10},"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","interval":1,"interval_unit":"month"}}}
        """;

    [Fact]
    public async Task GetPlansReadsTheConfiguredFamilyByHandleAndSkipsArchivedProducts()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            [{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
             {"product":{"id":2,"handle":"old-plan","name":"Old","price_in_cents":100,"archived_at":"2024-01-01T00:00:00Z"}}]
            """));

        var catalog = await Gateway(handler).GetPlansAsync(CancellationToken.None);

        var plan = Assert.Single(catalog.Plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(catalog.IsTruncated);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("maxio.test", request.RequestUri!.Host);
        Assert.Contains("/product_families/handle%3Aeshop-subscribe/products.json", request.RequestUri.AbsoluteUri);
        Assert.Contains("per_page=200", request.RequestUri.Query);
    }

    [Fact]
    public async Task GetPlansReportsTruncationWhenThePageCapIsReached()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, MaxioBillingGateway.PlanPageSize)
            .Select(i => $"{{\"product\":{{\"id\":{i},\"handle\":\"p{i}\",\"name\":\"P{i}\",\"price_in_cents\":100}}}}")) + "]";
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, fullPage));

        var catalog = await Gateway(handler, Settings(s => s.MaxPlanPages = 2)).GetPlansAsync(CancellationToken.None);

        Assert.True(catalog.IsTruncated);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2 * MaxioBillingGateway.PlanPageSize, catalog.Plans.Count);
    }

    [Fact]
    public async Task UnknownProductFamilyIsAConfigurationError()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "\"Not found\""));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => Gateway(handler).GetPlansAsync(CancellationToken.None));

        Assert.Equal(BillingFailureKind.Misconfigured, ex.Kind);
    }

    [Fact]
    public async Task RejectedCredentialsAreAConfigurationError()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("HTTP Basic: Access denied.") });

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => Gateway(handler).FindCustomerByReferenceAsync("eshop-u", CancellationToken.None));

        Assert.Equal(BillingFailureKind.Misconfigured, ex.Kind);
        Assert.Equal(401, ex.ProviderStatusCode);
        Assert.StartsWith("Basic ", handler.Requests.Single().Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task MissingCustomerIsNull()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await Gateway(handler).FindCustomerByReferenceAsync("eshop-u", CancellationToken.None));
        Assert.Contains("/customers/lookup.json", handler.Requests.Single().RequestUri!.AbsolutePath);
        Assert.Contains("reference=eshop-u", handler.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task UnreadableLookupIsNotMistakenForAbsence()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"customer":"not-an-object"}"""));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => Gateway(handler).FindCustomerByReferenceAsync("eshop-u", CancellationToken.None));

        Assert.Equal(BillingFailureKind.ProviderError, ex.Kind);
    }

    [Fact]
    public async Task CreateCustomerSendsReferenceAndContactDetails()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """{"customer":{"id":10,"reference":"eshop-u","email":"u@x.com"}}"""));

        var customer = await Gateway(handler).CreateCustomerAsync(
            new("eshop-u", "u@x.com", "u", "eShop Customer"), CancellationToken.None);

        Assert.Equal(10, customer.Id);
        Assert.Equal(HttpMethod.Post, handler.Requests.Single().Method);
        Assert.Contains("\"reference\":\"eshop-u\"", handler.LastBody);
        Assert.Contains("\"email\":\"u@x.com\"", handler.LastBody);
        Assert.Contains("\"first_name\":\"u\"", handler.LastBody);
    }

    [Fact]
    public async Task CreateCustomerAdoptsTheCustomerWhenTheReferenceIsAlreadyTaken()
    {
        var handler = new StubHandler(request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference must be unique"]}""")
            : Json(HttpStatusCode.OK, """{"customer":{"id":11,"reference":"eshop-u"}}"""));

        var customer = await Gateway(handler).CreateCustomerAsync(new("eshop-u", "u@x.com", "u", "c"), CancellationToken.None);

        Assert.Equal(11, customer.Id);
    }

    [Fact]
    public async Task DroppedConnectionOnCreateCustomerIsSettledByReference()
    {
        // The POST reaches Maxio and creates the customer, but the connection drops before the answer arrives.
        var handler = new StubHandler(request => request.Method == HttpMethod.Post
            ? throw new HttpRequestException("connection reset")
            : Json(HttpStatusCode.OK, """{"customer":{"id":12,"reference":"eshop-u"}}"""));

        var customer = await Gateway(handler).CreateCustomerAsync(new("eshop-u", "u@x.com", "u", "c"), CancellationToken.None);

        Assert.Equal(12, customer.Id);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
        Assert.Contains("/customers/lookup.json", handler.Requests.Last().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DroppedConnectionOnCreateCustomerThatCannotBeSettledIsAnUnknownOutcome()
    {
        var handler = new StubHandler(request => request.Method == HttpMethod.Post
            ? throw new HttpRequestException("connection reset")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<BillingOutcomeUnknownException>(
            () => Gateway(handler).CreateCustomerAsync(new("eshop-u", "u@x.com", "u", "c"), CancellationToken.None));

        Assert.Equal(BillingFailureKind.Unreachable, ex.Kind);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task CreateSubscriptionSendsCustomerPlanReferenceAndCollectionMethod()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, SubscriptionJson));

        var subscription = await Gateway(handler).CreateSubscriptionAsync(10, "eshop-pro", "eshop-sub-u", CancellationToken.None);

        Assert.Equal(500, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.NotNull(subscription.NextBillingAt);
        Assert.EndsWith("/subscriptions.json", handler.Requests.Single().RequestUri!.AbsolutePath);
        Assert.Contains("\"customer_id\":10", handler.LastBody);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", handler.LastBody);
        Assert.Contains("\"reference\":\"eshop-sub-u\"", handler.LastBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", handler.LastBody);
    }

    [Fact]
    public async Task CreateSubscriptionValidationErrorsAreSurfaced()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file"]}"""));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => Gateway(handler).CreateSubscriptionAsync(10, "eshop-pro", "eshop-sub-u", CancellationToken.None));

        Assert.IsNotType<BillingOutcomeUnknownException>(ex);
        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Equal(new[] { "No payment method was on file" }, ex.ProviderErrors);
    }

    [Fact]
    public async Task DroppedConnectionOnCreateIsAnUnknownOutcomeAndIsNeverResent()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<BillingOutcomeUnknownException>(
            () => Gateway(handler, Settings(s => s.MaxReadRetries = 3)).CreateSubscriptionAsync(10, "eshop-pro", "eshop-sub-u", CancellationToken.None));

        Assert.Equal(BillingFailureKind.Unreachable, ex.Kind);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ServerErrorOnCreateIsAnUnknownOutcome()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("oops") });

        await Assert.ThrowsAsync<BillingOutcomeUnknownException>(
            () => Gateway(handler).CreateSubscriptionAsync(10, "eshop-pro", "eshop-sub-u", CancellationToken.None));
    }

    [Fact]
    public async Task HungCreateTimesOutAsAnUnknownOutcome()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        var ex = await Assert.ThrowsAsync<BillingOutcomeUnknownException>(
            () => Gateway(handler).CreateSubscriptionAsync(10, "eshop-pro", "eshop-sub-u", CancellationToken.None));

        Assert.Equal(BillingFailureKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task HungReadTimesOut()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => Gateway(handler).FindSubscriptionByReferenceAsync("eshop-sub-u", CancellationToken.None));

        Assert.Equal(BillingFailureKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task FindSubscriptionMapsNotFoundToNullAndFoundToSubscription()
    {
        var notFound = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await Gateway(notFound).FindSubscriptionByReferenceAsync("eshop-sub-u", CancellationToken.None));
        Assert.Contains("/subscriptions/lookup.json", notFound.Requests.Single().RequestUri!.AbsolutePath);

        var found = new StubHandler(_ => Json(HttpStatusCode.OK, SubscriptionJson));
        var subscription = await Gateway(found).FindSubscriptionByReferenceAsync("eshop-sub-u", CancellationToken.None);
        Assert.Equal(10, subscription!.CustomerId);
    }

    [Fact]
    public async Task ListCustomerSubscriptionsReadsTheCustomersList()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[" + SubscriptionJson + "]"));

        var subscriptions = await Gateway(handler).ListCustomerSubscriptionsAsync(10, CancellationToken.None);

        Assert.Equal(500, Assert.Single(subscriptions).Id);
        Assert.EndsWith("/customers/10/subscriptions.json", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SubdomainDeterminesTheHostWhenNoBaseUrlIsSet()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Gateway(handler, Settings(s => s.BaseUrl = null)).FindCustomerByReferenceAsync("eshop-u", CancellationToken.None);

        Assert.Equal("unit-test-site.chargify.com", handler.Requests.Single().RequestUri!.Host);
    }
}

/// <summary>Records every request (and its body, read while still readable) and answers from a delegate.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((request, _) => Task.FromResult(responder(request)))
    {
    }

    public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();
    public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Bodies.Add(body);
        }
        var response = await _responder(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }
}
