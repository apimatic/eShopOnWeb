using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.Square;

public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body, string? Authorization,
    IReadOnlyDictionary<string, byte[]> Parts, IReadOnlyDictionary<string, string?> PartContentTypes)
{
    public JsonNode? Json => Body is null ? null : JsonNode.Parse(Body);
}

/// <summary>
/// An in-process stand-in for the Square API, plugged in as the primary handler of the SDK's HttpClient.
/// It keeps just enough state (catalog, orders, custom attributes, idempotency keys) to exercise the integration
/// without network access, and lets a test inject faults.
/// </summary>
public sealed class FakeSquare : HttpMessageHandler
{
    private readonly object _gate = new();
    private int _sequence;

    public string MerchantId { get; set; } = "MERCHANT_1";
    public string BusinessName { get; set; } = "Fake Coffee";
    public string LocationId { get; set; } = "LOCATION_1";
    public string Currency { get; set; } = "USD";
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    public List<RecordedRequest> Requests { get; } = new();
    public Dictionary<string, JsonObject> CatalogObjects { get; } = new();
    public Dictionary<string, JsonObject> Orders { get; } = new();
    public Dictionary<string, (string Value, int Version)> GiftMessages { get; } = new();
    public JsonObject? GiftDefinition { get; set; }
    private readonly Dictionary<string, string> _idempotentResponses = new();

    /// <summary>Return a response to answer the request without touching the fake's state.</summary>
    public Func<RecordedRequest, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>Return true to process the request and then drop the response (the write landed, the caller never hears).</summary>
    public Func<RecordedRequest, bool>? LoseResponse { get; set; }

    /// <summary>Token endpoint behaviour; default issues a 30-day token.</summary>
    public Func<JsonNode, HttpResponseMessage>? OnToken { get; set; }

    public IEnumerable<RecordedRequest> RequestsTo(HttpMethod method, string pathPrefix) =>
        Snapshot().Where(r => r.Method == method && r.Path.StartsWith(pathPrefix, StringComparison.Ordinal));

    public List<RecordedRequest> Snapshot()
    {
        lock (_gate) return Requests.ToList();
    }

    public IEnumerable<JsonObject> ItemsCreatedByShop() =>
        CatalogObjects.Values.Where(o => (string?)o["type"] == "ITEM" && o["is_deleted"]?.GetValue<bool>() != true
            && (o["item_data"]?["variations"]?.AsArray() ?? new JsonArray())
                .Any(v => ((string?)v?["item_variation_data"]?["sku"])?.StartsWith("eshop-item-") == true));

    /// <summary>A catalog item the merchant had before the shop connected; it must never be written.</summary>
    public string AddForeignItem(string name)
    {
        var id = NextId("FOREIGN");
        CatalogObjects[id] = JsonNode.Parse($$$"""
            {"type":"ITEM","id":"{{{id}}}","version":1,"item_data":{"name":"{{{name}}}","variations":[
              {"type":"ITEM_VARIATION","id":"{{{id}}}_V","version":1,"item_variation_data":{"item_id":"{{{id}}}","name":"Regular",
               "pricing_type":"FIXED_PRICING","price_money":{"amount":100,"currency":"USD"} } } ] } }
            """)!.AsObject();
        return id;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var parts = new Dictionary<string, byte[]>();
        var partTypes = new Dictionary<string, string?>();
        string? body = null;
        if (request.Content is MultipartFormDataContent multipart)
        {
            foreach (var part in multipart)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? "";
                parts[name] = await part.ReadAsByteArrayAsync(cancellationToken);
                partTypes[name] = part.Headers.ContentType?.MediaType;
            }
        }
        else if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var recorded = new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body,
            request.Headers.Authorization?.ToString(), parts, partTypes);
        lock (_gate) Requests.Add(recorded);

        var intercepted = Intercept?.Invoke(recorded);
        if (intercepted is not null) return intercepted;

        HttpResponseMessage response;
        lock (_gate) response = Route(recorded);

        if (LoseResponse?.Invoke(recorded) == true)
            throw new HttpRequestException("connection reset by fake");
        return response;
    }

    private HttpResponseMessage Route(RecordedRequest r)
    {
        var path = r.Path;
        Match m;
        if (r.Method == HttpMethod.Post && path == "/oauth2/token") return Token(r);
        if (r.Method == HttpMethod.Get && path == "/v2/merchants/me")
            return Ok(new JsonObject
            {
                ["merchant"] = new JsonObject
                {
                    ["id"] = MerchantId, ["business_name"] = BusinessName, ["country"] = "US", ["currency"] = Currency,
                    ["main_location_id"] = LocationId, ["status"] = "ACTIVE",
                },
            });
        if (r.Method == HttpMethod.Get && path == "/v2/locations")
            return Ok(JsonNode.Parse($$$"""
                {"locations":[
                  {"id":"INACTIVE_1","name":"Old shop","status":"INACTIVE","currency":"{{{Currency}}}"},
                  {"id":"{{{LocationId}}}","name":"Main street","status":"ACTIVE","currency":"{{{Currency}}}",
                   "address":{"address_line_1":"1 Main St","locality":"Springfield","administrative_district_level_1":"IL","postal_code":"62701","country":"US"}}]}
                """)!);
        if (r.Method == HttpMethod.Post && path == "/v2/catalog/batch-retrieve")
        {
            var ids = r.Json!["object_ids"]!.AsArray().Select(n => (string)n!).ToList();
            var found = new JsonArray(ids.Where(id => CatalogObjects.TryGetValue(id, out var o) && o["is_deleted"]?.GetValue<bool>() != true)
                .Select(id => (JsonNode)CatalogObjects[id].DeepClone()).ToArray());
            return Ok(new JsonObject { ["objects"] = found });
        }
        if (r.Method == HttpMethod.Post && path == "/v2/catalog/object") return Idempotent(r, UpsertCatalogObject);
        if (r.Method == HttpMethod.Post && path == "/v2/catalog/search") return SearchCatalog(r);
        if (r.Method == HttpMethod.Post && path == "/v2/catalog/images") return CreateImage(r);
        if (r.Method == HttpMethod.Get && (m = Regex.Match(path, "^/v2/catalog/object/([^/]+)$")).Success)
        {
            var id = Uri.UnescapeDataString(m.Groups[1].Value);
            return CatalogObjects.TryGetValue(id, out var o) && o["is_deleted"]?.GetValue<bool>() != true
                ? Ok(new JsonObject { ["object"] = o.DeepClone() })
                : Error(HttpStatusCode.NotFound, "NOT_FOUND", "INVALID_REQUEST_ERROR");
        }
        if (r.Method == HttpMethod.Get && path == "/v2/orders/custom-attribute-definitions/gift-message")
            return GiftDefinition is null
                ? Error(HttpStatusCode.NotFound, "NOT_FOUND", "INVALID_REQUEST_ERROR")
                : Ok(new JsonObject { ["custom_attribute_definition"] = GiftDefinition.DeepClone() });
        if (r.Method == HttpMethod.Post && path == "/v2/orders/custom-attribute-definitions")
        {
            if (GiftDefinition is not null) return Error(HttpStatusCode.Conflict, "CONFLICT", "INVALID_REQUEST_ERROR");
            GiftDefinition = r.Json!["custom_attribute_definition"]!.DeepClone().AsObject();
            GiftDefinition["version"] = 1;
            return Ok(new JsonObject { ["custom_attribute_definition"] = GiftDefinition.DeepClone() });
        }
        if ((m = Regex.Match(path, "^/v2/orders/([^/]+)/custom-attributes/gift-message$")).Success)
        {
            var orderId = m.Groups[1].Value;
            if (!Orders.ContainsKey(orderId)) return Error(HttpStatusCode.NotFound, "NOT_FOUND", "INVALID_REQUEST_ERROR");
            if (r.Method == HttpMethod.Get)
                return GiftMessages.TryGetValue(orderId, out var g)
                    ? Ok(new JsonObject { ["custom_attribute"] = new JsonObject { ["key"] = "gift-message", ["value"] = g.Value, ["version"] = g.Version } })
                    : Error(HttpStatusCode.NotFound, "NOT_FOUND", "INVALID_REQUEST_ERROR");
            if (GiftDefinition is null) return Error(HttpStatusCode.BadRequest, "BAD_REQUEST", "INVALID_REQUEST_ERROR");
            var attribute = r.Json!["custom_attribute"]!;
            var version = attribute["version"]?.GetValue<int>();
            if (GiftMessages.TryGetValue(orderId, out var existing) && version != existing.Version)
                return Error(HttpStatusCode.BadRequest, "BAD_REQUEST", "INVALID_REQUEST_ERROR"); // Square: version required to update
            var next = (attribute["value"]!.GetValue<string>(), existing.Version + 1);
            GiftMessages[orderId] = next;
            return Ok(new JsonObject { ["custom_attribute"] = new JsonObject { ["key"] = "gift-message", ["value"] = next.Item1, ["version"] = next.Item2 } });
        }
        if (r.Method == HttpMethod.Post && path == "/v2/orders") return Idempotent(r, CreateOrder);
        if (r.Method == HttpMethod.Get && (m = Regex.Match(path, "^/v2/orders/([^/]+)$")).Success)
            return Orders.TryGetValue(m.Groups[1].Value, out var order)
                ? Ok(new JsonObject { ["order"] = order.DeepClone() })
                : Error(HttpStatusCode.NotFound, "NOT_FOUND", "INVALID_REQUEST_ERROR");

        return Error(HttpStatusCode.NotFound, "NOT_FOUND", "INVALID_REQUEST_ERROR");
    }

    /// <summary>Simulates a Square staff member editing the gift message in Square.</summary>
    public void StaffEditsGiftMessage(string squareOrderId, string text)
    {
        lock (_gate)
        {
            var current = GiftMessages[squareOrderId];
            GiftMessages[squareOrderId] = (text, current.Version + 1);
        }
    }

    private HttpResponseMessage Token(RecordedRequest r)
    {
        if (OnToken is not null) return OnToken(r.Json!);
        var grant = (string?)r.Json!["grant_type"];
        var n = Interlocked.Increment(ref _sequence);
        return Ok(new JsonObject
        {
            ["access_token"] = $"oauth-access-{n}",
            ["token_type"] = "bearer",
            ["expires_at"] = Now().AddDays(30).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["merchant_id"] = MerchantId,
            ["refresh_token"] = grant == "refresh_token" ? (string?)r.Json["refresh_token"] : "oauth-refresh-1",
        });
    }

    private HttpResponseMessage Idempotent(RecordedRequest r, Func<RecordedRequest, HttpResponseMessage> handler)
    {
        var key = (string?)r.Json!["idempotency_key"];
        if (key is not null && _idempotentResponses.TryGetValue(r.Path + key, out var cached))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(cached, Encoding.UTF8, "application/json") };
        var response = handler(r);
        if (key is not null && response.IsSuccessStatusCode)
            _idempotentResponses[r.Path + key] = response.Content.ReadAsStringAsync().Result;
        return response;
    }

    private HttpResponseMessage UpsertCatalogObject(RecordedRequest r)
    {
        var obj = r.Json!["object"]!.DeepClone().AsObject();
        var mappings = new JsonArray();
        var id = (string)obj["id"]!;
        if (id.StartsWith('#'))
        {
            var newId = NextId("ITEM");
            mappings.Add(new JsonObject { ["client_object_id"] = id, ["object_id"] = newId });
            obj["id"] = newId;
            obj["version"] = 1L;
            foreach (var variation in obj["item_data"]?["variations"]?.AsArray() ?? new JsonArray())
            {
                var variationId = NextId("VAR");
                mappings.Add(new JsonObject { ["client_object_id"] = (string)variation!["id"]!, ["object_id"] = variationId });
                variation["id"] = variationId;
                variation["version"] = 1L;
                variation["item_variation_data"]!["item_id"] = newId;
            }
        }
        else
        {
            if (!CatalogObjects.TryGetValue(id, out var current) || current["is_deleted"]?.GetValue<bool>() == true)
                return Error(HttpStatusCode.NotFound, "NOT_FOUND", "INVALID_REQUEST_ERROR");
            if (obj["version"]?.GetValue<long>() != current["version"]!.GetValue<long>())
                return Error(HttpStatusCode.BadRequest, "VERSION_MISMATCH", "INVALID_REQUEST_ERROR");
            obj["version"] = current["version"]!.GetValue<long>() + 1;
            foreach (var variation in obj["item_data"]?["variations"]?.AsArray() ?? new JsonArray())
            {
                if (((string)variation!["id"]!).StartsWith('#'))
                {
                    var variationId = NextId("VAR");
                    mappings.Add(new JsonObject { ["client_object_id"] = (string)variation["id"]!, ["object_id"] = variationId });
                    variation["id"] = variationId;
                }
                variation["version"] = (variation["version"]?.GetValue<long>() ?? 0) + 1;
            }
        }
        CatalogObjects[(string)obj["id"]!] = obj;
        return Ok(new JsonObject { ["catalog_object"] = obj.DeepClone(), ["id_mappings"] = mappings });
    }

    private HttpResponseMessage SearchCatalog(RecordedRequest r)
    {
        var exact = r.Json!["query"]?["exact_query"];
        var sku = (string?)exact?["attribute_value"];
        var variations = CatalogObjects.Values
            .Where(o => o["is_deleted"]?.GetValue<bool>() != true)
            .SelectMany(o => o["item_data"]?["variations"]?.AsArray() ?? new JsonArray())
            .Where(v => (string?)v!["item_variation_data"]?["sku"] == sku)
            .Select(v => (JsonNode)v!.DeepClone())
            .ToArray();
        return Ok(new JsonObject { ["objects"] = new JsonArray(variations) });
    }

    private HttpResponseMessage CreateImage(RecordedRequest r)
    {
        var request = JsonNode.Parse(Encoding.UTF8.GetString(r.Parts["request"]))!;
        var key = (string)request["idempotency_key"]!;
        if (_idempotentResponses.TryGetValue("/v2/catalog/images" + key, out var cached))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(cached, Encoding.UTF8, "application/json") };

        var objectId = (string?)request["object_id"];
        if (objectId is null || !CatalogObjects.TryGetValue(objectId, out var item))
            return Error(HttpStatusCode.NotFound, "NOT_FOUND", "INVALID_REQUEST_ERROR");
        var imageId = NextId("IMAGE");
        var image = request["image"]!.DeepClone().AsObject();
        image["id"] = imageId;
        image["version"] = 1L;
        image["image_data"]!["url"] = $"https://images.fake-square.test/{imageId}.img";
        CatalogObjects[imageId] = image;

        var imageIds = item["item_data"]!["image_ids"]?.AsArray().Select(n => (string)n!).ToList() ?? new List<string>();
        if (request["is_primary"]?.GetValue<bool>() == true) imageIds.Insert(0, imageId); else imageIds.Add(imageId);
        item["item_data"]!["image_ids"] = new JsonArray(imageIds.Select(i => (JsonNode)i).ToArray());
        item["version"] = item["version"]!.GetValue<long>() + 1;

        var response = new JsonObject { ["image"] = image.DeepClone() }.ToJsonString();
        _idempotentResponses["/v2/catalog/images" + key] = response;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
    }

    private HttpResponseMessage CreateOrder(RecordedRequest r)
    {
        var order = r.Json!["order"]!.DeepClone().AsObject();
        if ((string?)order["location_id"] != LocationId)
            return Error(HttpStatusCode.BadRequest, "NOT_FOUND", "INVALID_REQUEST_ERROR");
        var id = NextId("ORDER");
        order["id"] = id;
        order["state"] = "OPEN";
        order["version"] = 1L;
        Orders[id] = order;
        return Ok(new JsonObject { ["order"] = order.DeepClone() });
    }

    private string NextId(string prefix) => $"{prefix}_{Interlocked.Increment(ref _sequence)}";

    public static HttpResponseMessage Ok(JsonNode body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Error(HttpStatusCode status, string code, string category) =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { errors = new[] { new { category, code, detail = "fake" } } }),
                Encoding.UTF8, "application/json"),
        };
}
