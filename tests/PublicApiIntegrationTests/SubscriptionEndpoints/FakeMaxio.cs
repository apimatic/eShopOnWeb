using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// An in-memory Maxio site behind the SDK's HttpClient seam: it serves the routes the subscription flow calls,
/// records every request it receives, and lets a test inject failures per request.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    public const string FamilyHandle = "test-family";

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _customersByReference = new();
    private readonly List<JsonElementSubscription> _subscriptions = new();
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private int _nextId = 1000;

    /// <summary>Checked before routing; return a response (or throw) to fail that request.</summary>
    public Func<RecordedRequest, HttpResponseMessage?>? Fault { get; set; }

    /// <summary>How long a subscription create takes to answer.</summary>
    public TimeSpan CreateSubscriptionDelay { get; set; } = TimeSpan.Zero;

    public bool RelationshipInvoicing { get; set; } = true;

    public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

    public int Count(string method, string path) =>
        _requests.Count(r => r.Method == method && r.Path == path);

    public int SubscriptionCount { get { lock (_gate) return _subscriptions.Count; } }

    /// <summary>Ends every subscription, as a cancellation in Maxio would.</summary>
    public void CancelAllSubscriptions()
    {
        lock (_gate)
        {
            for (var i = 0; i < _subscriptions.Count; i++)
            {
                _subscriptions[i] = _subscriptions[i] with { State = "canceled" };
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Read the body now: the SDK disposes the request content after the attempt.
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method.Method, request.RequestUri!.Host, Uri.UnescapeDataString(request.RequestUri.AbsolutePath),
            Query(request.RequestUri), body, request.Headers.Authorization?.ToString());
        _requests.Enqueue(recorded);

        var response = Fault?.Invoke(recorded);
        if (response is null)
        {
            if (recorded.Method == "POST" && recorded.Path == "/subscriptions.json" && CreateSubscriptionDelay > TimeSpan.Zero)
            {
                await Task.Delay(CreateSubscriptionDelay, cancellationToken);
            }

            response = Route(recorded);
        }

        response.RequestMessage = request;
        return response;
    }

    // The SDK client is a singleton that outlives handler rotation; keep the fake usable for the whole test.
    protected override void Dispose(bool disposing)
    {
    }

    public HttpResponseMessage Route(RecordedRequest r)
    {
        lock (_gate)
        {
            switch (r.Method, r.Path)
            {
                case ("GET", "/site.json"):
                    return Json(HttpStatusCode.OK, new
                    {
                        site = new { id = 1, subdomain = "test-site", currency = "USD", relationship_invoicing_enabled = RelationshipInvoicing }
                    });

                case ("GET", "/product_families/handle:" + FamilyHandle + "/products.json"):
                    return Json(HttpStatusCode.OK, new object[]
                    {
                        new { product = Product("basic-plan", "Basic Plan", 2900) },
                        new { product = Product("eshop-pro", "Pro Plan", 29900) },
                        new { product = Product("retired-plan", "Retired Plan", 100, archived: true) }
                    });

                case ("GET", "/customers/lookup.json"):
                    return _customersByReference.TryGetValue(r.Query["reference"], out var foundId)
                        ? Json(HttpStatusCode.OK, new { customer = new { id = foundId, reference = r.Query["reference"] } })
                        : Json(HttpStatusCode.NotFound, new { errors = new[] { "Not Found" } });

                case ("POST", "/customers.json"):
                {
                    var customer = JsonDocument.Parse(r.Body!).RootElement.GetProperty("customer");
                    var reference = customer.GetProperty("reference").GetString()!;
                    if (_customersByReference.ContainsKey(reference))
                    {
                        return Json(HttpStatusCode.UnprocessableEntity, new { errors = new[] { "Reference must be unique." } });
                    }

                    var id = ++_nextId;
                    _customersByReference[reference] = id;
                    return Json(HttpStatusCode.Created, new { customer = new { id, reference } });
                }

                case ("POST", "/subscriptions.json"):
                {
                    var subscription = JsonDocument.Parse(r.Body!).RootElement.GetProperty("subscription");
                    var created = new JsonElementSubscription(++_nextId,
                        subscription.GetProperty("customer_id").GetInt32(),
                        subscription.GetProperty("product_handle").GetString()!,
                        subscription.GetProperty("reference").GetString()!,
                        "active");
                    _subscriptions.Add(created);
                    return Json(HttpStatusCode.Created, new { subscription = created.ToWire() });
                }

                case ("GET", "/subscriptions/lookup.json"):
                {
                    var match = _subscriptions.FirstOrDefault(s => s.Reference == r.Query["reference"]);
                    return match is null
                        ? new HttpResponseMessage(HttpStatusCode.NotFound)
                        : Json(HttpStatusCode.OK, new { subscription = match.ToWire() });
                }
            }

            var customerSubscriptions = Regex.Match(r.Path, @"^/customers/(\d+)/subscriptions\.json$");
            if (r.Method == "GET" && customerSubscriptions.Success)
            {
                var customerId = int.Parse(customerSubscriptions.Groups[1].Value);
                return Json(HttpStatusCode.OK, _subscriptions.Where(s => s.CustomerId == customerId)
                    .Select(s => new { subscription = s.ToWire() }).ToArray());
            }

            var readSubscription = Regex.Match(r.Path, @"^/subscriptions/(\d+)\.json$");
            if (r.Method == "GET" && readSubscription.Success)
            {
                var match = _subscriptions.FirstOrDefault(s => s.Id == int.Parse(readSubscription.Groups[1].Value));
                return match is null
                    ? Json(HttpStatusCode.NotFound, new { errors = new[] { "Not Found" } })
                    : Json(HttpStatusCode.OK, new { subscription = match.ToWire() });
            }

            return Json(HttpStatusCode.NotFound, new { errors = new[] { $"FakeMaxio has no route for {r.Method} {r.Path}" } });
        }
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static object Product(string handle, string name, long priceInCents, bool archived = false) => new
    {
        id = Math.Abs(handle.GetHashCode() % 100000),
        handle,
        name,
        price_in_cents = priceInCents,
        interval = 1,
        interval_unit = "month",
        archived_at = archived ? "2026-01-01T00:00:00Z" : null
    };

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : string.Empty);

    private sealed record JsonElementSubscription(int Id, int CustomerId, string ProductHandle, string Reference, string State)
    {
        public object ToWire() => new
        {
            id = Id,
            state = State,
            reference = Reference,
            product_price_in_cents = ProductHandle == "eshop-pro" ? 29900 : 2900,
            currency = "USD",
            current_period_ends_at = "2026-11-15T10:00:00+00:00",
            next_assessment_at = "2026-11-15T10:00:00+00:00",
            activated_at = "2026-10-15T10:00:00+00:00",
            created_at = "2026-10-15T10:00:00+00:00",
            customer = new { id = CustomerId },
            product = Product(ProductHandle, ProductHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan", ProductHandle == "eshop-pro" ? 29900 : 2900)
        };
    }
}

public sealed record RecordedRequest(string Method, string Host, string Path, IReadOnlyDictionary<string, string> Query, string? Body, string? Authorization);
