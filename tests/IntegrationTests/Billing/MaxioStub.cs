#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// An in-process stand-in for the Maxio API behind the SDK's HttpClient seam. Routes are keyed by
/// "METHOD /path"; every request (and its body, captured while still readable) is recorded.
/// </summary>
public sealed class MaxioStub : HttpMessageHandler
{
    public const string BaseUrl = "https://maxio.test";

    private readonly Dictionary<string, Func<HttpRequestMessage, string?, Task<HttpResponseMessage>>> _routes = new();

    public ConcurrentQueue<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

    /// <summary>Scheme + host of every request, to assert where traffic went.</summary>
    public ConcurrentQueue<string> Authorities { get; } = new();

    public void OnAsync(HttpMethod method, string path, Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> respond) =>
        _routes[$"{method} {path}"] = respond;

    public void On(HttpMethod method, string path, Func<HttpRequestMessage, string?, HttpResponseMessage> respond) =>
        OnAsync(method, path, (r, b) => Task.FromResult(respond(r, b)));

    public int Count(HttpMethod method, string path) =>
        Requests.Count(r => r.Method == method && r.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        Requests.Enqueue((request.Method, path, body));
        Authorities.Enqueue(request.RequestUri.GetLeftPart(UriPartial.Authority));

        if (!_routes.TryGetValue($"{request.Method} {path}", out var respond))
        {
            return Json(HttpStatusCode.NotFound, """{"errors":["no stub route"]}""");
        }

        var response = await respond(request, body);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static string Product(string handle, string name, long priceInCents, string family = "test-family", string? archivedAt = null) =>
        "{\"product\":{\"id\":" + Math.Abs(handle.GetHashCode() % 100000) + ",\"name\":\"" + name + "\",\"handle\":\"" + handle +
        "\",\"price_in_cents\":" + priceInCents + ",\"interval\":1,\"interval_unit\":\"month\",\"archived_at\":" +
        (archivedAt is null ? "null" : "\"" + archivedAt + "\"") +
        ",\"product_family\":{\"handle\":\"" + family + "\"}}}";

    public static string Subscription(int id, string reference, string handle = "pro", string state = "active", long priceInCents = 29900) =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"" + state + "\",\"reference\":\"" + reference +
        "\",\"product_price_in_cents\":" + priceInCents +
        ",\"currency\":\"USD\",\"current_period_ends_at\":\"2026-11-19T00:00:00Z\",\"created_at\":\"2026-10-19T00:00:00Z\"" +
        ",\"product\":{\"handle\":\"" + handle + "\",\"name\":\"Pro Plan\",\"interval\":1,\"interval_unit\":\"month\"" +
        ",\"product_family\":{\"handle\":\"test-family\"}}}}";

    /// <summary>The <c>subscription.reference</c> a CreateSubscription body carried.</summary>
    public static string SentSubscriptionReference(string? body) =>
        JsonNode.Parse(body!)!["subscription"]!["reference"]!.GetValue<string>();
}
