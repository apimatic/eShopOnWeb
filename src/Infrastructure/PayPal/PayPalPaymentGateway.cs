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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST implementation of <see cref="IPaymentGateway"/>, built on the confirmed current
/// PayPal contract: Orders v2 (authorize), Payments v2 (capture / void / reauthorize / refund),
/// Payment Method Tokens v3 (vault), and Transaction Search v1 (reconciliation). It owns OAuth
/// token acquisition/caching, idempotency headers, and translation of PayPal error payloads.
///
/// Card numbers are forwarded straight to PayPal and are never persisted or logged by this class.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;
    private readonly string _baseUrl;

    // The gateway is resolved as a typed HttpClient (transient), but there is a single merchant
    // account per process, so the OAuth token is cached process-wide to avoid re-minting it on
    // every request.
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);
    private static string? _accessToken;
    private static DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalPaymentGateway(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _baseUrl = _settings.ResolveBaseUrl();
    }

    public string Currency => string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency.Trim().ToUpperInvariant();

    // ---------------------------------------------------------------- Authorize (Orders v2) ----

    public Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string idempotencyKey, CardDetails card, string invoiceId, CancellationToken cancellationToken)
    {
        var cardSource = BuildCardSource(card);
        return CreateAuthorizationOrderAsync(amount, idempotencyKey, cardSource, invoiceId, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeWithVaultAsync(
        decimal amount, string idempotencyKey, string vaultId, string invoiceId, CancellationToken cancellationToken)
    {
        var cardSource = new Dictionary<string, object?> { ["vault_id"] = vaultId };
        return CreateAuthorizationOrderAsync(amount, idempotencyKey, cardSource, invoiceId, cancellationToken);
    }

    private async Task<PayPalAuthorizationResult> CreateAuthorizationOrderAsync(
        decimal amount, string idempotencyKey, Dictionary<string, object?> cardSource, string invoiceId, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = invoiceId,
                    amount = new { currency_code = Currency, value = FormatAmount(amount) }
                }
            },
            payment_source = new { card = cardSource }
        };

        using var order = await SendAsync(
            HttpMethod.Post, "/v2/checkout/orders", body, cancellationToken,
            idempotencyKey: idempotencyKey, preferRepresentation: true);

        var root = order.RootElement;
        var payPalOrderId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return an order id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        if (status == "PAYER_ACTION_REQUIRED")
        {
            throw new PaymentGatewayException(
                "The card requires additional buyer authentication (3-D Secure) that cannot be completed without a " +
                "browser. The payment was not authorized.");
        }

        if (TryExtractAuthorization(root, out var auth))
        {
            return new PayPalAuthorizationResult(payPalOrderId, status, auth.Id, auth.Status, auth.ExpiresAt);
        }

        // Card provided inline normally authorizes on creation; if not, drive the authorize step.
        if (status is "APPROVED" or "CREATED")
        {
            using var authorized = await SendAsync(
                HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", new { }, cancellationToken,
                idempotencyKey: idempotencyKey + "-authorize", preferRepresentation: true);

            var authorizedRoot = authorized.RootElement;
            var authorizedStatus = GetString(authorizedRoot, "status") ?? status;
            if (TryExtractAuthorization(authorizedRoot, out var auth2))
            {
                return new PayPalAuthorizationResult(payPalOrderId, authorizedStatus, auth2.Id, auth2.Status, auth2.ExpiresAt);
            }
        }

        throw new PaymentGatewayException(
            $"PayPal accepted the order but returned no authorization to act on (order status {status}).");
    }

    // ---------------------------------------------------------------- Payments v2 ----

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, cancellationToken);
        var root = doc.RootElement;
        return new PayPalAuthorizationDetails(GetString(root, "status") ?? "UNKNOWN", GetDate(root, "expiration_time"));
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new { amount = new { currency_code = Currency, value = FormatAmount(amount) } };
        using var doc = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, cancellationToken,
            idempotencyKey: idempotencyKey, preferRepresentation: true);

        var root = doc.RootElement;
        var newId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return a reauthorized authorization id.");
        return new PayPalAuthorizationResult(
            PayPalOrderId: string.Empty,
            OrderStatus: string.Empty,
            AuthorizationId: newId,
            AuthorizationStatus: GetString(root, "status") ?? "CREATED",
            ExpiresAt: GetDate(root, "expiration_time"));
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId, decimal amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new
        {
            amount = new { currency_code = Currency, value = FormatAmount(amount) },
            final_capture = true
        };

        using var doc = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, cancellationToken,
            idempotencyKey: idempotencyKey, preferRepresentation: true);

        var root = doc.RootElement;
        var captureId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return a capture id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        var gross = amount;
        var fee = 0m;
        var net = amount;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ParseMoney(breakdown, "gross_amount") ?? gross;
            fee = ParseMoney(breakdown, "paypal_fee") ?? 0m;
            net = ParseMoney(breakdown, "net_amount") ?? (gross - fee);
        }

        return new PayPalCaptureResult(captureId, status, gross, fee, net, Currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", new { }, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        object body = amount.HasValue
            ? new { amount = new { currency_code = Currency, value = FormatAmount(amount.Value) } }
            : new { };

        using var doc = await SendAsync(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, cancellationToken,
            idempotencyKey: idempotencyKey, preferRepresentation: true);

        var root = doc.RootElement;
        var refundId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return a refund id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        var refunded = ParseMoney(root, "amount") ?? amount ?? 0m;
        return new PayPalRefundResult(refundId, status, refunded, Currency);
    }

    // ---------------------------------------------------------------- Vault (Payment Tokens v3) ----

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string? existingCustomerId, CancellationToken cancellationToken)
    {
        // Step 1: create a setup token from the raw card (optionally under an existing customer).
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new { card = BuildCardSource(card) }
        };
        if (!string.IsNullOrWhiteSpace(existingCustomerId))
        {
            setupBody["customer"] = new { id = existingCustomerId };
        }

        string setupTokenId;
        using (var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, cancellationToken, preferRepresentation: true))
        {
            setupTokenId = GetString(setup.RootElement, "id")
                ?? throw new PaymentGatewayException("PayPal did not return a setup token id.");
        }

        // Step 2: exchange the setup token for a durable payment token.
        var tokenBody = new
        {
            payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } }
        };

        using var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody, cancellationToken, preferRepresentation: true);
        var root = token.RootElement;

        var vaultId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return a payment token id.");
        var customerId = existingCustomerId ?? string.Empty;
        if (root.TryGetProperty("customer", out var customer))
        {
            customerId = GetString(customer, "id") ?? customerId;
        }

        var brand = "CARD";
        var last4 = "****";
        var expiry = card.Expiry;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand") ?? brand;
            last4 = GetString(cardEl, "last_digits") ?? last4;
            expiry = GetString(cardEl, "expiry") ?? expiry;
        }

        return new VaultedCard(vaultId, customerId, brand, last4, NormalizeExpiry(expiry));
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, cancellationToken);
    }

    // ---------------------------------------------------------------- Transaction search (reporting) ----

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransaction>();

        // PayPal caps a query at a 31-day range; walk the whole span in <=30-day windows.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(30);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await ReadTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);

            if (windowEnd >= to)
            {
                break;
            }
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ReadTransactionWindowAsync(
        DateTimeOffset start, DateTimeOffset end, List<PayPalTransaction> sink, CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query =
                $"?start_date={Uri.EscapeDataString(FormatDate(start))}" +
                $"&end_date={Uri.EscapeDataString(FormatDate(end))}" +
                "&fields=transaction_info&balance_affecting_records_only=Y" +
                $"&page_size=500&page={page}";

            using var doc = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions" + query, null, cancellationToken);
            var root = doc.RootElement;

            totalPages = root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 1;

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    var id = GetString(info, "transaction_id");
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    sink.Add(new PayPalTransaction(
                        id!,
                        GetString(info, "transaction_status") ?? "UNKNOWN",
                        GetString(info, "transaction_event_code") ?? string.Empty,
                        ParseMoney(info, "transaction_amount") ?? 0m,
                        ParseMoney(info, "fee_amount") ?? 0m,
                        GetMoneyCurrency(info, "transaction_amount") ?? Currency,
                        GetDate(info, "transaction_initiation_date") ?? start));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ---------------------------------------------------------------- HTTP plumbing ----

    private async Task<JsonDocument> SendAsync(
        HttpMethod method, string path, object? body, CancellationToken cancellationToken,
        string? idempotencyKey = null, bool preferRepresentation = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
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

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildGatewayException(response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return JsonDocument.Parse("{}");
        }

        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new PaymentGatewayException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildGatewayException(response.StatusCode, payload, "Failed to obtain a PayPal access token");
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var accessToken = GetString(root, "access_token")
                ?? throw new PaymentGatewayException("PayPal token response did not contain an access_token.");
            var expiresIn = root.TryGetProperty("expires_in", out var exp) && exp.ValueKind == JsonValueKind.Number
                ? exp.GetInt32() : 3600;

            _accessToken = accessToken;
            // Refresh a minute early to avoid using a token that expires mid-request.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private PaymentGatewayException BuildGatewayException(HttpStatusCode statusCode, string payload, string? context = null)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        var details = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            name = GetString(root, "name") ?? GetString(root, "error");
            message = GetString(root, "message") ?? GetString(root, "error_description");
            debugId = GetString(root, "debug_id");

            if (root.TryGetProperty("details", out var det) && det.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in det.EnumerateArray())
                {
                    var issue = GetString(d, "issue");
                    var description = GetString(d, "description");
                    var combined = string.Join(": ", new[] { issue, description }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (!string.IsNullOrWhiteSpace(combined))
                    {
                        details.Add(combined);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to the raw payload below.
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context)) parts.Add(context!);
        parts.Add($"PayPal returned {(int)statusCode}");
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name!);
        if (!string.IsNullOrWhiteSpace(message)) parts.Add(message!);
        if (details.Count > 0) parts.Add(string.Join("; ", details));
        if (parts.Count <= 2 && !string.IsNullOrWhiteSpace(payload)) parts.Add(Truncate(payload, 300));

        var text = string.Join(" - ", parts);
        _logger.LogWarning("PayPal error ({StatusCode}) debugId={DebugId}: {Message}", (int)statusCode, debugId, text);
        return new PaymentGatewayException(text, debugId);
    }

    // ---------------------------------------------------------------- Helpers ----

    private static Dictionary<string, object?> BuildCardSource(CardDetails card)
    {
        var source = new Dictionary<string, object?>
        {
            ["number"] = card.Number.Replace(" ", string.Empty).Replace("-", string.Empty),
            ["expiry"] = NormalizeExpiry(card.Expiry),
            ["security_code"] = card.SecurityCode
        };

        if (!string.IsNullOrWhiteSpace(card.CardholderName))
        {
            source["name"] = card.CardholderName;
        }

        var billing = card.BillingAddress;
        source["billing_address"] = new
        {
            address_line_1 = Coalesce(billing?.AddressLine1, "1 Main St"),
            address_line_2 = billing?.AddressLine2,
            admin_area_2 = Coalesce(billing?.AdminArea2, "San Jose"),
            admin_area_1 = Coalesce(billing?.AdminArea1, "CA"),
            postal_code = Coalesce(billing?.PostalCode, "95131"),
            country_code = Coalesce(billing?.CountryCode, "US")
        };

        return source;
    }

    /// <summary>Normalises common card-expiry inputs to PayPal's required <c>YYYY-MM</c> form.</summary>
    internal static string NormalizeExpiry(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return expiry;
        }

        var value = expiry.Trim();

        // Already YYYY-MM.
        if (value.Length == 7 && value[4] == '-')
        {
            return value;
        }

        var separators = new[] { '/', '-', ' ' };
        var parts = value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            var a = parts[0];
            var b = parts[1];

            // YYYY-MM
            if (a.Length == 4 && int.TryParse(a, out _))
            {
                return $"{a}-{b.PadLeft(2, '0')}";
            }

            // MM/YY or MM/YYYY
            var month = a.PadLeft(2, '0');
            var year = b.Length == 2 ? "20" + b : b;
            return $"{year}-{month}";
        }

        return value;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset when) =>
        when.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string Coalesce(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();

    private static string Truncate(string value, int max) => value.Length <= max ? value : value.Substring(0, max) + "…";

    private readonly record struct AuthorizationInfo(string Id, string Status, DateTimeOffset? ExpiresAt);

    private static bool TryExtractAuthorization(JsonElement orderRoot, out AuthorizationInfo info)
    {
        info = default;
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments)) continue;
            if (!payments.TryGetProperty("authorizations", out var auths) || auths.ValueKind != JsonValueKind.Array) continue;

            foreach (var auth in auths.EnumerateArray())
            {
                var id = GetString(auth, "id");
                if (string.IsNullOrEmpty(id)) continue;

                info = new AuthorizationInfo(
                    id!, GetString(auth, "status") ?? "CREATED", GetDate(auth, "expiration_time"));
                return true;
            }
        }

        return false;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        var text = GetString(element, property);
        if (text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static decimal? ParseMoney(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var money)) return null;
        var value = GetString(money, "value");
        return value is not null && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : null;
    }

    private static string? GetMoneyCurrency(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var money) ? GetString(money, "currency_code") : null;
}
