using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.PaymentsTests.Fakes;

public enum Fault
{
    /// <summary>PayPal processes the request, then the connection drops before the response arrives.</summary>
    DropAfterProcessing,
    /// <summary>PayPal never answers.</summary>
    Hang,
    /// <summary>The connection fails before PayPal sees the request.</summary>
    DropBeforeProcessing,
}

public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body, IReadOnlyDictionary<string, string> Headers)
{
    public JsonNode? Json => Body is null or "" ? null : JsonNode.Parse(Body);
    public string? Header(string name) => Headers.TryGetValue(name.ToLowerInvariant(), out var v) ? v : null;
}

/// <summary>
/// An in-memory stand-in for the PayPal REST API, behind the real SDK client (the HttpClient seam).
/// It keeps orders, authorizations, captures, refunds and vault tokens, honours PayPal-Request-Id
/// replays, and can inject connection faults. No network is used.
/// </summary>
public sealed class FakePayPal : HttpMessageHandler
{
    public const string DeclinedCard = "4000000000000002";

    private readonly object _gate = new();
    private int _sequence;
    private readonly Dictionary<string, JsonObject> _orders = new();
    private readonly Dictionary<string, JsonObject> _authorizations = new();
    private readonly Dictionary<string, string> _authorizationOrder = new();
    private readonly Dictionary<string, JsonObject> _captures = new();
    private readonly Dictionary<string, decimal> _refundedByCapture = new();
    private readonly Dictionary<string, JsonObject> _setupTokens = new();
    private readonly Dictionary<string, JsonObject> _paymentTokens = new();
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _replays = new();
    private readonly ConcurrentDictionary<string, Queue<Fault?>> _faults = new();

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>Create-order answers APPROVED without an authorization, so the client must call authorize.</summary>
    public bool AuthorizeInSecondStep { get; set; }
    public bool RequirePayerAction { get; set; }
    public bool ReauthorizeRefused { get; set; }
    /// <summary>Overrides the create_time PayPal reports for new authorizations.</summary>
    public DateTimeOffset? AuthorizationCreateTime { get; set; }
    public List<JsonObject> ReportingTransactions { get; } = new();
    public int ReportingPageSizeOverride { get; set; }

    public IEnumerable<RecordedRequest> Calls(string method, string pathPattern) =>
        Requests.Where(r => r.Method.Method == method && Regex.IsMatch(r.Path, "^" + pathPattern + "$"));

    /// <summary>The next <paramref name="times"/> matching calls (after <paramref name="passThrough"/> normal ones) fail with <paramref name="fault"/>.</summary>
    public void InjectFault(string method, string pathPattern, Fault fault, int times = 1, int passThrough = 0)
    {
        var queue = _faults.GetOrAdd($"{method} {pathPattern}", _ => new Queue<Fault?>());
        lock (queue)
        {
            for (var i = 0; i < passThrough; i++) queue.Enqueue(null);
            for (var i = 0; i < times; i++) queue.Enqueue(fault);
        }
    }

    public string Next(string prefix) => $"{prefix}{Interlocked.Increment(ref _sequence):D6}";

    public void SetAuthorizationStatus(string authorizationId, string status)
    {
        lock (_gate) _authorizations[authorizationId]["status"] = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
        Requests.Enqueue(new RecordedRequest(request.Method, path, request.RequestUri.Query, body, headers));

        var fault = TakeFault(request.Method.Method, path);
        if (fault == Fault.Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        if (fault == Fault.DropBeforeProcessing)
        {
            throw new HttpRequestException("connection reset (injected)");
        }

        (HttpStatusCode Status, string Body) result;
        lock (_gate)
        {
            var requestId = request.Headers.TryGetValues("PayPal-Request-Id", out var ids) ? ids.FirstOrDefault() : null;
            var replayKey = requestId is null ? null : $"{request.Method} {path} {requestId}";
            if (replayKey is not null && _replays.TryGetValue(replayKey, out var cached))
            {
                result = cached;
            }
            else
            {
                result = Route(request.Method.Method, path, request.RequestUri.Query, body);
                if (replayKey is not null && (int)result.Status < 300)
                    _replays[replayKey] = result;
            }
        }

        if (fault == Fault.DropAfterProcessing)
        {
            throw new HttpRequestException("connection reset after the request was processed (injected)");
        }

        return new HttpResponseMessage(result.Status)
        {
            Content = result.Status == HttpStatusCode.NoContent ? null : new StringContent(result.Body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }

    private Fault? TakeFault(string method, string path)
    {
        foreach (var (key, queue) in _faults)
        {
            var space = key.IndexOf(' ');
            if (key[..space] != method || !Regex.IsMatch(path, "^" + key[(space + 1)..] + "$"))
                continue;
            lock (queue)
            {
                if (queue.Count > 0) return queue.Dequeue();
            }
        }
        return null;
    }

    private (HttpStatusCode, string) Route(string method, string path, string query, string? body)
    {
        Match m;
        if (method == "POST" && path == "/v1/oauth2/token")
            return Ok(new JsonObject { ["access_token"] = "fake-access-token", ["token_type"] = "Bearer", ["expires_in"] = 32400 });
        if (method == "POST" && path == "/v2/checkout/orders")
            return CreateOrder(JsonNode.Parse(body!)!.AsObject());
        if (method == "POST" && (m = Regex.Match(path, "^/v2/checkout/orders/([^/]+)/authorize$")).Success)
            return AuthorizeOrder(m.Groups[1].Value);
        if (method == "GET" && (m = Regex.Match(path, "^/v2/checkout/orders/([^/]+)$")).Success)
            return _orders.TryGetValue(m.Groups[1].Value, out var order) ? Ok(order) : NotFound();
        if (method == "GET" && (m = Regex.Match(path, "^/v2/payments/authorizations/([^/]+)$")).Success)
            return _authorizations.TryGetValue(m.Groups[1].Value, out var auth) ? Ok(auth) : NotFound();
        if (method == "POST" && (m = Regex.Match(path, "^/v2/payments/authorizations/([^/]+)/capture$")).Success)
            return Capture(m.Groups[1].Value, JsonNode.Parse(body!)!.AsObject());
        if (method == "POST" && (m = Regex.Match(path, "^/v2/payments/authorizations/([^/]+)/void$")).Success)
            return Void(m.Groups[1].Value);
        if (method == "POST" && (m = Regex.Match(path, "^/v2/payments/authorizations/([^/]+)/reauthorize$")).Success)
            return Reauthorize(m.Groups[1].Value);
        if (method == "GET" && (m = Regex.Match(path, "^/v2/payments/captures/([^/]+)$")).Success)
            return _captures.TryGetValue(m.Groups[1].Value, out var capture) ? Ok(capture) : NotFound();
        if (method == "POST" && (m = Regex.Match(path, "^/v2/payments/captures/([^/]+)/refund$")).Success)
            return Refund(m.Groups[1].Value, string.IsNullOrEmpty(body) ? new JsonObject() : JsonNode.Parse(body)!.AsObject());
        if (method == "POST" && path == "/v3/vault/setup-tokens")
            return CreateSetupToken(JsonNode.Parse(body!)!.AsObject());
        if (method == "POST" && path == "/v3/vault/payment-tokens")
            return CreatePaymentToken(JsonNode.Parse(body!)!.AsObject());
        if (method == "DELETE" && (m = Regex.Match(path, "^/v3/vault/payment-tokens/([^/]+)$")).Success)
            return _paymentTokens.Remove(m.Groups[1].Value) ? (HttpStatusCode.NoContent, "") : NotFound();
        if (method == "GET" && path == "/v1/reporting/transactions")
            return SearchTransactions(query);
        return NotFound();
    }

    private (HttpStatusCode, string) CreateOrder(JsonObject body)
    {
        var unit = body["purchase_units"]![0]!.AsObject();
        var amount = unit["amount"]!.AsObject();
        var card = body["payment_source"]?["card"]?.AsObject();
        string? last4 = null;
        var brand = "VISA";
        if (card?["vault_id"]?.GetValue<string>() is { } vaultId)
        {
            if (!_paymentTokens.TryGetValue(vaultId, out var token))
                return Error(HttpStatusCode.UnprocessableEntity, "UNPROCESSABLE_ENTITY", "The requested action could not be performed.", "INVALID_VAULT_ID");
            last4 = token["payment_source"]!["card"]!["last_digits"]!.GetValue<string>();
        }
        else if (card?["number"]?.GetValue<string>() is { } number)
        {
            if (number == DeclinedCard)
                return Error(HttpStatusCode.UnprocessableEntity, "UNPROCESSABLE_ENTITY", "The requested action could not be performed, semantically incorrect, or failed business validation.", "INSTRUMENT_DECLINED");
            last4 = number[^4..];
        }

        var orderId = Next("ORDER");
        var order = new JsonObject
        {
            ["id"] = orderId,
            ["intent"] = body["intent"]!.GetValue<string>(),
            ["payment_source"] = new JsonObject { ["card"] = new JsonObject { ["last_digits"] = last4, ["brand"] = brand, ["type"] = "CREDIT" } },
            ["purchase_units"] = new JsonArray(new JsonObject
            {
                ["reference_id"] = unit["reference_id"]?.GetValue<string>(),
                ["amount"] = amount.DeepClone(),
                ["invoice_id"] = unit["invoice_id"]?.GetValue<string>(),
                ["custom_id"] = unit["custom_id"]?.GetValue<string>(),
                ["payments"] = new JsonObject(),
            }),
        };
        _orders[orderId] = order;

        if (RequirePayerAction)
        {
            order["status"] = "PAYER_ACTION_REQUIRED";
            return Created(order);
        }
        if (AuthorizeInSecondStep)
        {
            order["status"] = "APPROVED";
            return Created(order);
        }

        AddAuthorization(orderId, amount);
        order["status"] = "COMPLETED";
        return Created(order);
    }

    private (HttpStatusCode, string) AuthorizeOrder(string orderId)
    {
        if (!_orders.TryGetValue(orderId, out var order))
            return NotFound();
        if (order["status"]!.GetValue<string>() != "APPROVED")
            return Error(HttpStatusCode.UnprocessableEntity, "UNPROCESSABLE_ENTITY", "Order already authorized.", "ORDER_ALREADY_AUTHORIZED");
        AddAuthorization(orderId, order["purchase_units"]![0]!["amount"]!.AsObject());
        order["status"] = "COMPLETED";
        return Created(order);
    }

    private JsonObject AddAuthorization(string orderId, JsonObject amount)
    {
        var created = AuthorizationCreateTime ?? DateTimeOffset.UtcNow;
        var auth = new JsonObject
        {
            ["id"] = Next("AUTH"),
            ["status"] = "CREATED",
            ["amount"] = new JsonObject { ["currency_code"] = amount["currency_code"]!.GetValue<string>(), ["value"] = amount["value"]!.GetValue<string>() },
            ["create_time"] = created.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["expiration_time"] = created.AddDays(29).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        _authorizations[auth["id"]!.GetValue<string>()] = auth;
        _authorizationOrder[auth["id"]!.GetValue<string>()] = orderId;
        Payments(orderId)["authorizations"] = Append(Payments(orderId)["authorizations"], auth);
        return auth;
    }

    private (HttpStatusCode, string) Capture(string authorizationId, JsonObject body)
    {
        if (!_authorizations.TryGetValue(authorizationId, out var auth))
            return NotFound();
        var status = auth["status"]!.GetValue<string>();
        if (status == "CAPTURED")
            return Error(HttpStatusCode.UnprocessableEntity, "UNPROCESSABLE_ENTITY", "Authorization has been previously captured and hence cannot be captured again.", "AUTHORIZATION_ALREADY_CAPTURED");
        if (status is "VOIDED" or "EXPIRED")
            return Error(HttpStatusCode.UnprocessableEntity, "UNPROCESSABLE_ENTITY", "The authorization cannot be captured.", status == "VOIDED" ? "AUTHORIZATION_VOIDED" : "AUTHORIZATION_EXPIRED");

        var value = decimal.Parse(body["amount"]!["value"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        var currency = body["amount"]!["currency_code"]!.GetValue<string>();
        var fee = Math.Round(value * 0.0349m + 0.49m, 2);
        var capture = new JsonObject
        {
            ["id"] = Next("CAPTURE"),
            ["status"] = "COMPLETED",
            ["amount"] = Money(currency, value),
            ["final_capture"] = body["final_capture"]?.GetValue<bool>() ?? false,
            ["seller_receivable_breakdown"] = new JsonObject
            {
                ["gross_amount"] = Money(currency, value),
                ["paypal_fee"] = Money(currency, fee),
                ["net_amount"] = Money(currency, value - fee),
            },
            ["create_time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        _captures[capture["id"]!.GetValue<string>()] = capture;
        auth["status"] = "CAPTURED";
        var orderId = _authorizationOrder[authorizationId];
        Payments(orderId)["captures"] = Append(Payments(orderId)["captures"], capture);
        return Created(capture);
    }

    private (HttpStatusCode, string) Void(string authorizationId)
    {
        if (!_authorizations.TryGetValue(authorizationId, out var auth))
            return NotFound();
        if (auth["status"]!.GetValue<string>() == "CAPTURED")
            return Error(HttpStatusCode.UnprocessableEntity, "UNPROCESSABLE_ENTITY", "Authorization has been previously captured.", "PREVIOUSLY_CAPTURED");
        auth["status"] = "VOIDED";
        return Ok(auth);
    }

    private (HttpStatusCode, string) Reauthorize(string authorizationId)
    {
        if (!_authorizations.TryGetValue(authorizationId, out var auth))
            return NotFound();
        if (ReauthorizeRefused)
            return Error(HttpStatusCode.UnprocessableEntity, "UNPROCESSABLE_ENTITY", "Reauthorization is not supported for this payment.", "REAUTHORIZATION_NOT_SUPPORTED");
        var saved = AuthorizationCreateTime;
        AuthorizationCreateTime = null;
        var renewed = AddAuthorization(_authorizationOrder[authorizationId], auth["amount"]!.AsObject());
        AuthorizationCreateTime = saved;
        auth["status"] = "VOIDED"; // superseded by the renewal
        return Created(renewed);
    }

    private (HttpStatusCode, string) Refund(string captureId, JsonObject body)
    {
        if (!_captures.TryGetValue(captureId, out var capture))
            return NotFound();
        var captured = decimal.Parse(capture["amount"]!["value"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        var currency = capture["amount"]!["currency_code"]!.GetValue<string>();
        _refundedByCapture.TryGetValue(captureId, out var refunded);
        var value = body["amount"] is { } a ? decimal.Parse(a["value"]!.GetValue<string>(), CultureInfo.InvariantCulture) : captured - refunded;
        if (refunded + value > captured)
            return Error(HttpStatusCode.UnprocessableEntity, "UNPROCESSABLE_ENTITY", "The refund amount must be less than or equal to the capture amount that has not yet been refunded.", "REFUND_AMOUNT_EXCEEDED");
        _refundedByCapture[captureId] = refunded + value;
        capture["status"] = refunded + value == captured ? "REFUNDED" : "PARTIALLY_REFUNDED";
        var refund = new JsonObject
        {
            ["id"] = Next("REFUND"),
            ["status"] = "COMPLETED",
            ["amount"] = Money(currency, value),
            ["custom_id"] = body["custom_id"]?.GetValue<string>(),
        };
        return Created(refund);
    }

    private (HttpStatusCode, string) CreateSetupToken(JsonObject body)
    {
        var card = body["payment_source"]!["card"]!.AsObject();
        var number = card["number"]!.GetValue<string>();
        var customerId = body["customer"]?["id"]?.GetValue<string>() ?? Next("CUST");
        var token = new JsonObject
        {
            ["id"] = Next("SETUP"),
            ["status"] = "APPROVED",
            ["customer"] = new JsonObject { ["id"] = customerId },
            ["payment_source"] = new JsonObject
            {
                ["card"] = new JsonObject { ["last_digits"] = number[^4..], ["brand"] = "VISA", ["expiry"] = card["expiry"]!.GetValue<string>() },
            },
        };
        _setupTokens[token["id"]!.GetValue<string>()] = token;
        return Created(token);
    }

    private (HttpStatusCode, string) CreatePaymentToken(JsonObject body)
    {
        var setupId = body["payment_source"]!["token"]!["id"]!.GetValue<string>();
        if (!_setupTokens.TryGetValue(setupId, out var setup))
            return Error(HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND", "Setup token not found.", "INVALID_RESOURCE_ID");
        var token = new JsonObject
        {
            ["id"] = Next("pt"),
            ["customer"] = setup["customer"]!.DeepClone(),
            ["payment_source"] = setup["payment_source"]!.DeepClone(),
        };
        _paymentTokens[token["id"]!.GetValue<string>()] = token;
        return Created(token);
    }

    private (HttpStatusCode, string) SearchTransactions(string query)
    {
        var parameters = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p.Length > 1 ? p[1] : ""));
        var start = DateTimeOffset.Parse(parameters["start_date"], CultureInfo.InvariantCulture);
        var end = DateTimeOffset.Parse(parameters["end_date"], CultureInfo.InvariantCulture);
        if (end - start > TimeSpan.FromDays(31))
            return Error(HttpStatusCode.BadRequest, "INVALID_REQUEST", "Date range is greater than 31 days.", "INVALID_DATE_RANGE");
        var page = int.Parse(parameters.GetValueOrDefault("page", "1"), CultureInfo.InvariantCulture);
        var pageSize = ReportingPageSizeOverride > 0 ? ReportingPageSizeOverride : int.Parse(parameters.GetValueOrDefault("page_size", "100"), CultureInfo.InvariantCulture);

        var inRange = ReportingTransactions
            .Where(t =>
            {
                var at = DateTimeOffset.Parse(t["transaction_initiation_date"]!.GetValue<string>(), CultureInfo.InvariantCulture);
                return at >= start && at < end;
            })
            .ToList();
        var totalPages = (int)Math.Ceiling(inRange.Count / (double)pageSize);
        var details = new JsonArray(inRange.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => (JsonNode)new JsonObject { ["transaction_info"] = t.DeepClone() }).ToArray());
        return Ok(new JsonObject
        {
            ["transaction_details"] = details,
            ["page"] = page,
            ["total_items"] = inRange.Count,
            ["total_pages"] = totalPages,
        });
    }

    public static JsonObject ReportingTransaction(string id, DateTimeOffset at, decimal amount, string? invoiceId = null, string eventCode = "T0005") => new()
    {
        ["transaction_id"] = id,
        ["transaction_event_code"] = eventCode,
        ["transaction_initiation_date"] = at.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        ["transaction_amount"] = Money("USD", amount),
        ["transaction_status"] = "S",
        ["invoice_id"] = invoiceId,
    };

    private JsonObject Payments(string orderId) => _orders[orderId]["purchase_units"]![0]!["payments"]!.AsObject();

    private static JsonArray Append(JsonNode? array, JsonObject item)
    {
        var result = array is JsonArray existing ? new JsonArray(existing.Select(n => n!.DeepClone()).ToArray()) : new JsonArray();
        result.Add(item.DeepClone());
        return result;
    }

    private static JsonObject Money(string currency, decimal value) => new()
    {
        ["currency_code"] = currency,
        ["value"] = value.ToString("F2", CultureInfo.InvariantCulture),
    };

    private static (HttpStatusCode, string) Ok(JsonNode body) => (HttpStatusCode.OK, body.ToJsonString());
    private static (HttpStatusCode, string) Created(JsonNode body) => (HttpStatusCode.Created, body.ToJsonString());
    private static (HttpStatusCode, string) NotFound() =>
        Error(HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND", "The specified resource does not exist.", "INVALID_RESOURCE_ID");

    private static (HttpStatusCode, string) Error(HttpStatusCode status, string name, string message, string issue) =>
        (status, new JsonObject
        {
            ["name"] = name,
            ["message"] = message,
            ["debug_id"] = "fake-debug-" + issue.ToLowerInvariant(),
            ["details"] = new JsonArray(new JsonObject { ["issue"] = issue, ["description"] = $"{issue} (fake)" }),
        }.ToJsonString());
}
