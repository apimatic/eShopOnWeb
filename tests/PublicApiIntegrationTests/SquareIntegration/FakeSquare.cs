using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SquareIntegration;

/// <summary>
/// An in-process stand-in for Square's API, plugged in as the primary handler of the "Square" HttpClient.
/// It keeps just enough state (merchants, tokens, catalog, orders, custom attributes) to drive the
/// integration end to end without network access, honours idempotency keys, and can inject faults.
/// </summary>
public sealed class FakeSquare : HttpMessageHandler
{
    public const string ConfiguredAccessToken = "configured-access-token";
    public const string MerchantId = "MERCHANT-SANDBOX-1";
    public const string BusinessName = "Fake Sandbox Seller";
    public const string LocationId = "LOCATION-MAIN";
    public const string OAuthMerchantId = "MERCHANT-OAUTH-2";
    public const string OAuthBusinessName = "OAuth Connected Seller";
    public const string OAuthLocationId = "LOCATION-OAUTH";

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _tokens = new() { [ConfiguredAccessToken] = MerchantId };
    private readonly Dictionary<string, string> _refreshTokens = new();
    private readonly Dictionary<string, JsonObject> _catalog = new();
    private readonly Dictionary<string, string> _variationToItem = new();
    private readonly Dictionary<string, JsonObject> _orders = new();
    private readonly Dictionary<string, JsonObject> _definitions = new();
    private readonly Dictionary<(string OrderId, string Key), JsonObject> _orderAttributes = new();
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _idempotent = new();
    private readonly List<Fault> _faults = new();
    private int _sequence;

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>Lifetime of access tokens issued by the code exchange.</summary>
    public TimeSpan IssuedAccessTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Lifetime of access tokens issued by a refresh.</summary>
    public TimeSpan RefreshedAccessTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    public IReadOnlyList<RecordedRequest> RequestsTo(string method, string pathPrefix) =>
        Requests.Where(r => r.Method == method && r.Path.StartsWith(pathPrefix, StringComparison.Ordinal)).ToList();

    public IReadOnlyList<RecordedRequest> SquareApiRequests => Requests.ToList();

    // ---- fault injection ------------------------------------------------------------------------

    public enum FaultKind
    {
        /// <summary>The request is processed by "Square", then the connection drops before the answer arrives.</summary>
        DropAfterProcessing,
        /// <summary>The connection drops before the request reaches "Square".</summary>
        DropBeforeProcessing,
        /// <summary>"Square" answers with the given status and error code.</summary>
        Respond,
    }

    /// <param name="pathPrefix">Exact path, or a prefix when it ends with '*'.</param>
    public void Fail(string method, string pathPrefix, FaultKind kind, int times = 1, HttpStatusCode status = HttpStatusCode.BadRequest, string code = "INVALID_VALUE")
    {
        lock (_gate)
        {
            _faults.Add(new Fault(method, pathPrefix, kind, times, status, code));
        }
    }

    // ---- state seeding / inspection -------------------------------------------------------------

    public void AddForeignItem(string id, string name, long amount)
    {
        lock (_gate)
        {
            var variationId = id + "-VAR";
            var item = new JsonObject
            {
                ["type"] = "ITEM",
                ["id"] = id,
                ["version"] = 1L,
                ["item_data"] = new JsonObject
                {
                    ["name"] = name,
                    ["variations"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "ITEM_VARIATION",
                        ["id"] = variationId,
                        ["version"] = 1L,
                        ["item_variation_data"] = new JsonObject
                        {
                            ["item_id"] = id,
                            ["name"] = "Regular",
                            ["sku"] = "FOREIGN-" + id,
                            ["pricing_type"] = "FIXED_PRICING",
                            ["price_money"] = new JsonObject { ["amount"] = amount, ["currency"] = "USD" },
                        },
                    }),
                },
            };
            _catalog[id] = item;
            _variationToItem[variationId] = id;
        }
    }

    public IReadOnlyList<JsonObject> Items
    {
        get
        {
            lock (_gate)
            {
                return _catalog.Values.Where(o => (string?)o["type"] == "ITEM").Select(o => (JsonObject)o.DeepClone()).ToList();
            }
        }
    }

    public JsonObject? ItemWithSku(string sku) => Items.FirstOrDefault(i =>
        i["item_data"]?["variations"]?.AsArray().Any(v => (string?)v?["item_variation_data"]?["sku"] == sku) == true);

    public static string ItemName(JsonObject item) => (string)item["item_data"]!["name"]!;

    public static long ItemPrice(JsonObject item) =>
        (long)item["item_data"]!["variations"]![0]!["item_variation_data"]!["price_money"]!["amount"]!;

    public IReadOnlyList<JsonObject> Orders
    {
        get
        {
            lock (_gate)
            {
                return _orders.Values.Select(o => (JsonObject)o.DeepClone()).ToList();
            }
        }
    }

    public JsonObject? Definition(string key)
    {
        lock (_gate)
        {
            return _definitions.TryGetValue(key, out var d) ? (JsonObject)d.DeepClone() : null;
        }
    }

    public string? OrderAttributeValue(string orderId, string key)
    {
        lock (_gate)
        {
            return _orderAttributes.TryGetValue((orderId, key), out var a) ? (string?)a["value"] : null;
        }
    }

    /// <summary>What a staff member does in Square: edit the field on the order.</summary>
    public void StaffEditsOrderAttribute(string orderId, string key, string value)
    {
        lock (_gate)
        {
            _orderAttributes[(orderId, key)] = new JsonObject { ["key"] = key, ["value"] = value, ["version"] = 2L };
        }
    }

    // ---- HTTP ---------------------------------------------------------------------------------------

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recorded = await RecordedRequest.CaptureAsync(request);
        Requests.Enqueue(recorded);

        var fault = TakeFault(recorded);
        if (fault is { Kind: FaultKind.DropBeforeProcessing })
        {
            throw new HttpRequestException("Simulated connection reset before the request reached Square");
        }

        if (fault is { Kind: FaultKind.Respond })
        {
            return Error(fault.Status, fault.Code, "Simulated Square error");
        }

        HttpResponseMessage response;
        lock (_gate)
        {
            response = Route(recorded);
        }

        if (fault is { Kind: FaultKind.DropAfterProcessing })
        {
            throw new HttpRequestException("Simulated connection reset after Square processed the request");
        }

        response.RequestMessage = request;
        return response;
    }

    private Fault? TakeFault(RecordedRequest request)
    {
        lock (_gate)
        {
            var fault = _faults.FirstOrDefault(f => f.Remaining > 0 && f.Method == request.Method && f.Matches(request.Path));
            if (fault is not null)
            {
                fault.Remaining--;
            }

            return fault;
        }
    }

    private HttpResponseMessage Route(RecordedRequest r)
    {
        var segments = r.Path.Trim('/').Split('/');
        if (r.Method == "POST" && r.Path == "/oauth2/token")
        {
            return ObtainToken(r);
        }

        var merchantId = Authenticate(r);
        if (merchantId is null)
        {
            return Error(HttpStatusCode.Unauthorized, "UNAUTHORIZED", "The access token is not valid.", "AUTHENTICATION_ERROR");
        }

        return (r.Method, r.Path) switch
        {
            ("GET", "/v2/merchants/me") => Merchant(merchantId),
            ("GET", "/v2/locations") => Locations(merchantId),
            ("POST", "/v2/catalog/batch-retrieve") => BatchRetrieve(r),
            ("POST", "/v2/catalog/search") => Search(r),
            ("POST", "/v2/catalog/batch-upsert") => Idempotent(r, () => BatchUpsert(r)),
            ("POST", "/v2/catalog/images") => CreateImage(r),
            ("POST", "/v2/orders") => Idempotent(r, () => CreateOrder(r, merchantId)),
            ("POST", "/v2/orders/custom-attribute-definitions") => Idempotent(r, () => CreateDefinition(r)),
            ("GET", _) when segments is ["v2", "orders", "custom-attribute-definitions", var key] => RetrieveDefinition(key),
            ("GET", _) when segments is ["v2", "orders", var orderId, "custom-attributes", var key] => RetrieveOrderAttribute(orderId, key),
            ("POST", _) when segments is ["v2", "orders", var orderId, "custom-attributes", var key] => Idempotent(r, () => UpsertOrderAttribute(r, orderId, key)),
            ("GET", _) when segments is ["v2", "orders", var orderId] => RetrieveOrder(orderId),
            _ => Error(HttpStatusCode.NotFound, "NOT_FOUND", $"No fake route for {r.Method} {r.Path}"),
        };
    }

    private string? Authenticate(RecordedRequest r)
    {
        var header = r.Authorization;
        if (header is null || !header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return null;
        }

        return _tokens.TryGetValue(header["Bearer ".Length..], out var merchant) ? merchant : null;
    }

    private HttpResponseMessage ObtainToken(RecordedRequest r)
    {
        var body = r.Json!;
        if ((string?)body["client_id"] != SquareApiFactory.ApplicationId || (string?)body["client_secret"] != SquareApiFactory.ApplicationSecret)
        {
            return Error(HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Bad client credentials.", "AUTHENTICATION_ERROR");
        }

        var grant = (string?)body["grant_type"];
        if (grant == "authorization_code")
        {
            var code = (string?)body["code"];
            if (code is null || !code.StartsWith("code-", StringComparison.Ordinal) || (string?)body["redirect_uri"] != SquareApiFactory.RedirectUri)
            {
                return Error(HttpStatusCode.BadRequest, "INVALID_REQUEST", "Bad authorization code.");
            }

            return IssueToken(OAuthMerchantId, refreshToken: $"refresh-{Next()}", IssuedAccessTokenLifetime);
        }

        if (grant == "refresh_token")
        {
            var refresh = (string?)body["refresh_token"];
            if (refresh is null || !_refreshTokens.TryGetValue(refresh, out var merchant))
            {
                return Error(HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Refresh token revoked.", "AUTHENTICATION_ERROR");
            }

            // Code flow: the same refresh token is returned.
            return IssueToken(merchant, refresh, RefreshedAccessTokenLifetime);
        }

        return Error(HttpStatusCode.BadRequest, "INVALID_REQUEST", "Unsupported grant type.");
    }

    public void RevokeRefreshTokens()
    {
        lock (_gate)
        {
            _refreshTokens.Clear();
        }
    }

    private HttpResponseMessage IssueToken(string merchantId, string refreshToken, TimeSpan lifetime)
    {
        var access = $"oauth-access-{Next()}";
        _tokens[access] = merchantId;
        _refreshTokens[refreshToken] = merchantId;
        return Json(new JsonObject
        {
            ["access_token"] = access,
            ["token_type"] = "bearer",
            ["expires_at"] = DateTimeOffset.UtcNow.Add(lifetime).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["merchant_id"] = merchantId,
            ["refresh_token"] = refreshToken,
        });
    }

    private static HttpResponseMessage Merchant(string merchantId) => Json(new JsonObject
    {
        ["merchant"] = new JsonObject
        {
            ["id"] = merchantId,
            ["business_name"] = merchantId == MerchantId ? BusinessName : OAuthBusinessName,
            ["country"] = "US",
            ["currency"] = "USD",
            ["status"] = "ACTIVE",
            ["main_location_id"] = merchantId == MerchantId ? LocationId : OAuthLocationId,
        },
    });

    private static HttpResponseMessage Locations(string merchantId) => Json(new JsonObject
    {
        ["locations"] = new JsonArray(
            new JsonObject
            {
                ["id"] = "LOCATION-INACTIVE",
                ["name"] = "Closed shop",
                ["status"] = "INACTIVE",
                ["merchant_id"] = merchantId,
                ["currency"] = "USD",
            },
            new JsonObject
            {
                ["id"] = merchantId == MerchantId ? LocationId : OAuthLocationId,
                ["name"] = "Main",
                ["status"] = "ACTIVE",
                ["merchant_id"] = merchantId,
                ["currency"] = "USD",
            }),
    });

    private HttpResponseMessage BatchRetrieve(RecordedRequest r)
    {
        var objects = new JsonArray();
        foreach (var id in r.Json!["object_ids"]!.AsArray().Select(n => (string)n!))
        {
            if (_catalog.TryGetValue(id, out var obj))
            {
                objects.Add(obj.DeepClone());
            }
            else if (_variationToItem.TryGetValue(id, out var itemId))
            {
                objects.Add(FindVariation(_catalog[itemId], id)!.DeepClone());
            }
        }

        return Json(new JsonObject { ["objects"] = objects });
    }

    private HttpResponseMessage Search(RecordedRequest r)
    {
        var skus = r.Json!["query"]!["set_query"]!["attribute_values"]!.AsArray().Select(n => (string)n!).ToHashSet();
        var objects = new JsonArray();
        foreach (var item in _catalog.Values.Where(o => (string?)o["type"] == "ITEM"))
        {
            foreach (var variation in item["item_data"]?["variations"]?.AsArray() ?? new JsonArray())
            {
                if (skus.Contains((string?)variation?["item_variation_data"]?["sku"] ?? string.Empty))
                {
                    objects.Add(variation!.DeepClone());
                }
            }
        }

        return Json(new JsonObject { ["objects"] = objects });
    }

    private HttpResponseMessage BatchUpsert(RecordedRequest r)
    {
        var mappings = new JsonArray();
        var results = new JsonArray();
        foreach (var batch in r.Json!["batches"]!.AsArray())
        {
            var tempIds = new Dictionary<string, string>();
            foreach (var obj in batch!["objects"]!.AsArray().Select(o => (JsonObject)o!.DeepClone()))
            {
                AssignIds(obj, tempIds);
            }

            foreach (var obj in batch["objects"]!.AsArray().Select(o => (JsonObject)o!.DeepClone()))
            {
                var stored = Store(obj, tempIds, out var conflict);
                if (conflict is not null)
                {
                    return Error(HttpStatusCode.BadRequest, "VERSION_MISMATCH", conflict);
                }

                results.Add(stored.DeepClone());
            }

            foreach (var (client, server) in tempIds)
            {
                mappings.Add(new JsonObject { ["client_object_id"] = client, ["object_id"] = server });
            }
        }

        return Json(new JsonObject { ["objects"] = results, ["id_mappings"] = mappings });
    }

    private void AssignIds(JsonObject obj, Dictionary<string, string> tempIds)
    {
        var id = (string)obj["id"]!;
        if (id.StartsWith('#') && !tempIds.ContainsKey(id))
        {
            tempIds[id] = $"SQ-{(string)obj["type"]!}-{Next()}";
        }

        foreach (var nested in obj["item_data"]?["variations"]?.AsArray() ?? new JsonArray())
        {
            AssignIds((JsonObject)nested!, tempIds);
        }
    }

    private JsonObject Store(JsonObject obj, Dictionary<string, string> tempIds, out string? conflict)
    {
        conflict = null;
        var id = Resolve((string)obj["id"]!, tempIds);
        obj["id"] = id;
        var type = (string)obj["type"]!;

        if (type == "ITEM")
        {
            if (_catalog.TryGetValue(id, out var existing) && (long?)obj["version"] != (long?)existing["version"])
            {
                conflict = $"Item {id} version mismatch";
                return obj;
            }

            obj["version"] = ((long?)existing?["version"] ?? 0) + 1;
            foreach (var variation in obj["item_data"]?["variations"]?.AsArray().Select(v => (JsonObject)v!) ?? Enumerable.Empty<JsonObject>())
            {
                var variationId = Resolve((string)variation["id"]!, tempIds);
                variation["id"] = variationId;
                variation["version"] = ((long?)variation["version"] ?? 0) + 1;
                var data = (JsonObject)variation["item_variation_data"]!;
                data["item_id"] = Resolve((string?)data["item_id"] ?? id, tempIds);
                _variationToItem[variationId] = id;
            }

            if (existing?["item_data"]?["image_ids"] is { } images && obj["item_data"] is JsonObject itemData && itemData["image_ids"] is null)
            {
                itemData["image_ids"] = images.DeepClone();
            }

            _catalog[id] = obj;
            return obj;
        }

        if (type == "ITEM_VARIATION")
        {
            var data = (JsonObject)obj["item_variation_data"]!;
            var itemId = Resolve((string)data["item_id"]!, tempIds);
            data["item_id"] = itemId;
            var item = _catalog[itemId];
            var variations = item["item_data"]!["variations"]!.AsArray();
            var index = variations.Select((v, i) => (v, i)).FirstOrDefault(p => (string?)p.v?["id"] == id).i;
            obj["version"] = ((long?)obj["version"] ?? 0) + 1;
            if (variations.Any(v => (string?)v?["id"] == id))
            {
                variations[index] = obj.DeepClone();
            }
            else
            {
                variations.Add(obj.DeepClone());
            }

            _variationToItem[id] = itemId;
            return obj;
        }

        _catalog[id] = obj;
        return obj;
    }

    private static string Resolve(string id, Dictionary<string, string> tempIds) => tempIds.TryGetValue(id, out var real) ? real : id;

    private static JsonObject? FindVariation(JsonObject item, string variationId) =>
        item["item_data"]?["variations"]?.AsArray().Select(v => (JsonObject)v!).FirstOrDefault(v => (string?)v["id"] == variationId);

    private HttpResponseMessage CreateImage(RecordedRequest r)
    {
        var request = JsonNode.Parse(r.MultipartText["request"])!.AsObject();
        var key = "images:" + (string)request["idempotency_key"]!;
        if (_idempotent.TryGetValue(key, out var cached))
        {
            return Raw(cached.Status, cached.Body);
        }

        var objectId = (string?)request["object_id"];
        if (objectId is null || !_catalog.TryGetValue(objectId, out var item))
        {
            return Error(HttpStatusCode.NotFound, "NOT_FOUND", "Object not found.");
        }

        var imageId = $"SQ-IMAGE-{Next()}";
        var imageData = (JsonObject)request["image"]!["image_data"]!.DeepClone();
        imageData["url"] = $"https://items-images-sandbox.example/{imageId}.{(r.MultipartContentTypes["image_file"] == "image/png" ? "png" : "jpg")}";
        var image = new JsonObject { ["type"] = "IMAGE", ["id"] = imageId, ["version"] = 1L, ["image_data"] = imageData };
        _catalog[imageId] = image;

        var itemData = (JsonObject)item["item_data"]!;
        var ids = itemData["image_ids"]?.AsArray().Select(n => (string)n!).ToList() ?? new List<string>();
        if ((bool?)request["is_primary"] == true)
        {
            ids.Insert(0, imageId);
        }
        else
        {
            ids.Add(imageId);
        }

        itemData["image_ids"] = new JsonArray(ids.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray());
        item["version"] = (long)item["version"]! + 1;

        var body = new JsonObject { ["image"] = image.DeepClone() }.ToJsonString();
        _idempotent[key] = (HttpStatusCode.OK, body);
        return Raw(HttpStatusCode.OK, body);
    }

    private HttpResponseMessage CreateOrder(RecordedRequest r, string merchantId)
    {
        var order = (JsonObject)r.Json!["order"]!.DeepClone();
        var locationId = (string?)order["location_id"];
        var expectedLocation = merchantId == MerchantId ? LocationId : OAuthLocationId;
        if (locationId != expectedLocation)
        {
            return Error(HttpStatusCode.BadRequest, "NOT_FOUND", "Location not found.");
        }

        long total = 0;
        foreach (var line in order["line_items"]!.AsArray().Select(l => (JsonObject)l!))
        {
            var catalogObjectId = (string?)line["catalog_object_id"];
            if (catalogObjectId is not null)
            {
                if (!_variationToItem.TryGetValue(catalogObjectId, out var itemId))
                {
                    return Error(HttpStatusCode.BadRequest, "NOT_FOUND", "Catalog object not found.");
                }

                var variation = FindVariation(_catalog[itemId], catalogObjectId)!;
                line["name"] = (string?)_catalog[itemId]["item_data"]!["name"];
                line["base_price_money"] = variation["item_variation_data"]!["price_money"]!.DeepClone();
            }

            var price = (long)line["base_price_money"]!["amount"]!;
            var quantity = long.Parse((string)line["quantity"]!, CultureInfo.InvariantCulture);
            line["total_money"] = new JsonObject { ["amount"] = price * quantity, ["currency"] = "USD" };
            total += price * quantity;
        }

        var id = $"SQ-ORDER-{Next()}";
        order["id"] = id;
        order["state"] = "OPEN";
        order["version"] = 1L;
        order["total_money"] = new JsonObject { ["amount"] = total, ["currency"] = "USD" };
        _orders[id] = order;
        return Json(new JsonObject { ["order"] = order.DeepClone() });
    }

    private HttpResponseMessage RetrieveOrder(string orderId) =>
        _orders.TryGetValue(orderId, out var order)
            ? Json(new JsonObject { ["order"] = order.DeepClone() })
            : Error(HttpStatusCode.NotFound, "NOT_FOUND", "Order not found.");

    private HttpResponseMessage CreateDefinition(RecordedRequest r)
    {
        var definition = (JsonObject)r.Json!["custom_attribute_definition"]!.DeepClone();
        var key = (string)definition["key"]!;
        if (_definitions.ContainsKey(key))
        {
            return Error(HttpStatusCode.Conflict, "CONFLICT", "A definition with this key already exists.");
        }

        definition["version"] = 1L;
        _definitions[key] = definition;
        return Json(new JsonObject { ["custom_attribute_definition"] = definition.DeepClone() });
    }

    private HttpResponseMessage RetrieveDefinition(string key) =>
        _definitions.TryGetValue(key, out var definition)
            ? Json(new JsonObject { ["custom_attribute_definition"] = definition.DeepClone() })
            : Error(HttpStatusCode.NotFound, "NOT_FOUND", "Definition not found.");

    private HttpResponseMessage UpsertOrderAttribute(RecordedRequest r, string orderId, string key)
    {
        if (!_definitions.ContainsKey(key) || !_orders.ContainsKey(orderId))
        {
            return Error(HttpStatusCode.NotFound, "NOT_FOUND", "Order or definition not found.");
        }

        var attribute = (JsonObject)r.Json!["custom_attribute"]!.DeepClone();
        if (attribute["value"] is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
        {
            return Error(HttpStatusCode.BadRequest, "INVALID_VALUE", "Value must be a string.");
        }

        // As observed in the sandbox: changing an existing value requires its current version.
        if (_orderAttributes.TryGetValue((orderId, key), out var existing))
        {
            if ((long?)attribute["version"] != (long?)existing["version"])
            {
                return Error(HttpStatusCode.BadRequest, "BAD_REQUEST", "Missing required parameter `version`");
            }
        }

        attribute["key"] = key;
        attribute["version"] = ((long?)existing?["version"] ?? 0) + 1;
        _orderAttributes[(orderId, key)] = attribute;
        return Json(new JsonObject { ["custom_attribute"] = attribute.DeepClone() });
    }

    private HttpResponseMessage RetrieveOrderAttribute(string orderId, string key) =>
        _orderAttributes.TryGetValue((orderId, key), out var attribute)
            ? Json(new JsonObject { ["custom_attribute"] = attribute.DeepClone() })
            : Error(HttpStatusCode.NotFound, "NOT_FOUND", "Custom attribute not found.");

    private HttpResponseMessage Idempotent(RecordedRequest r, Func<HttpResponseMessage> handler)
    {
        var key = (string?)r.Json?["idempotency_key"];
        if (key is null)
        {
            return handler();
        }

        var cacheKey = r.Path + "|" + key;
        if (_idempotent.TryGetValue(cacheKey, out var cached))
        {
            return Raw(cached.Status, cached.Body);
        }

        var response = handler();
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (response.IsSuccessStatusCode)
        {
            _idempotent[cacheKey] = (response.StatusCode, body);
        }

        return Raw(response.StatusCode, body);
    }

    private int Next() => Interlocked.Increment(ref _sequence);

    private static HttpResponseMessage Json(JsonObject body) => Raw(HttpStatusCode.OK, body.ToJsonString());

    private static HttpResponseMessage Raw(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Error(HttpStatusCode status, string code, string detail, string category = "INVALID_REQUEST_ERROR") =>
        Raw(status, new JsonObject
        {
            ["errors"] = new JsonArray(new JsonObject { ["category"] = category, ["code"] = code, ["detail"] = detail }),
        }.ToJsonString());

    private sealed class Fault
    {
        public Fault(string method, string pathPrefix, FaultKind kind, int remaining, HttpStatusCode status, string code)
        {
            Method = method;
            PathPrefix = pathPrefix;
            Kind = kind;
            Remaining = remaining;
            Status = status;
            Code = code;
        }

        public string Method { get; }
        public string PathPrefix { get; }
        public FaultKind Kind { get; }

        /// <summary>Exact path, or a prefix when the pattern ends with '*'.</summary>
        public bool Matches(string path) => PathPrefix.EndsWith('*')
            ? path.StartsWith(PathPrefix[..^1], StringComparison.Ordinal)
            : path == PathPrefix;
        public int Remaining { get; set; }
        public HttpStatusCode Status { get; }
        public string Code { get; }
    }
}

/// <summary>A request as the fake received it; the body is captured while it is still readable.</summary>
public sealed class RecordedRequest
{
    public string Method { get; private init; } = string.Empty;
    public string Path { get; private init; } = string.Empty;
    public string Host { get; private init; } = string.Empty;
    public string? Authorization { get; private init; }
    public string? Body { get; private init; }
    public JsonObject? Json { get; private init; }
    public Dictionary<string, string> MultipartText { get; } = new();
    public Dictionary<string, string?> MultipartContentTypes { get; } = new();
    public Dictionary<string, byte[]> MultipartBytes { get; } = new();

    public static async Task<RecordedRequest> CaptureAsync(HttpRequestMessage request)
    {
        string? body = null;
        JsonObject? json = null;
        var recorded = new RecordedRequest
        {
            Method = request.Method.Method,
            Path = request.RequestUri!.AbsolutePath,
            Host = request.RequestUri.Host,
            Authorization = request.Headers.Authorization?.ToString(),
        };

        if (request.Content is MultipartFormDataContent multipart)
        {
            foreach (var part in multipart)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty;
                var bytes = await part.ReadAsByteArrayAsync();
                recorded.MultipartBytes[name] = bytes;
                recorded.MultipartText[name] = Encoding.UTF8.GetString(bytes);
                recorded.MultipartContentTypes[name] = part.Headers.ContentType?.MediaType;
            }
        }
        else if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith('{'))
            {
                json = JsonNode.Parse(body)!.AsObject();
            }
        }

        return new RecordedRequest
        {
            Method = recorded.Method,
            Path = recorded.Path,
            Host = recorded.Host,
            Authorization = recorded.Authorization,
            Body = body,
            Json = json,
        }.WithParts(recorded);
    }

    private RecordedRequest WithParts(RecordedRequest source)
    {
        foreach (var (k, v) in source.MultipartText) MultipartText[k] = v;
        foreach (var (k, v) in source.MultipartContentTypes) MultipartContentTypes[k] = v;
        foreach (var (k, v) in source.MultipartBytes) MultipartBytes[k] = v;
        return this;
    }
}
