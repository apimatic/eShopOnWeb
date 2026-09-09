using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// HTTP implementation of <see cref="IPayPalClient"/> over the PayPal REST API. Owns OAuth token
/// acquisition/caching, idempotency headers, JSON shapes and error translation. Raw card details are
/// forwarded straight to PayPal and never persisted or logged here.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const int MaxPageSize = 500;
    private const int MaxWindowDays = 30; // PayPal caps transaction search at a 31-day range per call

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;

    // Access token is cached process-wide (one merchant, fixed credentials).
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static string? _cachedToken;
    private static DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient http, IOptions<PayPalSettings> settings, IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => _settings.Currency;

    // ----------------------------------------------------------------- authorize (create + hold)

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string reference, decimal amount,
        CardDetails? card, string? vaultId, string idempotencyKey, CancellationToken ct = default)
    {
        object cardSource = vaultId is not null
            ? new { vault_id = vaultId }
            : BuildCardPayload(card!);

        var body = new
        {
            intent = "AUTHORIZE",
            payment_source = new { card = cardSource },
            purchase_units = new[]
            {
                new
                {
                    invoice_id = reference,
                    custom_id = reference,
                    amount = Money(amount)
                }
            }
        };

        using var created = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, ct,
            idempotencyKey: idempotencyKey, preferRepresentation: true);
        var root = created.RootElement;

        var payPalOrderId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This integration is browser-free and cannot complete such a challenge.");
        }

        var authorization = FindFirstAuthorization(root);
        if (authorization is null)
        {
            // No authorization inline (redirect-style CREATED/APPROVED order) → authorize explicitly.
            using var authorized = await SendAsync(HttpMethod.Post,
                $"v2/checkout/orders/{payPalOrderId}/authorize", new { }, ct,
                idempotencyKey: $"{idempotencyKey}:authorize", preferRepresentation: true);
            authorization = FindFirstAuthorization(authorized.RootElement)
                ?? throw new PayPalApiException(
                    "PayPal did not return an authorization for the order.", 502, "NO_AUTHORIZATION",
                    Array.Empty<string>(), null, authorized.RootElement.GetRawText());
        }

        var auth = authorization.Value;
        return new PayPalAuthorizationResult(
            payPalOrderId,
            auth.GetProperty("id").GetString()!,
            auth.TryGetProperty("status", out var astat) ? astat.GetString() ?? "CREATED" : "CREATED",
            ReadDate(auth, "expiration_time"));
    }

    // ----------------------------------------------------------------- capture (take the money)

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = new { final_capture = true };
        using var doc = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture", body, ct,
            idempotencyKey: idempotencyKey, preferRepresentation: true);
        var root = doc.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "COMPLETED" : "COMPLETED";
        var capturedAmount = ReadMoney(root, "amount") ?? amount;

        decimal fee = 0m, net = capturedAmount;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = ReadMoney(breakdown, "paypal_fee") ?? 0m;
            net = ReadMoney(breakdown, "net_amount") ?? (capturedAmount - fee);
        }

        return new PayPalCaptureResult(captureId, status, capturedAmount, fee, net, _settings.Currency);
    }

    // ----------------------------------------------------------------- reauthorize (renew hold)

    public async Task<PayPalReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        CancellationToken ct = default)
    {
        var body = new { amount = Money(amount) };
        using var doc = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize", body, ct,
            idempotencyKey: null, preferRepresentation: true);
        var root = doc.RootElement;

        return new PayPalReauthorizationResult(
            root.GetProperty("id").GetString()!,
            root.TryGetProperty("status", out var st) ? st.GetString() ?? "CREATED" : "CREATED",
            ReadDate(root, "expiration_time"));
    }

    // ----------------------------------------------------------------- void (release hold)

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void", null, ct);
        // 204 No Content on success; SendAsync already validated the status.
    }

    // ----------------------------------------------------------------- refund

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount,
        string currency, string idempotencyKey, CancellationToken ct = default)
    {
        object body = amount.HasValue
            ? new { amount = Money(amount.Value) }
            : new { };

        using var doc = await SendAsync(HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund", body, ct,
            idempotencyKey: idempotencyKey, preferRepresentation: true);
        var root = doc.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "COMPLETED" : "COMPLETED";
        var refundedAmount = ReadMoney(root, "amount") ?? amount ?? 0m;

        return new PayPalRefundResult(refundId, status, refundedAmount, currency);
    }

    // ----------------------------------------------------------------- vault (save card)

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string? customerId,
        string idempotencyKey, CancellationToken ct = default)
    {
        object setupBody = customerId is null
            ? new { payment_source = new { card = BuildCardPayload(card) } }
            : new { customer = new { id = customerId }, payment_source = new { card = BuildCardPayload(card) } };

        using var setup = await SendAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupBody, ct,
            idempotencyKey: idempotencyKey);
        var setupTokenId = setup.RootElement.GetProperty("id").GetString()!;

        var tokenBody = new
        {
            payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } }
        };
        using var token = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", tokenBody, ct,
            idempotencyKey: $"{idempotencyKey}:pt");
        var root = token.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        string? vaultedCustomerId = root.TryGetProperty("customer", out var cust)
            && cust.TryGetProperty("id", out var cid) ? cid.GetString() : customerId;

        string brand = "UNKNOWN", last4 = "0000", expiry = card.Expiry, name = card.Name ?? string.Empty;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = c.TryGetProperty("brand", out var b) ? b.GetString() ?? brand : brand;
            last4 = c.TryGetProperty("last_digits", out var l) ? l.GetString() ?? last4 : last4;
            expiry = c.TryGetProperty("expiry", out var e) ? e.GetString() ?? expiry : expiry;
            name = c.TryGetProperty("name", out var n) ? n.GetString() ?? name : name;
        }

        return new VaultedCardResult(vaultId, vaultedCustomerId, brand, last4, expiry,
            string.IsNullOrWhiteSpace(name) ? card.Name : name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct);

        // Already gone is fine — the goal (card no longer usable) is met either way.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }
        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            throw ToApiException(response.StatusCode, raw);
        }
    }

    // ----------------------------------------------------------------- reconciliation

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        var transactions = new Dictionary<string, PayPalTransaction>(StringComparer.Ordinal);

        var windowStart = from;
        while (windowStart <= to)
        {
            var windowEnd = windowStart.AddDays(MaxWindowDays);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            while (true)
            {
                var url = "v1/reporting/transactions"
                    + $"?start_date={Uri.EscapeDataString(FormatDate(windowStart))}"
                    + $"&end_date={Uri.EscapeDataString(FormatDate(windowEnd))}"
                    + "&fields=all"
                    + $"&page_size={MaxPageSize}"
                    + $"&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, url, null, ct);
                var root = doc.RootElement;

                if (root.TryGetProperty("transaction_details", out var details)
                    && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info))
                        {
                            continue;
                        }
                        var txn = ReadTransaction(info);
                        if (txn is not null)
                        {
                            transactions[txn.TransactionId] = txn; // dedupe across window boundaries
                        }
                    }
                }

                var totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var t)
                    ? t : 1;
                if (page >= totalPages)
                {
                    break;
                }
                page++;
            }

            if (windowEnd >= to)
            {
                break;
            }
            windowStart = windowEnd.AddSeconds(1);
        }

        return transactions.Values.ToList();
    }

    private static PayPalTransaction? ReadTransaction(JsonElement info)
    {
        if (!info.TryGetProperty("transaction_id", out var idEl) || idEl.GetString() is not { } id)
        {
            return null;
        }

        var amount = ReadMoney(info, "transaction_amount") ?? 0m;
        var currency = ReadCurrency(info, "transaction_amount") ?? string.Empty;
        var fee = ReadMoney(info, "fee_amount");
        var status = info.TryGetProperty("transaction_status", out var s) ? s.GetString() : null;
        var invoiceId = info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null;
        var custom = info.TryGetProperty("custom_field", out var cf) ? cf.GetString() : null;
        var eventCode = info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null;
        var date = ReadDate(info, "transaction_initiation_date");

        return new PayPalTransaction(id, status, amount, currency, fee, invoiceId, custom, date, eventCode);
    }

    // ----------------------------------------------------------------- HTTP + auth plumbing

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await TokenGate.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _cachedToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw ToApiException(response.StatusCode, raw);
            }

            using var doc = JsonDocument.Parse(raw);
            var token = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var s)
                ? s : 300;

            _cachedToken = token;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return token;
        }
        finally
        {
            TokenGate.Release();
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        CancellationToken ct, string? idempotencyKey = null, bool preferRepresentation = false)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw ToApiException(response.StatusCode, raw);
        }

        // Some calls (void) return 204 with no body.
        if (string.IsNullOrWhiteSpace(raw))
        {
            return JsonDocument.Parse("{}");
        }
        return JsonDocument.Parse(raw);
    }

    private PayPalApiException ToApiException(HttpStatusCode statusCode, string raw)
    {
        string? name = null, message = null, debugId = null;
        var issues = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            debugId = root.TryGetProperty("debug_id", out var d) ? d.GetString() : null;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("issue", out var issue) && issue.GetString() is { } iss)
                    {
                        issues.Add(iss);
                    }
                }
            }
            // OAuth-style error payloads.
            if (name is null && root.TryGetProperty("error", out var err))
            {
                name = err.GetString();
                message ??= root.TryGetProperty("error_description", out var ed) ? ed.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with what we have.
        }

        var summary = message ?? name ?? $"PayPal request failed with status {(int)statusCode}.";
        var issueText = issues.Count > 0 ? $" [{string.Join(", ", issues)}]" : string.Empty;
        _logger.LogWarning("PayPal API error {0} {1}{2} (debug_id {3}).",
            (int)statusCode, name ?? "-", issueText, debugId ?? "-");

        return new PayPalApiException($"{summary}{issueText}", (int)statusCode, name, issues, debugId, raw);
    }

    // ----------------------------------------------------------------- payload / parsing helpers

    private object BuildCardPayload(CardDetails card)
    {
        object? billing = null;
        if (!string.IsNullOrWhiteSpace(card.AddressLine1) || !string.IsNullOrWhiteSpace(card.PostalCode)
            || !string.IsNullOrWhiteSpace(card.CountryCode))
        {
            billing = new
            {
                address_line_1 = card.AddressLine1,
                address_line_2 = card.AddressLine2,
                admin_area_1 = card.AdminArea1,
                admin_area_2 = card.AdminArea2,
                postal_code = card.PostalCode,
                country_code = card.CountryCode
            };
        }

        return new
        {
            number = card.Number,
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            name = card.Name,
            billing_address = billing
        };
    }

    private object Money(decimal amount) => new
    {
        currency_code = _settings.Currency,
        value = amount.ToString("F2", CultureInfo.InvariantCulture)
    };

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static JsonElement? FindFirstAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units)
            || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array && auths.GetArrayLength() > 0)
            {
                return auths[0];
            }
        }
        return null;
    }

    private static decimal? ReadMoney(JsonElement parent, string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out var money)
            && money.TryGetProperty("value", out var val)
            && val.GetString() is { } s
            && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return null;
    }

    private static string? ReadCurrency(JsonElement parent, string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out var money)
            && money.TryGetProperty("currency_code", out var cc))
        {
            return cc.GetString();
        }
        return null;
    }

    private static DateTimeOffset? ReadDate(JsonElement parent, string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(el.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt;
        }
        return null;
    }
}
