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

/// <summary>
/// A small stateful stand-in for the Maxio API, plugged in under the SDK as the primary handler of the
/// "Maxio" HttpClient. No request leaves the process.
/// </summary>
public sealed class FakeMaxioHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private string? _customerJson;
    private string? _subscriptionJson;

    public enum CreateBehavior
    {
        Succeed,
        /// <summary>Maxio refuses with 422 and an errors[] body.</summary>
        Reject,
        /// <summary>The subscription is created at Maxio, then the connection drops before the response arrives.</summary>
        LandThenDropConnection,
        /// <summary>The connection drops and the subscription was never created.</summary>
        DropConnectionWithoutLanding
    }

    public CreateBehavior CreateSubscription { get; set; } = CreateBehavior.Succeed;

    /// <summary>Customer create behavior (<see cref="CreateBehavior.Reject"/> is not modelled for customers).</summary>
    public CreateBehavior CreateCustomer { get; set; } = CreateBehavior.Succeed;

    /// <summary>Delay applied to lookups, to widen race windows in concurrency tests.</summary>
    public TimeSpan LookupDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Every request in arrival order: method, path, body (read inside the handler, while still readable).</summary>
    public ConcurrentQueue<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

    /// <summary>Full request URIs (host included), in arrival order.</summary>
    public ConcurrentQueue<Uri> Uris { get; } = new();

    public int Count(HttpMethod method, string pathSuffix) =>
        Requests.Count(r => r.Method == method && r.Path.EndsWith(pathSuffix, StringComparison.Ordinal));

    public string? LastBody(HttpMethod method, string pathSuffix) =>
        Requests.LastOrDefault(r => r.Method == method && r.Path.EndsWith(pathSuffix, StringComparison.Ordinal)).Body;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uris.Enqueue(request.RequestUri!);
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request.Method, path, body));

        if (request.Method == HttpMethod.Get && path.EndsWith("/products.json", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, """
                [
                  {"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
                  {"product":{"id":2,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}
                ]
                """);
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
        {
            await Task.Delay(LookupDelay, cancellationToken);
            lock (_gate)
            {
                return _customerJson is null ? Json(HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}") : Json(HttpStatusCode.OK, _customerJson);
            }
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.Ordinal))
        {
            if (CreateCustomer == CreateBehavior.DropConnectionWithoutLanding)
            {
                throw new HttpRequestException("connection reset by peer");
            }
            using var doc = JsonDocument.Parse(body!);
            var reference = doc.RootElement.GetProperty("customer").GetProperty("reference").GetString();
            lock (_gate)
            {
                _customerJson = $$$"""{"customer":{"id":501,"reference":"{{{reference}}}"}}""";
            }
            if (CreateCustomer == CreateBehavior.LandThenDropConnection)
            {
                throw new HttpRequestException("connection reset by peer");
            }
            return Json(HttpStatusCode.Created, _customerJson);
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json", StringComparison.Ordinal))
        {
            await Task.Delay(LookupDelay, cancellationToken);
            lock (_gate)
            {
                return _subscriptionJson is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(HttpStatusCode.OK, _subscriptionJson);
            }
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
        {
            switch (CreateSubscription)
            {
                case CreateBehavior.Reject:
                    return Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"No payment method was on file for the $299.00 balance\"]}");
                case CreateBehavior.DropConnectionWithoutLanding:
                    throw new HttpRequestException("connection reset by peer");
            }

            using var doc = JsonDocument.Parse(body!);
            var subscription = doc.RootElement.GetProperty("subscription");
            var reference = subscription.GetProperty("reference").GetString();
            var handle = subscription.GetProperty("product_handle").GetString();
            var price = handle == "eshop-pro" ? 29900 : 2900;
            lock (_gate)
            {
                _subscriptionJson = $$$"""
                    {"subscription":{"id":901,"state":"active","reference":"{{{reference}}}",
                     "product":{"id":1,"handle":"{{{handle}}}","name":"Plan {{{handle}}}"},
                     "product_price_in_cents":{{{price}}},"currency":"USD",
                     "next_assessment_at":"2026-12-13T00:00:00-05:00","current_period_ends_at":"2026-12-13T00:00:00-05:00"}}
                    """;
            }
            if (CreateSubscription == CreateBehavior.LandThenDropConnection)
            {
                throw new HttpRequestException("connection reset by peer");
            }
            return Json(HttpStatusCode.Created, _subscriptionJson);
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions.json", StringComparison.Ordinal) && path.Contains("/customers/"))
        {
            lock (_gate)
            {
                return Json(HttpStatusCode.OK, _subscriptionJson is null ? "[]" : "[" + _subscriptionJson + "]");
            }
        }

        return Json(HttpStatusCode.NotFound, "{\"errors\":[\"unexpected request in test\"]}");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
