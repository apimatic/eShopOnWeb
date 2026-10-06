using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// An in-memory stand-in for the Maxio API, plugged in as the primary handler of the SDK's HttpClient.
/// It speaks the wire shapes the SDK expects, so the whole stack (endpoint → service → gateway → SDK) runs offline.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, JsonObject> _customersByReference = new();
    private readonly Dictionary<string, JsonObject> _subscriptionsByReference = new();
    private int _nextId = 1000;

    public int CustomerPosts { get; private set; }
    public int SubscriptionPosts { get; private set; }

    /// <summary>Every request hangs until cancelled (an unresponsive Maxio).</summary>
    public bool HangEverything { get; set; }

    /// <summary>POST subscriptions.json records the subscription, then never answers (lost response).</summary>
    public bool LoseCreateSubscriptionResponse { get; set; }

    /// <summary>Artificial latency on POST subscriptions.json, to widen double-submit races.</summary>
    public TimeSpan CreateSubscriptionDelay { get; set; } = TimeSpan.Zero;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (HangEverything)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        var path = request.RequestUri!.AbsolutePath;
        var reference = HttpUtility.ParseQueryString(request.RequestUri.Query)["reference"] ?? "";
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        if (request.Method == HttpMethod.Get && path.Contains("/product_families/") && path.EndsWith("/products.json"))
        {
            return Json(HttpStatusCode.OK, """
                [{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
                 {"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}]
                """);
        }
        if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json"))
        {
            lock (_gate)
            {
                return _customersByReference.TryGetValue(reference, out var customer)
                    ? Json(HttpStatusCode.OK, Wrap("customer", customer))
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }
        if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
        {
            var input = JsonNode.Parse(body!)!["customer"]!;
            lock (_gate)
            {
                CustomerPosts++;
                var customerReference = (string)input["reference"]!;
                if (_customersByReference.ContainsKey(customerReference))
                {
                    return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference must be unique"]}""");
                }
                var customer = new JsonObject
                {
                    ["id"] = _nextId++,
                    ["reference"] = customerReference,
                    ["email"] = (string?)input["email"]
                };
                _customersByReference[customerReference] = customer;
                return Json(HttpStatusCode.Created, Wrap("customer", customer));
            }
        }
        if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
        {
            lock (_gate)
            {
                return _subscriptionsByReference.TryGetValue(reference, out var subscription)
                    ? Json(HttpStatusCode.OK, Wrap("subscription", subscription))
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }
        if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
        {
            if (CreateSubscriptionDelay > TimeSpan.Zero)
            {
                await Task.Delay(CreateSubscriptionDelay, cancellationToken);
            }
            var input = JsonNode.Parse(body!)!["subscription"]!;
            JsonObject subscription;
            lock (_gate)
            {
                SubscriptionPosts++;
                var subscriptionReference = (string)input["reference"]!;
                if (_subscriptionsByReference.ContainsKey(subscriptionReference))
                {
                    return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference has already been taken"]}""");
                }
                var handle = (string)input["product_handle"]!;
                var price = handle == "eshop-pro" ? 29900 : 2900;
                var now = DateTimeOffset.UtcNow;
                subscription = new JsonObject
                {
                    ["id"] = _nextId++,
                    ["state"] = "active",
                    ["reference"] = subscriptionReference,
                    ["product_price_in_cents"] = price,
                    ["currency"] = "USD",
                    ["created_at"] = now.ToString("O"),
                    ["current_period_ends_at"] = now.AddMonths(1).ToString("O"),
                    ["next_assessment_at"] = now.AddMonths(1).ToString("O"),
                    ["payment_collection_method"] = (string?)input["payment_collection_method"],
                    ["customer"] = new JsonObject { ["id"] = (int)input["customer_id"]! },
                    ["product"] = new JsonObject
                    {
                        ["id"] = 1,
                        ["handle"] = handle,
                        ["name"] = handle == "eshop-pro" ? "Pro Plan" : "Basic Plan",
                        ["price_in_cents"] = price,
                        ["interval"] = 1,
                        ["interval_unit"] = "month"
                    }
                };
                _subscriptionsByReference[subscriptionReference] = subscription;
            }
            if (LoseCreateSubscriptionResponse)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return Json(HttpStatusCode.Created, Wrap("subscription", subscription));
        }
        var customerSubscriptions = Regex.Match(path, @"/customers/(\d+)/subscriptions\.json$");
        if (request.Method == HttpMethod.Get && customerSubscriptions.Success)
        {
            var customerId = int.Parse(customerSubscriptions.Groups[1].Value);
            lock (_gate)
            {
                var items = _subscriptionsByReference.Values
                    .Where(s => (int)s["customer"]!["id"]! == customerId)
                    .Select(s => (JsonNode?)JsonNode.Parse(Wrap("subscription", s)))
                    .ToArray();
                return Json(HttpStatusCode.OK, new JsonArray(items).ToJsonString());
            }
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string Wrap(string name, JsonObject value) => new JsonObject { [name] = value.DeepClone() }.ToJsonString();

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
