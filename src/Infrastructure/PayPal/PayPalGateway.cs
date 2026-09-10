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
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST client hand-written strictly against the OpenAPI specs in api-specs/:
/// Orders v2 (checkout_orders_v2), Payments v2 (payments_payment_v2), Payment Method Tokens v3
/// (vault_payment_tokens_v3) and Transaction Search v1 (transaction_search_v1). No PayPal SDK is used.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalGateway> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private const string TokenCacheKey = "paypal-access-token";

    public PayPalGateway(HttpClient http, PayPalOptions options, IMemoryCache cache,
        IAppLogger<PayPalGateway> logger)
    {
        _http = http;
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    private string BaseUrl => _options.ResolveBaseUrl();

    // ================================================================= OAuth

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(TokenCacheKey + ":" + _options.ClientId, out string? cached) &&
            !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new PayPalGatewayException((int)response.StatusCode, "authentication_error",
                $"Failed to obtain a PayPal access token ({(int)response.StatusCode}).", null,
                Array.Empty<string>());
        }

        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3000;

        _cache.Set(TokenCacheKey + ":" + _options.ClientId, token,
            TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
        return token;
    }

    // ================================================================= HTTP core

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, string? prefer, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (!string.IsNullOrEmpty(prefer))
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError((int)response.StatusCode, content);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        return JsonDocument.Parse(content);
    }

    private static PayPalGatewayException ParseError(int statusCode, string content)
    {
        string? name = null, message = null, debugId = null;
        var issues = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("name", out var n)) name = n.GetString();
            if (root.TryGetProperty("message", out var m)) message = m.GetString();
            if (root.TryGetProperty("debug_id", out var d)) debugId = d.GetString();
            if (root.TryGetProperty("error", out var errName)) name ??= errName.GetString();
            if (root.TryGetProperty("error_description", out var errDesc)) message ??= errDesc.GetString();
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("issue", out var issue) && issue.GetString() is { } s)
                    {
                        issues.Add(s);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // non-JSON error body
        }

        var summary = $"PayPal API error {statusCode}"
            + (name is not null ? $" [{name}]" : string.Empty)
            + (issues.Count > 0 ? $" ({string.Join(", ", issues)})" : string.Empty)
            + (message is not null ? $": {message}" : string.Empty)
            + (debugId is not null ? $" (debug_id={debugId})" : string.Empty);

        return new PayPalGatewayException(statusCode, name, summary, debugId, issues);
    }

    // ================================================================= Orders v2

    public async Task<GatewayAuthorizationResult> AuthorizeAsync(GatewayAuthorizeRequest request,
        CancellationToken ct = default)
    {
        var paymentSource = BuildPaymentSource(request);

        var orderBody = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = request.InvoiceId,
                    ["custom_id"] = request.CustomId,
                    ["amount"] = Money(request.Amount, request.CurrencyCode)
                }
            },
            ["payment_source"] = paymentSource
        };

        using var createDoc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", orderBody,
            request.RequestId, "return=representation", ct);
        var order = createDoc!.RootElement;

        var payPalOrderId = order.GetProperty("id").GetString()!;
        GuardNoChallenge(order, payPalOrderId);

        // Single-step card processing may already have created the authorization; otherwise
        // authorize explicitly.
        var authElement = FindAuthorization(order);
        if (authElement is null)
        {
            using var authDoc = await SendAsync(HttpMethod.Post,
                $"/v2/checkout/orders/{payPalOrderId}/authorize", new Dictionary<string, object?>(),
                request.RequestId + "-auth", "return=representation", ct);
            var authorized = authDoc!.RootElement;
            GuardNoChallenge(authorized, payPalOrderId);
            authElement = FindAuthorization(authorized);

            if (authElement is null)
            {
                throw new PayPalGatewayException(502, "no_authorization",
                    "PayPal did not return an authorization for the order.", null, Array.Empty<string>());
            }

            return BuildAuthorizationResult(payPalOrderId, authElement.Value, authorized, request);
        }

        return BuildAuthorizationResult(payPalOrderId, authElement.Value, order, request);
    }

    private Dictionary<string, object?> BuildPaymentSource(GatewayAuthorizeRequest request)
    {
        if (request.VaultId is not null)
        {
            // Pay with a saved card: reference the vault token id.
            return new Dictionary<string, object?>
            {
                ["card"] = new Dictionary<string, object?> { ["vault_id"] = request.VaultId }
            };
        }

        if (request.Card is null)
        {
            throw new PayPalGatewayException(400, "missing_payment_source",
                "A card or a saved card must be supplied to authorize.", null, Array.Empty<string>());
        }

        var card = BuildCard(request.Card);

        if (request.SaveCard && request.CustomerId is not null)
        {
            // Vault the card on a successful authorization (store_in_vault: ON_SUCCESS).
            card["attributes"] = new Dictionary<string, object?>
            {
                ["vault"] = new Dictionary<string, object?> { ["store_in_vault"] = "ON_SUCCESS" },
                ["customer"] = new Dictionary<string, object?> { ["id"] = request.CustomerId }
            };
        }

        return new Dictionary<string, object?> { ["card"] = card };
    }

    private static Dictionary<string, object?> BuildCard(GatewayCardDetails details)
    {
        var card = new Dictionary<string, object?>
        {
            ["number"] = details.Number,
            ["expiry"] = details.Expiry
        };
        if (!string.IsNullOrEmpty(details.Name)) card["name"] = details.Name;
        if (!string.IsNullOrEmpty(details.SecurityCode)) card["security_code"] = details.SecurityCode;

        if (details.BillingAddress is { } addr)
        {
            card["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = addr.AddressLine1,
                ["address_line_2"] = addr.AddressLine2,
                ["admin_area_2"] = addr.AdminArea2,
                ["admin_area_1"] = addr.AdminArea1,
                ["postal_code"] = addr.PostalCode,
                ["country_code"] = addr.CountryCode
            };
        }
        return card;
    }

    private void GuardNoChallenge(JsonElement order, string payPalOrderId)
    {
        var status = order.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            var approvalUrl = FindLink(order, "payer-action") ?? FindLink(order, "approve");
            throw new PayPalChallengeRequiredException(
                $"PayPal requires the shopper to approve this payment in a browser (order {payPalOrderId}, " +
                "status PAYER_ACTION_REQUIRED). This integration does not build a browser approval step.",
                approvalUrl);
        }
    }

    private static string? FindLink(JsonElement element, string rel)
    {
        if (!element.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var link in links.EnumerateArray())
        {
            if (link.TryGetProperty("rel", out var r) &&
                string.Equals(r.GetString(), rel, StringComparison.OrdinalIgnoreCase) &&
                link.TryGetProperty("href", out var href))
            {
                return href.GetString();
            }
        }
        return null;
    }

    private static JsonElement? FindAuthorization(JsonElement order)
    {
        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) &&
                auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                {
                    return auth; // first authorization
                }
            }
        }
        return null;
    }

    private GatewayAuthorizationResult BuildAuthorizationResult(string payPalOrderId,
        JsonElement auth, JsonElement order, GatewayAuthorizeRequest request)
    {
        var authId = auth.GetProperty("id").GetString()!;
        var status = auth.TryGetProperty("status", out var s) ? s.GetString() ?? "CREATED" : "CREATED";
        DateTimeOffset? expiresAt = null;
        if (auth.TryGetProperty("expiration_time", out var exp) &&
            DateTimeOffset.TryParse(exp.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
        {
            expiresAt = parsed;
        }

        // If the card was vaulted, surface the new token + safe description.
        string? vaultedCardId = null;
        GatewayCardDescription? vaultedCard = null;
        if (order.TryGetProperty("payment_source", out var ps) &&
            ps.TryGetProperty("card", out var respCard))
        {
            if (respCard.TryGetProperty("attributes", out var attrs) &&
                attrs.TryGetProperty("vault", out var vault) &&
                vault.TryGetProperty("id", out var vid))
            {
                vaultedCardId = vid.GetString();
            }
            vaultedCard = ReadCardDescription(respCard);
        }

        return new GatewayAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authId,
            AuthorizationStatus = status,
            ExpiresAt = expiresAt,
            Amount = request.Amount,
            CurrencyCode = request.CurrencyCode,
            VaultedCardId = vaultedCardId,
            VaultedCard = vaultedCard
        };
    }

    // ================================================================= Payments v2

    public async Task<GatewayAuthorizationResult> ReauthorizeAsync(string authorizationId,
        decimal amount, string currencyCode, string requestId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount, currencyCode) };
        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId,
            "return=representation", ct);
        var auth = doc!.RootElement;

        var newAuthId = auth.TryGetProperty("id", out var id) ? id.GetString()! : authorizationId;
        var status = auth.TryGetProperty("status", out var s) ? s.GetString() ?? "CREATED" : "CREATED";
        DateTimeOffset? expiresAt = null;
        if (auth.TryGetProperty("expiration_time", out var exp) &&
            DateTimeOffset.TryParse(exp.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
        {
            expiresAt = parsed;
        }

        return new GatewayAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = newAuthId,
            AuthorizationStatus = status,
            ExpiresAt = expiresAt,
            Amount = amount,
            CurrencyCode = currencyCode
        };
    }

    public async Task<GatewayCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currencyCode, string requestId, CancellationToken ct = default)
    {
        // invoice_id / custom_id propagate from the order to the capture; we only set amount here.
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(amount, currencyCode),
            ["final_capture"] = true
        };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId,
            "return=representation", ct);
        var capture = doc!.RootElement;

        var captureId = capture.GetProperty("id").GetString()!;
        var status = capture.TryGetProperty("status", out var s) ? s.GetString() ?? "COMPLETED" : "COMPLETED";
        var capturedAmount = ReadMoneyValue(capture, "amount") ?? amount;

        decimal? fee = null, net = null;
        if (capture.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = ReadMoneyValue(breakdown, "paypal_fee");
            net = ReadMoneyValue(breakdown, "net_amount");
        }

        return new GatewayCaptureResult
        {
            CaptureId = captureId,
            Status = status,
            Amount = capturedAmount,
            PayPalFee = fee,
            NetAmount = net,
            CurrencyCode = currencyCode
        };
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        // Prefer: return=minimal → 204 No Content on success.
        using var _ = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null, null, "return=minimal", ct);
    }

    public async Task<GatewayRefundResult> RefundAsync(string captureId, decimal? amount,
        string currencyCode, string customId, string requestId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["custom_id"] = customId };
        if (amount is not null)
        {
            body["amount"] = Money(amount.Value, currencyCode);
        }

        // A capture can be briefly non-refundable while it settles. Retry transient failures with
        // backoff; the caller's idempotency key is the PayPal-Request-Id, so retries never
        // double-refund (PayPal returns the same refund for a repeated key).
        JsonDocument? doc = null;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                doc = await SendAsync(HttpMethod.Post,
                    $"/v2/payments/captures/{captureId}/refund", body, requestId,
                    "return=representation", ct);
                break;
            }
            catch (PayPalGatewayException ex) when (IsTransient(ex) && attempt < 5)
            {
                _logger.LogWarning(
                    $"Refund of capture {captureId} hit a transient error (attempt {attempt}, {ex.Message}); retrying.");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
            catch (Exception ex) when (
                (ex is HttpRequestException || ex is TaskCanceledException) && attempt < 5)
            {
                _logger.LogWarning(
                    $"Refund of capture {captureId} hit a transient network error (attempt {attempt}, {ex.Message}); retrying.");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }
        var refund = doc!.RootElement;

        var refundId = refund.GetProperty("id").GetString()!;
        var status = refund.TryGetProperty("status", out var s) ? s.GetString() ?? "COMPLETED" : "COMPLETED";
        var refundedAmount = ReadMoneyValue(refund, "amount") ?? amount ?? 0m;

        return new GatewayRefundResult
        {
            RefundId = refundId,
            Status = status,
            Amount = refundedAmount,
            CurrencyCode = currencyCode
        };
    }

    // ================================================================= Vault v3

    public async Task<GatewayVaultCardResult> VaultCardAsync(string customerId,
        GatewayCardDetails card, string requestId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?> { ["id"] = customerId },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCard(card) }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId,
            null, ct);
        var token = doc!.RootElement;

        var tokenId = token.GetProperty("id").GetString()!;
        var resolvedCustomerId = customerId;
        if (token.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid))
        {
            resolvedCustomerId = cid.GetString() ?? customerId;
        }

        GatewayCardDescription? desc = null;
        if (token.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var respCard))
        {
            desc = ReadCardDescription(respCard);
        }

        return new GatewayVaultCardResult
        {
            TokenId = tokenId,
            Brand = desc?.Brand,
            LastDigits = desc?.LastDigits,
            Expiry = desc?.Expiry,
            CardholderName = desc?.CardholderName,
            CustomerId = resolvedCustomerId
        };
    }

    public async Task DeleteVaultedCardAsync(string tokenId, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{tokenId}", null,
            null, null, ct);
    }

    private static GatewayCardDescription ReadCardDescription(JsonElement card)
    {
        string? brand = card.TryGetProperty("brand", out var b) ? b.GetString() : null;
        string? last = card.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
        string? expiry = card.TryGetProperty("expiry", out var e) ? e.GetString() : null;
        string? name = card.TryGetProperty("name", out var n) ? n.GetString() : null;
        return new GatewayCardDescription(brand, last, expiry, name);
    }

    // ================================================================= Transaction Search v1

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<GatewayTransaction>();

        // The spec caps a single query at a 31-day window; chunk longer ranges.
        var windowStart = from.ToUniversalTime();
        var end = to.ToUniversalTime();
        var maxWindow = TimeSpan.FromDays(31);

        while (windowStart < end)
        {
            var windowEnd = windowStart + maxWindow;
            if (windowEnd > end) windowEnd = end;

            await CollectWindowAsync(windowStart, windowEnd, results, ct);

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task CollectWindowAsync(DateTimeOffset start, DateTimeOffset end,
        List<GatewayTransaction> results, CancellationToken ct)
    {
        const int pageSize = 500;
        int page = 1;
        int totalPages;

        do
        {
            var query =
                $"start_date={Uri.EscapeDataString(FormatDate(start))}" +
                $"&end_date={Uri.EscapeDataString(FormatDate(end))}" +
                $"&fields=transaction_info" +
                $"&balance_affecting_records_only=N" +
                $"&page_size={pageSize}" +
                $"&page={page}";

            using var doc = await SendAsync(HttpMethod.Get, $"/v1/reporting/transactions?{query}", null,
                null, null, ct);
            var root = doc!.RootElement;

            totalPages = root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number
                ? tp.GetInt32()
                : 0;

            if (root.TryGetProperty("transaction_details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("transaction_info", out var info))
                    {
                        results.Add(ReadTransaction(info));
                    }
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    private static GatewayTransaction ReadTransaction(JsonElement info)
    {
        string? Str(string name) => info.TryGetProperty(name, out var v) ? v.GetString() : null;

        DateTimeOffset? date = null;
        if (info.TryGetProperty("transaction_initiation_date", out var d) &&
            DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
        {
            date = parsed;
        }

        return new GatewayTransaction
        {
            TransactionId = Str("transaction_id"),
            Status = Str("transaction_status"),
            EventCode = Str("transaction_event_code"),
            InitiationDate = date,
            Amount = ReadMoneyValue(info, "transaction_amount"),
            CurrencyCode = ReadMoneyCurrency(info, "transaction_amount"),
            FeeAmount = ReadMoneyValue(info, "fee_amount"),
            InvoiceId = Str("invoice_id"),
            CustomField = Str("custom_field"),
            ReferenceId = Str("paypal_reference_id")
        };
    }

    // ================================================================= shared helpers

    private static Dictionary<string, object?> Money(decimal amount, string currencyCode) => new()
    {
        ["currency_code"] = currencyCode,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal? ReadMoneyValue(JsonElement parent, string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out var money) &&
            money.TryGetProperty("value", out var value) &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return null;
    }

    private static string? ReadMoneyCurrency(JsonElement parent, string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out var money) &&
            money.TryGetProperty("currency_code", out var c))
        {
            return c.GetString();
        }
        return null;
    }

    // A capture can be briefly non-refundable right after it is taken; treat server errors and
    // unprocessable/conflict responses as transient for the (idempotent) refund retry.
    private static bool IsTransient(PayPalGatewayException ex) =>
        ex.StatusCode >= 500 || ex.StatusCode == 422 || ex.StatusCode == 409;

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
