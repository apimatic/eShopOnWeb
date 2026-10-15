using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// A scripted Maxio behind the SDK's HttpClient seam. Each route answers from a handler the test sets; every request
/// (and its body, buffered while still readable) is recorded so tests can count what actually reached "Maxio".
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    public const string BaseUrl = "https://maxio.test";

    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = new();

    public ConcurrentQueue<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

    public FakeMaxio()
    {
        On("GET", "/product_families/handle:test-family/products.json", _ => Json(HttpStatusCode.OK, Products(
            Product(1, "basic-plan", "Basic Plan", 2900),
            Product(2, "eshop-pro", "Pro Plan", 29900),
            Product(3, "old-plan", "Old Plan", 100, archived: true))));
    }

    public FakeMaxio On(string method, string path, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        OnAsync(method, path, r => Task.FromResult(respond(r)));

    public FakeMaxio OnAsync(string method, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        _routes[$"{method} {path}"] = respond;
        return this;
    }

    public int Count(string method, string path) =>
        Requests.Count(r => r.Method.Method == method && r.Path == path);

    public string? LastBody(string method, string path) =>
        Requests.LastOrDefault(r => r.Method.Method == method && r.Path == path).Body;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request.Method, path, body));

        if (!_routes.TryGetValue($"{request.Method.Method} {path}", out var respond))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request, Content = new StringContent("") };
        }

        var response = await respond(request);
        response.RequestMessage = request;
        return response;
    }

    // ------------------------------------------------------------- wiring

    public static readonly Subscriber DemoUser = new("user-1", "DEMOUSER@MICROSOFT.COM", "demouser@microsoft.com");

    public MaxioSubscriptionBillingService CreateService(CatalogContext db) =>
        new(CreateClient(), db, Options.Create(Settings()), TimeProvider.System, NullLogger<MaxioSubscriptionBillingService>.Instance);

    public MaxioAdvancedBillingClient CreateClient()
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Default() with { Delay = TimeSpan.FromMilliseconds(1), MaxJitter = TimeSpan.Zero },
            Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance }
        };
        options.Server.Production.Us.BaseUrl = BaseUrl;
        return new MaxioAdvancedBillingClient(new HttpClient(this), options);
    }

    public static MaxioSettings Settings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "test-family"
    };

    public static CatalogContext NewContext(string databaseName) =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(databaseName).Options);

    // ------------------------------------------------------------- wire payloads

    public static HttpResponseMessage Json(HttpStatusCode status, object payload) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage Empty(HttpStatusCode status) => new(status) { Content = new StringContent("") };

    public static object[] Products(params object[] products) => products.Select(p => (object)new { product = p }).ToArray();

    public static object Product(int id, string handle, string name, long priceInCents, bool archived = false) => new
    {
        id,
        handle,
        name,
        price_in_cents = priceInCents,
        interval = 1,
        interval_unit = "month",
        require_credit_card = false,
        archived_at = archived ? "2026-01-01T00:00:00Z" : null
    };

    public static object Customer(int id) => new { customer = new { id, reference = "ref" } };

    public static object Subscription(int id, string handle, string state = "active", string? reference = null) => new
    {
        subscription = SubscriptionBody(id, handle, state, reference)
    };

    public static object SubscriptionBody(int id, string handle, string state = "active", string? reference = null) => new
    {
        id,
        state,
        reference,
        currency = "USD",
        product_price_in_cents = handle == "eshop-pro" ? 29900 : 2900,
        current_period_ends_at = "2026-11-15T12:00:00Z",
        next_assessment_at = "2026-11-15T12:00:00Z",
        created_at = DateTimeOffset.UtcNow.ToString("O"),
        product = new { id = 2, handle, name = handle }
    };
}
