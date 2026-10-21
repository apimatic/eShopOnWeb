using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>A fake Maxio: answers by method and path, records every request and its body.</summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string Path, Func<HttpRequestMessage, int, HttpResponseMessage> Respond)> _routes = new();
    private readonly Dictionary<string, int> _calls = new();

    public List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = new();

    public List<string> Hosts { get; } = new();

    public FakeMaxio On(HttpMethod method, string path, Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        _routes.Insert(0, (method, path, respond));
        return this;
    }

    public FakeMaxio On(HttpMethod method, string path, HttpStatusCode status, string json) =>
        On(method, path, (_, _) => Json(status, json));

    public int Count(HttpMethod method, string path) =>
        Requests.Count(r => r.Method == method && r.PathAndQuery.StartsWith(path, StringComparison.Ordinal));

    public IEnumerable<string> BodiesOf(HttpMethod method, string path) =>
        Requests.Where(r => r.Method == method && r.PathAndQuery.StartsWith(path, StringComparison.Ordinal))
            .Select(r => r.Body ?? string.Empty);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body now: the SDK disposes the content after each attempt.
        var body = request.Content is null ? null : request.Content.ReadAsStringAsync(cancellationToken).Result;
        var pathAndQuery = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
        Requests.Add((request.Method, pathAndQuery, body));
        Hosts.Add(request.RequestUri.Host);

        var route = _routes.FirstOrDefault(r => r.Method == request.Method &&
                                                Uri.UnescapeDataString(request.RequestUri.AbsolutePath) == r.Path);
        if (route.Respond is null)
            return Task.FromResult(Json(HttpStatusCode.NotImplemented, "{}"));

        var key = request.Method + " " + route.Path;
        _calls[key] = _calls.TryGetValue(key, out var n) ? n + 1 : 1;
        var response = route.Respond(request, _calls[key]);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public sealed class FakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>Builds the real gateway and service over a fake Maxio and an in-memory store.</summary>
public sealed class MaxioTestHarness : IDisposable
{
    public const string Family = "test-family";
    public const string Plan = "pro";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public MaxioTestHarness()
    {
        // A distinct subdomain per harness: the request limit is process-wide per site.
        Options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "unit-" + Guid.NewGuid().ToString("N")[..12],
            ProductFamilyHandle = Family,
            BaseUrl = "https://maxio.test",
        };
        Maxio
            .On(HttpMethod.Get, "/site.json", HttpStatusCode.OK,
                """{"site":{"id":1,"subdomain":"unit","currency":"USD","relationship_invoicing_enabled":true,"test":true}}""")
            .On(HttpMethod.Get, $"/product_families/handle:{Family}/products.json", (request, _) =>
                FakeMaxio.Json(HttpStatusCode.OK, System.Text.RegularExpressions.Regex.IsMatch(request.RequestUri!.Query, "[?&]page=1(&|$)")
                    ? $$$"""[{"product":{"id":11,"handle":"{{{Plan}}}","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]"""
                    : "[]"));

        var options = new MaxioAdvancedBillingClientOptions();
        MaxioServiceCollectionExtensions.ConfigureClient(options, Options, NullLoggerFactory.Instance);
        Client = new MaxioAdvancedBillingClient(new HttpClient(Maxio), options);
        Gateway = new MaxioGateway(Client, Options, _cache, Time, NullLogger<MaxioGateway>.Instance);
    }

    public FakeMaxio Maxio { get; } = new();
    public FakeTime Time { get; } = new();
    public MaxioOptions Options { get; }
    public MaxioAdvancedBillingClient Client { get; }
    public MaxioGateway Gateway { get; }

    public CatalogContext NewDb() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    /// <summary>A service with its own DbContext, as each HTTP request would have.</summary>
    public MaxioSubscriptionService NewService(CatalogContext? db = null) =>
        new(db ?? NewDb(), Gateway, new TestHostEnvironment(), Time, NullLogger<MaxioSubscriptionService>.Instance);

    public static string SubscriptionJson(int id, string state, string reference, string productHandle = Plan) =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"" + state + "\",\"reference\":\"" + reference +
        "\",\"product_price_in_cents\":29900,\"currency\":\"USD\",\"current_period_ends_at\":\"2026-11-21T12:00:00Z\"," +
        "\"product\":{\"id\":11,\"handle\":\"" + productHandle + "\",\"name\":\"Pro Plan\"}}}";

    public void Dispose() => _cache.Dispose();
}
