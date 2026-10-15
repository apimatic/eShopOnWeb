using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>In-memory stand-in for the Maxio API, plugged in under the SDK's HttpClient.</summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    private int _nextId = 1000;
    private readonly ConcurrentDictionary<string, int> _customers = new();
    private readonly ConcurrentDictionary<string, int> _subscriptions = new();

    public ConcurrentQueue<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = new();

    /// <summary>Set to make POST /subscriptions.json misbehave (return a response or throw). Null = normal behaviour.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? OnCreateSubscription { get; set; }

    public int Count(HttpMethod method, string pathFragment) =>
        Requests.Count(r => r.Method == method && r.PathAndQuery.Contains(pathFragment, StringComparison.Ordinal));

    /// <summary>Records that Maxio holds a subscription with this reference (as if a write had committed).</summary>
    public int CommitSubscription(string reference) => _subscriptions.GetOrAdd(reference, _ => Interlocked.Increment(ref _nextId));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
        var path = request.RequestUri!.AbsolutePath;
        var query = Uri.UnescapeDataString(request.RequestUri.Query);
        Requests.Enqueue((request.Method, Uri.UnescapeDataString(request.RequestUri.PathAndQuery), body));

        if (request.Method == HttpMethod.Get && path.EndsWith("/products.json"))
        {
            return Json("""
                [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
                 {"product":{"id":2,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}},
                 {"product":{"id":3,"name":"Old Plan","handle":"old-plan","price_in_cents":100,"archived_at":"2020-01-01T00:00:00Z"}}]
                """);
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json"))
        {
            var reference = ReferenceFromQuery(query);
            return _customers.TryGetValue(reference, out var id) ? Json(CustomerJson(id, reference)) : NotFound();
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
        {
            var reference = JsonDocument.Parse(body!).RootElement.GetProperty("customer").GetProperty("reference").GetString()!;
            var id = _customers.GetOrAdd(reference, _ => Interlocked.Increment(ref _nextId));
            return Json(CustomerJson(id, reference), HttpStatusCode.Created);
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
        {
            var reference = ReferenceFromQuery(query);
            return _subscriptions.TryGetValue(reference, out var id) ? Json(SubscriptionJson(id, reference)) : NotFound();
        }

        if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/") && path.EndsWith(".json"))
        {
            var id = int.Parse(path.Split('/').Last().Replace(".json", ""));
            return Json(SubscriptionJson(id, _subscriptions.First(kv => kv.Value == id).Key));
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
        {
            var reference = JsonDocument.Parse(body!).RootElement.GetProperty("subscription").GetProperty("reference").GetString()!;
            var failure = OnCreateSubscription?.Invoke(request);
            if (failure != null) return failure;
            var id = _subscriptions.GetOrAdd(reference, _ => Interlocked.Increment(ref _nextId));
            return Json(SubscriptionJson(id, reference), HttpStatusCode.Created);
        }

        return new HttpResponseMessage(HttpStatusCode.NotImplemented);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string ReferenceFromQuery(string query) => query.Split("reference=")[1].Split('&')[0];

    private static HttpResponseMessage NotFound() => Json("""{"errors":["Not Found"]}""", HttpStatusCode.NotFound);

    private static string CustomerJson(int id, string reference) =>
        $$$"""{"customer":{"id":{{{id}}},"reference":"{{{reference}}}","email":"x@example.com"}}""";

    private static string SubscriptionJson(int id, string reference) =>
        $$$"""
        {"subscription":{"id":{{{id}}},"state":"active","product_price_in_cents":29900,
          "next_assessment_at":"2030-01-01T00:00:00Z","reference":"{{{reference}}}",
          "product":{"handle":"eshop-pro","name":"Pro Plan"},"customer":{"id":7}
        }}
        """;
}
