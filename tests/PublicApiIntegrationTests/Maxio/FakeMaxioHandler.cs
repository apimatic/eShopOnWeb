using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.Maxio;

/// <summary>An in-process stand-in for the Maxio HTTP API: no test ever leaves the process.</summary>
public sealed class FakeMaxioHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, JsonObject> _customers = new();
    private readonly Dictionary<string, JsonObject> _subscriptions = new();
    private int _nextId = 1000;

    public List<string> Calls { get; } = new();
    public HashSet<string> Hosts { get; } = new();

    /// <summary>When set, the next subscription POST is stored and then fails at the transport level.</summary>
    public bool DropConnectionAfterSubscriptionPost { get; set; }

    /// <summary>When set, lookups by reference also fail at the transport level.</summary>
    public bool FailLookups { get; set; }

    /// <summary>When set, lookups start failing once the dropped subscription POST has happened.</summary>
    public bool FailLookupsAfterDrop { get; set; }

    /// <summary>When set, the subscription POST is refused with a 422.</summary>
    public bool RefuseSubscriptionPost { get; set; }

    public int Count(string call) { lock (_gate) return Calls.Count(c => c == call); }

    public void Reset()
    {
        lock (_gate)
        {
            Calls.Clear(); _customers.Clear(); _subscriptions.Clear();
            DropConnectionAfterSubscriptionPost = false; FailLookups = false; FailLookupsAfterDrop = false; RefuseSubscriptionPost = false;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        await Task.Delay(25, cancellationToken);   // widen the race window for concurrency tests
        lock (_gate)
        {
            Calls.Add($"{request.Method} {path}");
            Hosts.Add(request.RequestUri.Host);
            switch ((request.Method.Method, path))
            {
                case ("GET", "/site.json"):
                    return Json(HttpStatusCode.OK, """{"site":{"subdomain":"fake","test":true,"relationship_invoicing_enabled":false}}""");

                case ("GET", var p) when p.EndsWith("/products.json"):
                    return Json(HttpStatusCode.OK, $"[{Product("eshop-pro", "Pro Plan", 29900)},{Product("basic-plan", "Basic Plan", 2900)},{Product("old-plan", "Old", 100, archived: true)}]");

                case ("GET", "/customers/lookup.json"):
                    if (FailLookups) throw new HttpRequestException("connection reset");
                    return _customers.TryGetValue(query["reference"]!, out var c)
                        ? Json(HttpStatusCode.OK, new JsonObject { ["customer"] = c.DeepClone() }.ToJsonString())
                        : Json(HttpStatusCode.NotFound, "{}");

                case ("POST", "/customers.json"):
                {
                    var customer = JsonNode.Parse(body!)!["customer"]!.AsObject();
                    var stored = new JsonObject { ["id"] = ++_nextId, ["reference"] = customer["reference"]!.GetValue<string>(), ["email"] = customer["email"]!.GetValue<string>() };
                    _customers[stored["reference"]!.GetValue<string>()] = stored;
                    return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = stored.DeepClone() }.ToJsonString());
                }

                case ("GET", "/subscriptions/lookup.json"):
                    if (FailLookups) throw new HttpRequestException("connection reset");
                    return _subscriptions.TryGetValue(query["reference"]!, out var s)
                        ? Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = s.DeepClone() }.ToJsonString())
                        : Json(HttpStatusCode.NotFound, "{}");

                case ("POST", "/subscriptions.json"):
                {
                    if (RefuseSubscriptionPost)
                        return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file"]}""");
                    var sub = JsonNode.Parse(body!)!["subscription"]!.AsObject();
                    var handle = sub["product_handle"]!.GetValue<string>();
                    var stored = new JsonObject
                    {
                        ["id"] = ++_nextId,
                        ["state"] = "active",
                        ["reference"] = sub["reference"]!.GetValue<string>(),
                        ["product_price_in_cents"] = handle == "eshop-pro" ? 29900 : 2900,
                        ["next_assessment_at"] = "2030-01-01T00:00:00Z",
                        ["created_at"] = "2029-12-01T00:00:00Z",
                        ["product"] = JsonNode.Parse(Product(handle, handle, handle == "eshop-pro" ? 29900 : 2900))!["product"]!.DeepClone()
                    };
                    _subscriptions[stored["reference"]!.GetValue<string>()] = stored;
                    if (DropConnectionAfterSubscriptionPost)
                    {
                        DropConnectionAfterSubscriptionPost = false;
                        if (FailLookupsAfterDrop) FailLookups = true;
                        throw new HttpRequestException("connection reset");
                    }
                    return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = stored.DeepClone() }.ToJsonString());
                }

                case ("GET", "/subscriptions.json"):
                {
                    var list = new JsonArray(_subscriptions.Values.Select(v => (JsonNode)new JsonObject { ["subscription"] = v.DeepClone() }).ToArray());
                    return Json(HttpStatusCode.OK, list.ToJsonString());
                }
            }
            return Json(HttpStatusCode.NotFound, "{}");
        }
    }

    private static string Product(string handle, string name, int cents, bool archived = false) =>
        new JsonObject
        {
            ["product"] = new JsonObject
            {
                ["id"] = 1,
                ["handle"] = handle,
                ["name"] = name,
                ["price_in_cents"] = cents,
                ["interval"] = 1,
                ["interval_unit"] = "month",
                ["archived_at"] = archived ? "2020-01-01T00:00:00Z" : null
            }
        }.ToJsonString();

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
