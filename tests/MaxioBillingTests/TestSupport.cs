using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>One request the stub received, captured while its body was still readable.</summary>
public sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Body, string? Authorization);

/// <summary>
/// The HttpClient seam beneath the SDK: every request goes through the real SDK pipeline (serialization, retry,
/// exception translation) and ends here instead of the network.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<CapturedRequest, Task<HttpResponseMessage>> _responder;
    private readonly object _gate = new();

    public StubHandler(Func<CapturedRequest, Task<HttpResponseMessage>> responder) => _responder = responder;

    public StubHandler(Func<CapturedRequest, HttpResponseMessage> responder) : this(r => Task.FromResult(responder(r)))
    {
    }

    public List<CapturedRequest> Requests { get; } = new();

    public int Count(HttpMethod method, string path)
    {
        lock (_gate)
        {
            return Requests.Count(r => r.Method == method && r.Uri.AbsolutePath == path);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var captured = new CapturedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString());
        lock (_gate)
        {
            Requests.Add(captured);
        }
        var response = await _responder(captured);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Empty(HttpStatusCode status) =>
        new(status) { Content = new StringContent(string.Empty) };
}

/// <summary>Builds the real integration stack (SDK client, gateway, services) over a stub and an in-memory store.</summary>
public sealed class Harness
{
    public const string FamilyHandle = "eshop-subscribe";
    public const string BaseUrl = "https://maxio.test";

    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public Harness(StubHandler stub, MaxioOptions? settings = null)
    {
        Stub = stub;
        Settings = settings ?? new MaxioOptions
        {
            ApiKey = "test-placeholder-not-a-key",
            Subdomain = "test-placeholder",
            ProductFamilyHandle = FamilyHandle,
            BaseUrl = BaseUrl,
        };
        var clientOptions = MaxioClientOptionsFactory.Create(Settings, NullLoggerFactory.Instance);
        Client = new MaxioAdvancedBillingClient(new HttpClient(stub), clientOptions);
        Gateway = new MaxioGateway(Client, NullLogger<MaxioGateway>.Instance);
        Cache = new MemoryCache(new MemoryCacheOptions());
    }

    public StubHandler Stub { get; }
    public MaxioOptions Settings { get; }
    public MaxioAdvancedBillingClient Client { get; }
    public MaxioGateway Gateway { get; }
    public IMemoryCache Cache { get; }
    public TimeProvider Time { get; set; } = TimeProvider.System;

    public CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(_databaseName, _root)
            .Options);

    public MaxioPlanCatalog NewCatalog() =>
        new(Gateway, Cache, Options.Create(Settings), NullLogger<MaxioPlanCatalog>.Instance);

    public MaxioCustomerService NewCustomerService(CatalogContext db) =>
        new(db, Gateway, Time, NullLogger<MaxioCustomerService>.Instance);

    public MaxioSubscriptionService NewSubscriptionService(CatalogContext db) =>
        new(db, Gateway, NewCatalog(), NewCustomerService(db), Time, NullLogger<MaxioSubscriptionService>.Instance);
}

/// <summary>Wire-shaped JSON bodies (wire names from the SDK models).</summary>
public static class Wire
{
    public const string UserName = "demouser@microsoft.com";

    public static string Products(params (int Id, string Handle, long PriceInCents, bool Archived)[] products) =>
        "[" + string.Join(",", products.Select(p =>
            $$$"""{"product":{"id":{{{p.Id}}},"handle":"{{{p.Handle}}}","name":"Plan {{{p.Handle}}}","price_in_cents":{{{p.PriceInCents}}},"interval":1,"interval_unit":"month","archived_at":{{{(p.Archived ? "\"2026-01-01T00:00:00Z\"" : "null")}}}}}""")) + "]";

    public static string StandardPlans() => Products((7126957, "eshop-pro", 29900, false), (7126958, "basic-plan", 2900, false));

    public static string Customer(int id, string reference = UserName) =>
        $$$"""{"customer":{"id":{{{id}}},"reference":"{{{reference}}}","email":"{{{UserName}}}","first_name":"demouser","last_name":"eShop"}}""";

    public static string Subscription(int id, string reference, string handle = "eshop-pro", int customerId = 42, string state = "active") =>
        $$$"""{"subscription":{"id":{{{id}}},"state":"{{{state}}}","reference":"{{{reference}}}","product_price_in_cents":29900,"currency":"USD","next_assessment_at":"2026-11-20T10:00:00-04:00","current_period_ends_at":"2026-11-20T10:00:00-04:00","created_at":"2026-10-20T10:00:00-04:00","customer":{"id":{{{customerId}}}},"product":{"id":7126957,"handle":"{{{handle}}}","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}""" + "}";

    public static string Errors(params string[] messages) =>
        "{\"errors\":[" + string.Join(",", messages.Select(m => "\"" + m + "\"")) + "]}";
}
