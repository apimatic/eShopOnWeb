using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Verified client over the PayPal REST APIs used by this integration. Every endpoint, field and
/// status handled here was confirmed against PayPal's sandbox before it was written. The client is
/// environment-agnostic: its base address is configured at registration from
/// <see cref="PayPalSettings.ResolveBaseUrl"/>.
///
/// Card details flow straight through to PayPal and are never persisted or logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    // PayPal Transaction Search allows at most a 31-day span per request; we use 30 to stay safely under.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(30);
    private const int SearchPageSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly PayPalTokenCache _tokenCache;
    private readonly IAppLogger<PayPalClient> _logger;

    public PayPalClient(HttpClient http, PayPalSettings settings, PayPalTokenCache tokenCache,
        IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _tokenCache = tokenCache;
        _logger = logger;
    }

    public async Task<PayPalAuthorization> AuthorizeAsync(decimal amount, string currency, string invoiceId,
        string orderReference, PayPalCard? card, string? vaultId, string idempotencyKey, CancellationToken ct = default)
    {
        object paymentSource = vaultId is not null
            ? new { card = new { vault_id = vaultId } }
            : new { card = BuildCardPayload(card!) };

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = invoiceId,
                    custom_id = orderReference,
                    amount = new { currency_code = currency, value = Money(amount) }
                }
            },
            payment_source = paymentSource
        };

        using var created = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, idempotencyKey,
            prefer: "return=representation", ct);
        var root = created!.RootElement;

        var payPalOrderId = root.GetProperty("id").GetString()!;
        var status = GetString(root, "status") ?? "UNKNOWN";

        // A card/vault payment processed inline already carries its authorization; otherwise (APPROVED)
        // we explicitly authorize the order.
        var authorization = FindAuthorization(root);
        if (authorization is null)
        {
            if (RequiresBuyerAction(root, status))
                throw BuyerActionRequired();

            if (status is "APPROVED" or "CREATED")
            {
                using var authorized = await SendAsync(HttpMethod.Post,
                    $"v2/checkout/orders/{payPalOrderId}/authorize", body: null, idempotencyKey + "-auth",
                    prefer: "return=representation", ct);
                authorization = FindAuthorization(authorized!.RootElement);
            }
        }

        if (authorization is null)
            throw new PayPalApiException(
                $"PayPal accepted order {payPalOrderId} with status '{status}' but returned no authorization to act on.");

        return authorization with
        {
            PayPalOrderId = payPalOrderId,
            InstrumentDescription = DescribeInstrument(root) ?? authorization.InstrumentDescription
        };
    }

    public async Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, null, null, ct);
        var root = doc!.RootElement;
        return new PayPalAuthorizationState(
            root.GetProperty("id").GetString()!,
            GetString(root, "status") ?? "UNKNOWN",
            GetDate(root, "expiration_time"));
    }

    public async Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = new { amount = new { currency_code = currency, value = Money(amount) }, final_capture = true };
        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture",
            body, idempotencyKey, prefer: "return=representation", ct);
        var root = doc!.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = GetString(root, "status") ?? "UNKNOWN";

        var breakdown = TryGetProperty(root, "seller_receivable_breakdown");
        if (breakdown is null)
        {
            // The capture POST can omit the money breakdown; fetch it from the capture resource.
            using var full = await SendAsync(HttpMethod.Get, $"v2/payments/captures/{captureId}", null, null, null, ct);
            var fullRoot = full!.RootElement;
            status = GetString(fullRoot, "status") ?? status;
            breakdown = TryGetProperty(fullRoot, "seller_receivable_breakdown");
        }

        decimal gross = amount, fee = 0m, net = amount;
        if (breakdown is { } b)
        {
            gross = MoneyValue(b, "gross_amount") ?? gross;
            fee = MoneyValue(b, "paypal_fee") ?? fee;
            net = MoneyValue(b, "net_amount") ?? (gross - fee);
        }

        return new PayPalCapture(captureId, status, gross, fee, net, currency);
    }

    public async Task<PayPalAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken ct = default)
    {
        var body = new { amount = new { currency_code = currency, value = Money(amount) } };
        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body, Guid.NewGuid().ToString("N"), prefer: "return=representation", ct);
        var root = doc!.RootElement;
        return new PayPalAuthorizationState(
            root.GetProperty("id").GetString()!,
            GetString(root, "status") ?? "UNKNOWN",
            GetDate(root, "expiration_time"));
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void",
            body: null, requestId: null, prefer: null, ct);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        object? body = amount is null
            ? null // full refund
            : new { amount = new { currency_code = currency, value = Money(amount.Value) } };

        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund",
            body, idempotencyKey, prefer: "return=representation", ct);
        var root = doc!.RootElement;

        var refundedAmount = MoneyValue(root, "amount") ?? amount ?? 0m;
        return new PayPalRefundResult(
            root.GetProperty("id").GetString()!,
            GetString(root, "status") ?? "UNKNOWN",
            refundedAmount,
            currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(PayPalCard card, string? customerId, string idempotencyKey,
        CancellationToken ct = default)
    {
        object setupBody = customerId is null
            ? new { payment_source = new { card = BuildCardPayload(card) } }
            : new { payment_source = new { card = BuildCardPayload(card) }, customer = new { id = customerId } };

        using var setup = await SendAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupBody,
            idempotencyKey + "-setup", prefer: null, ct);
        var setupId = setup!.RootElement.GetProperty("id").GetString()!;

        var tokenBody = new { payment_source = new { token = new { id = setupId, type = "SETUP_TOKEN" } } };
        using var token = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", tokenBody,
            idempotencyKey + "-token", prefer: null, ct);
        var root = token!.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        var custId = TryGetProperty(root, "customer") is { } c ? GetString(c, "id") : null;

        string brand = "CARD", last4 = "****", expiry = "";
        string? name = null;
        if (TryGetProperty(root, "payment_source") is { } ps && TryGetProperty(ps, "card") is { } cardEl)
        {
            brand = GetString(cardEl, "brand") ?? brand;
            last4 = GetString(cardEl, "last_digits") ?? last4;
            expiry = GetString(cardEl, "expiry") ?? expiry;
            name = GetString(cardEl, "name");
        }

        return new PayPalVaultedCard(vaultId, custId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultId}",
            body: null, requestId: null, prefer: null, ct);
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransactionRecord>();

        // Cover the whole range by chunking into windows within PayPal's per-request span limit.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to) windowEnd = to;

            await ReadAllPagesAsync(windowStart, windowEnd, results, ct);

            if (windowEnd == to) break;
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ReadAllPagesAsync(DateTimeOffset start, DateTimeOffset end,
        List<PayPalTransactionRecord> sink, CancellationToken ct)
    {
        var page = 1;
        int totalPages;
        do
        {
            var url = $"v1/reporting/transactions?start_date={Iso(start)}&end_date={Iso(end)}" +
                      $"&fields=all&page_size={SearchPageSize}&page={page}";
            using var doc = await SendAsync(HttpMethod.Get, url, null, null, null, ct);
            var root = doc!.RootElement;

            totalPages = TryGetProperty(root, "total_pages") is { } tp ? tp.GetInt32() : 1;

            if (TryGetProperty(root, "transaction_details") is { ValueKind: JsonValueKind.Array } details)
            {
                foreach (var d in details.EnumerateArray())
                {
                    if (TryGetProperty(d, "transaction_info") is not { } info) continue;
                    sink.Add(MapTransaction(info));
                }
            }

            page++;
        } while (page <= totalPages);
    }

    private static PayPalTransactionRecord MapTransaction(JsonElement info)
    {
        var amount = MoneyValue(info, "transaction_amount") ?? 0m;
        var currency = TryGetProperty(info, "transaction_amount") is { } amt ? GetString(amt, "currency_code") ?? "" : "";
        return new PayPalTransactionRecord(
            GetString(info, "transaction_id") ?? "",
            GetString(info, "transaction_event_code") ?? "",
            GetString(info, "transaction_status") ?? "",
            amount,
            currency,
            MoneyValue(info, "fee_amount"),
            GetDate(info, "transaction_initiation_date") ?? GetDate(info, "transaction_updated_date"),
            GetString(info, "invoice_id"),
            GetString(info, "custom_field"),
            GetString(info, "paypal_reference_id"));
    }

    // --- HTTP plumbing -----------------------------------------------------------------------

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        string? prefer, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (!string.IsNullOrEmpty(prefer))
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw BuildError(response.StatusCode, payload);

        if (string.IsNullOrWhiteSpace(payload))
            return null;
        return JsonDocument.Parse(payload);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_tokenCache.IsValid)
            return _tokenCache.AccessToken!;

        await _tokenCache.RefreshGate.WaitAsync(ct);
        try
        {
            if (_tokenCache.IsValid)
                return _tokenCache.AccessToken!;

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _http.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw BuildError(response.StatusCode, payload, "PayPal authentication failed");

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var accessToken = root.GetProperty("access_token").GetString()!;
            var expiresIn = TryGetProperty(root, "expires_in") is { } e ? e.GetInt32() : 300;

            _tokenCache.AccessToken = accessToken;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _tokenCache.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return accessToken;
        }
        finally
        {
            _tokenCache.RefreshGate.Release();
        }
    }

    private static PayPalApiException BuildError(HttpStatusCode statusCode, string payload, string? context = null)
    {
        string name = "", message = "", debugId = "";
        var issues = new List<string>();
        var requiresBuyerAction = false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            name = GetString(root, "name") ?? "";
            message = GetString(root, "message") ?? "";
            debugId = GetString(root, "debug_id") ?? "";
            if (TryGetProperty(root, "details") is { ValueKind: JsonValueKind.Array } details)
            {
                foreach (var d in details.EnumerateArray())
                {
                    var issue = GetString(d, "issue");
                    var desc = GetString(d, "description");
                    if (issue is not null) issues.Add(desc is null ? issue : $"{issue}: {desc}");
                    if (issue is not null && issue.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase))
                        requiresBuyerAction = true;
                }
            }
        }
        catch (JsonException)
        {
            message = payload;
        }

        var detail = string.Join("; ", issues);
        var full = $"{context ?? "PayPal API error"} ({(int)statusCode} {name}): {message}" +
                   (detail.Length > 0 ? $" [{detail}]" : "") +
                   (debugId.Length > 0 ? $" (debug_id {debugId})" : "");
        return new PayPalApiException(full, (int)statusCode, debugId, requiresBuyerAction);
    }

    private static PayPalApiException BuyerActionRequired() => new(
        "PayPal requires the shopper to approve this payment in a browser (a 3-D Secure / payer-action " +
        "challenge). This integration does not perform a browser approval round-trip; use a card that " +
        "clears without a challenge (e.g. the sandbox test card).",
        statusCode: 402, requiresBuyerAction: true);

    // --- payload helpers ---------------------------------------------------------------------

    private static object BuildCardPayload(PayPalCard card)
    {
        object? billing = card.BillingAddress is { } a
            ? new
            {
                address_line_1 = a.Line1,
                address_line_2 = a.Line2,
                admin_area_2 = a.AdminArea2,
                admin_area_1 = a.AdminArea1,
                postal_code = a.PostalCode,
                country_code = a.CountryCode
            }
            : null;

        return new
        {
            number = card.Number,
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            name = card.Name,
            billing_address = billing
        };
    }

    private static PayPalAuthorization? FindAuthorization(JsonElement orderRoot)
    {
        if (TryGetProperty(orderRoot, "purchase_units") is not { ValueKind: JsonValueKind.Array } units)
            return null;

        foreach (var unit in units.EnumerateArray())
        {
            if (TryGetProperty(unit, "payments") is not { } payments) continue;
            if (TryGetProperty(payments, "authorizations") is not { ValueKind: JsonValueKind.Array } auths) continue;
            foreach (var auth in auths.EnumerateArray())
            {
                var id = GetString(auth, "id");
                if (id is null) continue;
                return new PayPalAuthorization(
                    PayPalOrderId: "",
                    AuthorizationId: id,
                    Status: GetString(auth, "status") ?? "CREATED",
                    ExpiresAt: GetDate(auth, "expiration_time"),
                    InstrumentDescription: null);
            }
        }
        return null;
    }

    private static string? DescribeInstrument(JsonElement orderRoot)
    {
        if (TryGetProperty(orderRoot, "payment_source") is not { } ps) return null;
        if (TryGetProperty(ps, "card") is not { } card) return null;
        var brand = GetString(card, "brand");
        var last4 = GetString(card, "last_digits");
        if (last4 is null) return brand;
        return brand is null ? $"card ****{last4}" : $"{brand} ****{last4}";
    }

    private static bool RequiresBuyerAction(JsonElement root, string status)
    {
        if (status.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase)) return true;
        if (TryGetProperty(root, "links") is { ValueKind: JsonValueKind.Array } links)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = GetString(link, "rel");
                if (rel is not null && (rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase)
                                        || rel.Contains("3ds", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        return false;
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static JsonElement? TryGetProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static DateTimeOffset? GetDate(JsonElement element, string name) =>
        TryGetProperty(element, name) is { ValueKind: JsonValueKind.String } v
        && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d : null;

    private static decimal? MoneyValue(JsonElement element, string name)
    {
        if (TryGetProperty(element, name) is not { } money) return null;
        var value = GetString(money, "value");
        return value is not null && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }
}
