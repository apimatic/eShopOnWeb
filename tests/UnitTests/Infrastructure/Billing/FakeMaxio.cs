using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// An in-memory stand-in for the Maxio Advanced Billing HTTP API, plugged in under the SDK as its HttpClient's
/// primary handler. It implements the routes the integration calls, records every request (body buffered while
/// readable) and lets a test inject faults. Shared with PublicApiIntegrationTests (linked file).
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    private readonly object _gate = new();
    private int _nextId = 1000;

    public FakeMaxio(string familyHandle)
    {
        FamilyHandle = familyHandle;
    }

    public string FamilyHandle { get; }

    /// <summary>Path prefix in front of the API routes (for a base-URL override such as https://proxy/maxio).</summary>
    public string BasePath { get; set; } = string.Empty;

    public List<JsonObject> Products { get; } = new();

    /// <summary>The site's relationship_invoicing_enabled flag (null = omitted from GET /site.json).</summary>
    public bool? RelationshipInvoicingEnabled { get; set; } = true;

    public ConcurrentDictionary<string, JsonObject> CustomersByReference { get; } = new();

    public List<JsonObject> Subscriptions { get; } = new();

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>Runs before routing. Return a response (or throw) to inject a fault; return null to proceed normally.</summary>
    public Func<HttpRequestMessage, string?, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>When set, the next POST /subscriptions.json is applied and THEN the connection fails (ambiguous write).</summary>
    public bool DropConnectionAfterNextSubscriptionCreate { get; set; }

    /// <summary>When set, the next POST /customers.json is applied and THEN the connection fails (ambiguous write).</summary>
    public bool DropConnectionAfterNextCustomerCreate { get; set; }

    /// <summary>Optional delay applied to POST /subscriptions.json, to widen race windows.</summary>
    public TimeSpan SubscriptionCreateDelay { get; set; }

    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);

    public int Count(HttpMethod method, string path)
        => Requests.Count(r => r.Method == method && Uri.UnescapeDataString(r.Uri.AbsolutePath).EndsWith(path, StringComparison.Ordinal));

    public FakeMaxio WithProduct(string handle, string name, long priceInCents, bool archived = false)
    {
        Products.Add(new JsonObject
        {
            ["id"] = Interlocked.Increment(ref _nextId),
            ["name"] = name,
            ["handle"] = handle,
            ["description"] = name + " plan",
            ["price_in_cents"] = priceInCents,
            ["interval"] = 1,
            ["interval_unit"] = "month",
            ["require_credit_card"] = false,
            ["archived_at"] = archived ? "2024-01-01T00:00:00-05:00" : null
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, body));

        var injected = Intercept?.Invoke(request, body);
        if (injected is not null)
        {
            injected.RequestMessage = request;
            return injected;
        }

        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        if (BasePath.Length > 0 && path.StartsWith(BasePath, StringComparison.Ordinal))
        {
            path = path[BasePath.Length..];
        }
        var query = ParseQuery(request.RequestUri.Query);

        if (request.Method == HttpMethod.Get && path.StartsWith("/product_families/", StringComparison.Ordinal) && path.EndsWith("/products.json", StringComparison.Ordinal))
        {
            var familyId = path["/product_families/".Length..^"/products.json".Length];
            if (familyId != "handle:" + FamilyHandle)
            {
                return Json(HttpStatusCode.NotFound, "\"Not Found\"");
            }

            var page = int.Parse(query.GetValueOrDefault("page", "1"));
            var perPage = int.Parse(query.GetValueOrDefault("per_page", "20"));
            var items = new JsonArray();
            lock (_gate)
            {
                foreach (var p in Products.Skip((page - 1) * perPage).Take(perPage))
                {
                    items.Add(new JsonObject { ["product"] = p.DeepClone() });
                }
            }

            return Json(HttpStatusCode.OK, items.ToJsonString());
        }

        if (request.Method == HttpMethod.Get && path == "/site.json")
        {
            var site = new JsonObject { ["id"] = 1, ["subdomain"] = "fake-site", ["test"] = true };
            if (RelationshipInvoicingEnabled is bool ri)
            {
                site["relationship_invoicing_enabled"] = ri;
            }

            return Json(HttpStatusCode.OK, new JsonObject { ["site"] = site }.ToJsonString());
        }

        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            return CustomersByReference.TryGetValue(query.GetValueOrDefault("reference", ""), out var c)
                ? Json(HttpStatusCode.OK, new JsonObject { ["customer"] = c.DeepClone() }.ToJsonString())
                : new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
        }

        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            var input = JsonNode.Parse(body!)!["customer"]!.AsObject();
            var reference = input["reference"]?.GetValue<string>() ?? "";
            JsonObject created;
            lock (_gate)
            {
                if (reference.Length > 0 && CustomersByReference.ContainsKey(reference))
                {
                    return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference must be unique."]}""");
                }

                created = new JsonObject
                {
                    ["id"] = Interlocked.Increment(ref _nextId),
                    ["first_name"] = input["first_name"]?.GetValue<string>(),
                    ["last_name"] = input["last_name"]?.GetValue<string>(),
                    ["email"] = input["email"]?.GetValue<string>(),
                    ["reference"] = reference
                };
                CustomersByReference[reference] = created;
            }

            if (DropConnectionAfterNextCustomerCreate)
            {
                DropConnectionAfterNextCustomerCreate = false;
                throw new HttpRequestException("connection reset after the customer was created");
            }

            return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = created.DeepClone() }.ToJsonString());
        }

        if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            var reference = query.GetValueOrDefault("reference", "");
            JsonObject? found;
            lock (_gate)
            {
                found = Subscriptions.FirstOrDefault(s => s["reference"]?.GetValue<string>() == reference);
            }

            return found is not null
                ? Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = found.DeepClone() }.ToJsonString())
                : new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
        }

        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            if (SubscriptionCreateDelay > TimeSpan.Zero)
            {
                await Task.Delay(SubscriptionCreateDelay, cancellationToken);
            }

            var input = JsonNode.Parse(body!)!["subscription"]!.AsObject();
            var handle = input["product_handle"]?.GetValue<string>();
            var customerId = input["customer_id"]?.GetValue<int>();
            var collectionMethod = input["payment_collection_method"]?.GetValue<string>() ?? "automatic";
            JsonObject created;
            lock (_gate)
            {
                var product = Products.FirstOrDefault(p => p["handle"]!.GetValue<string>() == handle && p["archived_at"] is null);
                if (product is null)
                {
                    return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Product must be specified."]}""");
                }

                // Like the real site: with no payment method, only the architecture's invoice-style method is accepted.
                var invoiceStyle = RelationshipInvoicingEnabled == false ? "invoice" : "remittance";
                if (collectionMethod != invoiceStyle)
                {
                    return Json(HttpStatusCode.UnprocessableEntity,
                        $$"""{"errors":["No payment method was on file for the ${{product["price_in_cents"]!.GetValue<long>() / 100m:0.00}} balance"]}""");
                }

                var customer = CustomersByReference.Values.FirstOrDefault(c => c["id"]!.GetValue<int>() == customerId);
                if (customer is null)
                {
                    return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Customer must be specified."]}""");
                }

                created = new JsonObject
                {
                    ["id"] = Interlocked.Increment(ref _nextId),
                    ["state"] = "active",
                    ["reference"] = input["reference"]?.GetValue<string>(),
                    ["payment_collection_method"] = collectionMethod,
                    ["product_price_in_cents"] = product["price_in_cents"]!.GetValue<long>(),
                    ["current_period_ends_at"] = "2026-12-13T10:00:00-05:00",
                    ["next_assessment_at"] = "2026-12-13T10:00:00-05:00",
                    ["created_at"] = "2026-11-13T10:00:00-05:00",
                    ["customer"] = customer.DeepClone(),
                    ["product"] = product.DeepClone()
                };
                Subscriptions.Add(created);
            }

            if (DropConnectionAfterNextSubscriptionCreate)
            {
                DropConnectionAfterNextSubscriptionCreate = false;
                throw new HttpRequestException("connection reset after the subscription was created");
            }

            return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = created.DeepClone() }.ToJsonString());
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/customers/", StringComparison.Ordinal) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
        {
            var id = int.Parse(path["/customers/".Length..^"/subscriptions.json".Length]);
            var items = new JsonArray();
            lock (_gate)
            {
                foreach (var s in Subscriptions.Where(s => s["customer"]!["id"]!.GetValue<int>() == id))
                {
                    items.Add(new JsonObject { ["subscription"] = s.DeepClone() });
                }
            }

            return Json(HttpStatusCode.OK, items.ToJsonString());
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };

        HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
        }

        return result;
    }

    public static string JsonOf(object value) => JsonSerializer.Serialize(value);
}
