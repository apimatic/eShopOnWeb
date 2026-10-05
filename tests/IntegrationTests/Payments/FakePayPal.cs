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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PaymentTests;

/// <summary>
/// An in-process stand-in for the PayPal REST API, plugged in as the SDK's HttpMessageHandler. It keeps just
/// enough state (orders, authorizations, captures, refunds, vault tokens, a transaction report) to drive the
/// payment flows offline, honours PayPal-Request-Id replays, and lets a test inject failures per route.
/// </summary>
public sealed class FakePayPal : HttpMessageHandler
{
    public const string DeclinedCardNumber = "4000000000000002";
    public const string ChallengeCardNumber = "4000000000003220";

    /// <summary>Marks a test's own re-delivery of a request, so it is processed but not recorded as a client call.</summary>
    public const string ForwardedHeader = "X-Fake-Forwarded";

    private readonly object _gate = new();
    private int _sequence;
    private readonly Dictionary<string, JsonObject> _orders = new();
    private readonly Dictionary<string, JsonObject> _authorizations = new();
    private readonly Dictionary<string, JsonObject> _captures = new();
    private readonly Dictionary<string, JsonObject> _tokens = new();
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _replays = new();

    /// <summary>Every request, in order: method, path+query, PayPal-Request-Id, body (captured while readable).</summary>
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>Return a response (or throw) to override the fake for a request; return null to fall through.</summary>
    public Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage?>>? Intercept { get; set; }

    /// <summary>Transactions served by the reporting endpoint.</summary>
    public List<JsonObject> ReportTransactions { get; } = new();

    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public int Count(string method, string pathPattern) =>
        Requests.Count(r => r.Method == method && Regex.IsMatch(r.Path, pathPattern));

    public string? AuthorizationStatus(string id) { lock (_gate) return _authorizations.TryGetValue(id, out var a) ? (string?)a["status"] : null; }
    public bool TokenExists(string id) { lock (_gate) return _tokens.ContainsKey(id); }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var requestId = request.Headers.TryGetValues("PayPal-Request-Id", out var ids) ? ids.FirstOrDefault() : null;
        if (!request.Headers.Contains(ForwardedHeader))
            Requests.Enqueue(new RecordedRequest(request.Method.Method, request.RequestUri!.PathAndQuery, requestId, body,
            request.Headers.TryGetValues("Prefer", out var prefer) ? prefer.FirstOrDefault() : null,
            request.RequestUri!.GetLeftPart(UriPartial.Authority)));

        if (Intercept is not null)
        {
            var overridden = await Intercept(request, body, cancellationToken);
            if (overridden is not null) return overridden;
        }

        var path = request.RequestUri!.AbsolutePath;
        var replayKey = requestId is null ? null : $"{request.Method} {path} {requestId}";
        lock (_gate)
        {
            if (replayKey is not null && _replays.TryGetValue(replayKey, out var replay))
                return Json(replay.Status, replay.Body);

            var (status, json) = Route(request.Method.Method, path, request.RequestUri!.Query, body);
            if (replayKey is not null && (int)status < 500) _replays[replayKey] = (status, json);
            return Json(status, json);
        }
    }

    private (HttpStatusCode, string) Route(string method, string path, string query, string? body)
    {
        Match m;
        if (method == "POST" && path == "/v1/oauth2/token")
            return Ok("""{"access_token":"fake-access-token","token_type":"Bearer","expires_in":32400}""");

        if (method == "POST" && path == "/v2/checkout/orders")
        {
            var req = JsonNode.Parse(body!)!.AsObject();
            var id = Next("ORDER");
            var unit = req["purchase_units"]![0]!.AsObject();
            var order = new JsonObject
            {
                ["id"] = id,
                ["status"] = "CREATED",
                ["intent"] = req["intent"]!.GetValue<string>(),
                ["purchase_units"] = new JsonArray(new JsonObject
                {
                    ["reference_id"] = "default",
                    ["amount"] = unit["amount"]!.DeepClone(),
                    ["custom_id"] = unit["custom_id"]?.DeepClone(),
                    ["invoice_id"] = unit["invoice_id"]?.DeepClone()
                })
            };
            _orders[id] = order;
            return (HttpStatusCode.Created, order.ToJsonString());
        }

        if ((m = Regex.Match(path, "^/v2/checkout/orders/([A-Z0-9]+)/authorize$")).Success && method == "POST")
        {
            if (!_orders.TryGetValue(m.Groups[1].Value, out var order)) return NotFound();
            if ((string?)order["status"] == "COMPLETED") return Unprocessable("ORDER_ALREADY_AUTHORIZED", "Order already authorized.");
            var card = JsonNode.Parse(body ?? "{}")?["payment_source"]?["card"];
            string brand = "VISA", last4, expiry;
            if (card?["vault_id"] is { } vaultId)
            {
                if (!_tokens.TryGetValue(vaultId.GetValue<string>(), out var token)) return Unprocessable("INVALID_RESOURCE_ID", "Vault token not found.");
                last4 = (string)token["payment_source"]!["card"]!["last_digits"]!;
                expiry = (string)token["payment_source"]!["card"]!["expiry"]!;
            }
            else
            {
                var number = (string?)card?["number"] ?? string.Empty;
                if (number == DeclinedCardNumber) return Unprocessable("INSTRUMENT_DECLINED", "The instrument presented was declined.");
                if (number == ChallengeCardNumber)
                {
                    order["status"] = "PAYER_ACTION_REQUIRED";
                    return (HttpStatusCode.OK, order.ToJsonString());
                }
                last4 = number.Length >= 4 ? number[^4..] : number;
                expiry = (string?)card?["expiry"] ?? "2030-12";
            }

            var amount = order["purchase_units"]![0]!["amount"]!;
            var auth = new JsonObject
            {
                ["id"] = Next("AUTH"),
                ["status"] = "CREATED",
                ["amount"] = new JsonObject { ["currency_code"] = amount["currency_code"]!.DeepClone(), ["value"] = amount["value"]!.DeepClone() },
                ["create_time"] = Now.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ["expiration_time"] = Now.AddDays(29).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ["order_id"] = order["id"]!.DeepClone()
            };
            _authorizations[(string)auth["id"]!] = auth;
            order["status"] = "COMPLETED";
            order["payment_source"] = new JsonObject { ["card"] = new JsonObject { ["brand"] = brand, ["last_digits"] = last4, ["expiry"] = expiry } };
            order["purchase_units"]![0]!["payments"] = new JsonObject { ["authorizations"] = new JsonArray(Clean(auth)) };
            return (HttpStatusCode.Created, order.ToJsonString());
        }

        if ((m = Regex.Match(path, "^/v2/checkout/orders/([A-Z0-9]+)$")).Success && method == "GET")
            return _orders.TryGetValue(m.Groups[1].Value, out var o) ? Ok(o.ToJsonString()) : NotFound();

        if ((m = Regex.Match(path, "^/v2/payments/authorizations/([A-Z0-9]+)$")).Success && method == "GET")
            return _authorizations.TryGetValue(m.Groups[1].Value, out var a) ? Ok(Clean(a).ToJsonString()) : NotFound();

        if ((m = Regex.Match(path, "^/v2/payments/authorizations/([A-Z0-9]+)/reauthorize$")).Success && method == "POST")
        {
            if (!_authorizations.TryGetValue(m.Groups[1].Value, out var old)) return NotFound();
            if ((string?)old["status"] != "CREATED") return Unprocessable("AUTHORIZATION_ALREADY_CAPTURED", "Authorization cannot be reauthorized.");
            var renewed = (JsonObject)old.DeepClone();
            renewed["id"] = Next("AUTH");
            renewed["create_time"] = Now.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            _authorizations[(string)renewed["id"]!] = renewed;
            return (HttpStatusCode.Created, Clean(renewed).ToJsonString());
        }

        if ((m = Regex.Match(path, "^/v2/payments/authorizations/([A-Z0-9]+)/capture$")).Success && method == "POST")
        {
            if (!_authorizations.TryGetValue(m.Groups[1].Value, out var auth)) return NotFound();
            if ((string?)auth["status"] != "CREATED") return Unprocessable("AUTHORIZATION_VOIDED", $"Authorization is {auth["status"]}.");
            var amount = JsonNode.Parse(body!)!["amount"] ?? auth["amount"]!;
            var gross = decimal.Parse((string)amount["value"]!, CultureInfo.InvariantCulture);
            var fee = Math.Round(gross * 0.0349m + 0.49m, 2);
            var currency = (string)amount["currency_code"]!;
            var capture = new JsonObject
            {
                ["id"] = Next("CAPTURE"),
                ["status"] = "COMPLETED",
                ["amount"] = Money(gross, currency),
                ["final_capture"] = true,
                ["seller_receivable_breakdown"] = new JsonObject
                {
                    ["gross_amount"] = Money(gross, currency),
                    ["paypal_fee"] = Money(fee, currency),
                    ["net_amount"] = Money(gross - fee, currency)
                },
                ["refunded"] = "0.00"
            };
            _captures[(string)capture["id"]!] = capture;
            auth["status"] = "CAPTURED";
            return (HttpStatusCode.Created, Clean(capture).ToJsonString());
        }

        if ((m = Regex.Match(path, "^/v2/payments/authorizations/([A-Z0-9]+)/void$")).Success && method == "POST")
        {
            if (!_authorizations.TryGetValue(m.Groups[1].Value, out var auth)) return NotFound();
            if ((string?)auth["status"] != "CREATED") return Unprocessable("PREVIOUSLY_VOIDED", $"Authorization is {auth["status"]}.");
            auth["status"] = "VOIDED";
            return Ok(Clean(auth).ToJsonString());
        }

        if ((m = Regex.Match(path, "^/v2/payments/captures/([A-Z0-9]+)/refund$")).Success && method == "POST")
        {
            if (!_captures.TryGetValue(m.Groups[1].Value, out var capture)) return NotFound();
            var captured = decimal.Parse((string)capture["amount"]!["value"]!, CultureInfo.InvariantCulture);
            var refunded = decimal.Parse((string)capture["refunded"]!, CultureInfo.InvariantCulture);
            var requested = JsonNode.Parse(body ?? "{}")?["amount"];
            var amount = requested is null ? captured - refunded : decimal.Parse((string)requested["value"]!, CultureInfo.InvariantCulture);
            if (refunded + amount > captured) return Unprocessable("REFUND_AMOUNT_EXCEEDED", "The refund amount must be less than or equal to the capture amount that has not yet been refunded.");
            capture["refunded"] = (refunded + amount).ToString("0.00", CultureInfo.InvariantCulture);
            var currency = (string)capture["amount"]!["currency_code"]!;
            return (HttpStatusCode.Created, new JsonObject
            {
                ["id"] = Next("REFUND"),
                ["status"] = "COMPLETED",
                ["amount"] = Money(amount, currency)
            }.ToJsonString());
        }

        if (path == "/v3/vault/payment-tokens" && method == "POST")
        {
            var req = JsonNode.Parse(body!)!;
            var card = req["payment_source"]!["card"]!;
            var number = (string)card["number"]!;
            if (number == DeclinedCardNumber) return Unprocessable("CARD_VERIFICATION_FAILED", "The card could not be verified.");
            var customerId = (string?)req["customer"]?["id"] ?? Next("CUST");
            var token = new JsonObject
            {
                ["id"] = Next("TOKEN"),
                ["customer"] = new JsonObject { ["id"] = customerId },
                ["payment_source"] = new JsonObject
                {
                    ["card"] = new JsonObject
                    {
                        ["name"] = card["name"]?.DeepClone(),
                        ["brand"] = "VISA",
                        ["last_digits"] = number[^4..],
                        ["expiry"] = card["expiry"]!.DeepClone()
                    }
                }
            };
            _tokens[(string)token["id"]!] = token;
            return (HttpStatusCode.Created, token.ToJsonString());
        }

        if ((m = Regex.Match(path, "^/v3/vault/payment-tokens/([A-Z0-9]+)$")).Success)
        {
            var id = m.Groups[1].Value;
            if (method == "DELETE")
                return _tokens.Remove(id) ? (HttpStatusCode.NoContent, string.Empty) : NotFound();
            if (method == "GET")
                return _tokens.TryGetValue(id, out var t) ? Ok(t.ToJsonString()) : NotFound();
        }

        if (path == "/v3/vault/payment-tokens" && method == "GET")
        {
            var customer = Query(query, "customer_id");
            var tokens = _tokens.Values.Where(t => (string?)t["customer"]?["id"] == customer).Select(t => t.DeepClone()).ToArray();
            return Ok(new JsonObject { ["payment_tokens"] = new JsonArray(tokens), ["total_pages"] = 1, ["total_items"] = tokens.Length }.ToJsonString());
        }

        if (path == "/v1/reporting/transactions" && method == "GET")
        {
            var page = int.Parse(Query(query, "page") ?? "1", CultureInfo.InvariantCulture);
            var size = int.Parse(Query(query, "page_size") ?? "100", CultureInfo.InvariantCulture);
            var start = DateTimeOffset.Parse(Query(query, "start_date")!, CultureInfo.InvariantCulture);
            var end = DateTimeOffset.Parse(Query(query, "end_date")!, CultureInfo.InvariantCulture);
            if (end - start > TimeSpan.FromDays(31)) return (HttpStatusCode.BadRequest, """{"name":"INVALID_REQUEST","message":"Date range is greater than 31 days","debug_id":"fake-debug"}""");
            var inRange = ReportTransactions.Where(t =>
            {
                var at = DateTimeOffset.Parse((string)t["transaction_initiation_date"]!, CultureInfo.InvariantCulture);
                return at >= start && at <= end;
            }).ToList();
            var totalPages = Math.Max(1, (int)Math.Ceiling(inRange.Count / (double)size));
            var items = inRange.Skip((page - 1) * size).Take(size).Select(t => (JsonNode)new JsonObject { ["transaction_info"] = t.DeepClone() }).ToArray();
            return Ok(new JsonObject
            {
                ["transaction_details"] = new JsonArray(items),
                ["page"] = page,
                ["total_items"] = inRange.Count,
                ["total_pages"] = totalPages
            }.ToJsonString());
        }

        return NotFound();
    }

    public static JsonObject Transaction(string id, DateTimeOffset at, decimal amount, string eventCode = "T0006", string? invoiceId = null) => new()
    {
        ["transaction_id"] = id,
        ["transaction_event_code"] = eventCode,
        ["transaction_initiation_date"] = at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        ["transaction_amount"] = Money(amount, "USD"),
        ["transaction_status"] = "S",
        ["invoice_id"] = invoiceId
    };

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => Json(status, json);

    private static JsonObject Money(decimal value, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = value.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static JsonObject Clean(JsonObject o)
    {
        var copy = (JsonObject)o.DeepClone();
        copy.Remove("order_id");
        copy.Remove("refunded");
        return copy;
    }

    private string Next(string prefix) => $"{prefix}{Interlocked.Increment(ref _sequence):D6}";
    private static (HttpStatusCode, string) Ok(string json) => (HttpStatusCode.OK, json);
    private static (HttpStatusCode, string) NotFound() =>
        (HttpStatusCode.NotFound, """{"name":"RESOURCE_NOT_FOUND","message":"The specified resource does not exist.","debug_id":"fake-debug-404"}""");
    private static (HttpStatusCode, string) Unprocessable(string issue, string description) =>
        (HttpStatusCode.UnprocessableEntity, new JsonObject
        {
            ["name"] = "UNPROCESSABLE_ENTITY",
            ["message"] = "The requested action could not be performed, semantically incorrect, or failed business validation.",
            ["debug_id"] = "fake-debug-422",
            ["details"] = new JsonArray(new JsonObject { ["issue"] = issue, ["description"] = description })
        }.ToJsonString());

    private static string? Query(string query, string name) =>
        query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p[0] == name)
            .Select(p => Uri.UnescapeDataString(p.Length > 1 ? p[1] : string.Empty))
            .FirstOrDefault();

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = status == HttpStatusCode.NoContent ? null : new StringContent(json, Encoding.UTF8, "application/json")
    };
}

public record RecordedRequest(string Method, string Path, string? PayPalRequestId, string? Body, string? Prefer, string Authority);
