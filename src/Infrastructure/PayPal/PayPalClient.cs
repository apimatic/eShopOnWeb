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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST client covering Orders v2 (authorize), Payments v2 (capture/void/reauthorize/refund),
/// Vault v3 (save/delete card) and Reporting v1 (transaction list). Card details flow through this class
/// straight to PayPal; they are never persisted and never written to logs.
/// </summary>
public sealed class PayPalClient : IPayPalClient
{
    private const int ReportingWindowDays = 30; // stay safely inside PayPal's 31-day reporting limit
    private const int ReportingPageSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly IPayPalTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;

    public PayPalClient(HttpClient http, IPayPalTokenProvider tokenProvider,
        IOptions<PayPalSettings> settings, IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _tokenProvider = tokenProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    // --- Orders v2: authorize ----------------------------------------------

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(PayPalAuthorizeRequest request,
        CancellationToken ct = default)
    {
        object cardNode = request.VaultId is not null
            ? new { vault_id = request.VaultId }
            : BuildCardNode(request.Card!);

        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    custom_id = request.CustomId,
                    amount = new { currency_code = request.Currency, value = Money(request.Amount) }
                }
            },
            payment_source = new { card = cardNode }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            requestId: request.RequestId, prefer: "return=representation", ct: ct);
        var root = doc!.RootElement;

        EnsureNoBrowserChallenge(root);

        var orderId = GetString(root, "id") ?? string.Empty;
        if (!TryGetFirstAuthorization(root, out var auth))
        {
            var status = GetString(root, "status") ?? "UNKNOWN";
            throw new PaymentException(
                $"PayPal did not create an authorization (order status '{status}'). The card may have been declined.");
        }

        var authId = GetString(auth, "id")!;
        var authStatus = GetString(auth, "status") ?? "CREATED";
        var expiresAt = ParseDate(GetString(auth, "expiration_time"));
        var (brand, last4) = ReadCardDescriptor(root);

        return new PayPalAuthorizationResult(orderId, authId, authStatus, expiresAt, brand, last4);
    }

    // --- Payments v2: authorization lifecycle ------------------------------

    public async Task<string?> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}",
            body: null, requestId: null, prefer: null, ct: ct);
        return GetString(doc!.RootElement, "status");
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken ct = default)
    {
        var body = new
        {
            amount = new { currency_code = currency, value = Money(amount) },
            final_capture = true
        };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body,
            requestId: requestId, prefer: "return=representation", ct: ct);
        var root = doc!.RootElement;

        var captureId = GetString(root, "id")!;
        var status = GetString(root, "status") ?? "COMPLETED";

        var (gross, fee, net, cur) = ReadReceivableBreakdown(root, amount, currency);
        if (gross is null)
        {
            // Representation did not include the breakdown — read it back explicitly.
            using var capDoc = await SendAsync(HttpMethod.Get, $"/v2/payments/captures/{captureId}",
                body: null, requestId: null, prefer: null, ct: ct);
            (gross, fee, net, cur) = ReadReceivableBreakdown(capDoc!.RootElement, amount, currency);
        }

        return new PayPalCaptureResult(captureId, status, gross ?? amount, fee ?? 0m, net ?? (gross ?? amount), cur);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken ct = default)
    {
        var body = new { amount = new { currency_code = currency, value = Money(amount) } };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            requestId: requestId, prefer: "return=representation", ct: ct);
        var root = doc!.RootElement;

        var newAuthId = GetString(root, "id")!;
        var status = GetString(root, "status") ?? "CREATED";
        var expiresAt = ParseDate(GetString(root, "expiration_time"));
        return new PayPalAuthorizationResult(string.Empty, newAuthId, status, expiresAt, null, null);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            body: null, requestId: requestId, prefer: null, ct: ct);
    }

    // --- Payments v2: refund -----------------------------------------------

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, string? noteToPayer, CancellationToken ct = default)
    {
        object? body = amount is null
            ? (noteToPayer is null ? null : new { note_to_payer = noteToPayer })
            : new
            {
                amount = new { currency_code = currency, value = Money(amount.Value) },
                note_to_payer = noteToPayer
            };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body,
            requestId: requestId, prefer: "return=representation", ct: ct);
        var root = doc!.RootElement;

        var refundId = GetString(root, "id")!;
        var status = GetString(root, "status") ?? "COMPLETED";
        var (value, cur) = ReadMoney(root, "amount");
        return new PayPalRefundResult(refundId, status, value ?? amount ?? 0m, cur ?? currency);
    }

    // --- Vault v3: saved cards ---------------------------------------------

    public async Task<PayPalVaultCardResult> VaultCardAsync(PayPalCardDetails card, string? customerId,
        string requestId, CancellationToken ct = default)
    {
        object body = customerId is null
            ? new { payment_source = new { card = BuildCardNode(card) } }
            : new { customer = new { id = customerId }, payment_source = new { card = BuildCardNode(card) } };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            requestId: requestId, prefer: null, ct: ct);
        var root = doc!.RootElement;

        var vaultId = GetString(root, "id")!;
        string? returnedCustomerId = null;
        if (root.TryGetProperty("customer", out var customer))
            returnedCustomerId = GetString(customer, "id");

        var (brand, last4) = ReadCardDescriptor(root);
        string expiry = "";
        string? name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            expiry = GetString(cardEl, "expiry") ?? "";
            name = GetString(cardEl, "name");
        }

        return new PayPalVaultCardResult(vaultId, returnedCustomerId, brand ?? "CARD", last4 ?? "0000", expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}",
            body: null, requestId: null, prefer: null, ct: ct);
    }

    // --- Reporting v1: reconciliation --------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(ReportingWindowDays);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            var totalPages = 1;
            do
            {
                var query = $"/v1/reporting/transactions?start_date={Rfc3339(windowStart)}&end_date={Rfc3339(windowEnd)}" +
                            $"&fields=all&page_size={ReportingPageSize}&page={page}";
                using var doc = await SendAsync(HttpMethod.Get, query, body: null, requestId: null, prefer: null, ct: ct);
                var root = doc!.RootElement;

                if (root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var parsedPages))
                    totalPages = parsedPages;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in details.EnumerateArray())
                    {
                        var tx = ParseTransaction(t);
                        if (tx is null) continue;
                        // Dedupe across overlapping window boundaries.
                        if (!string.IsNullOrEmpty(tx.TransactionId) && !seen.Add(tx.TransactionId)) continue;
                        results.Add(tx);
                    }
                }

                page++;
            }
            while (page <= totalPages);

            // Next window starts where this one ended (records are timestamped; dedupe handles the seam).
            windowStart = windowEnd == to ? to : windowEnd;
            if (windowEnd == to) break;
        }

        return results;
    }

    // --- HTTP plumbing ------------------------------------------------------

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, string? prefer, CancellationToken ct)
    {
        // One transparent retry if the cached token was rejected.
        for (var attempt = 0; ; attempt++)
        {
            var token = await _tokenProvider.GetAccessTokenAsync(FetchTokenAsync, ct);

            using var message = new HttpRequestMessage(method, BuildUri(path));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(requestId))
                message.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (!string.IsNullOrEmpty(prefer))
                message.Headers.TryAddWithoutValidation("Prefer", prefer);
            if (body is not null)
                message.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(message, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _tokenProvider.Invalidate();
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                ThrowForError(method, path, response.StatusCode, payload);
            }

            _logger.LogInformation($"PayPal {method} {path} -> {(int)response.StatusCode}");

            return string.IsNullOrWhiteSpace(payload) ? EmptyDocument() : JsonDocument.Parse(payload);
        }
    }

    private async Task<PayPalAccessToken> FetchTokenAsync(CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        message.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await _http.SendAsync(message, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new PaymentException($"PayPal authentication failed ({(int)response.StatusCode}).");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var accessToken = GetString(root, "access_token")
            ?? throw new PaymentException("PayPal authentication returned no access_token.");
        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 3000;
        return new PayPalAccessToken(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private void ThrowForError(HttpMethod method, string path, HttpStatusCode status, string payload)
    {
        string name = status.ToString();
        string message = status.ToString();
        string? issue = null;
        string? description = null;
        string? debugId = null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            name = GetString(root, "name") ?? name;
            message = GetString(root, "message") ?? message;
            debugId = GetString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
                && details.GetArrayLength() > 0)
            {
                var first = details[0];
                issue = GetString(first, "issue");
                description = GetString(first, "description");
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to status text.
        }

        // Log only safe metadata — never the request body / card data.
        _logger.LogWarning($"PayPal {method} {path} failed: {(int)status} {name}/{issue} debug_id={debugId}");

        var human = $"PayPal error ({(int)status} {name}"
            + (issue is null ? "" : $"/{issue}") + $"): {description ?? message}"
            + (debugId is null ? "" : $" [debug_id {debugId}]");

        if (IsBrowserChallengeError(name, issue))
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This browser-less integration cannot complete that. " + human);

        throw new PaymentException(human);
    }

    private Uri BuildUri(string path) => new(BaseUrl + path);

    // --- parsing helpers ----------------------------------------------------

    private static object BuildCardNode(PayPalCardDetails card)
    {
        object? billing = card.BillingAddress is null ? null : new
        {
            address_line_1 = card.BillingAddress.AddressLine1,
            address_line_2 = card.BillingAddress.AddressLine2,
            admin_area_2 = card.BillingAddress.AdminArea2,
            admin_area_1 = card.BillingAddress.AdminArea1,
            postal_code = card.BillingAddress.PostalCode,
            country_code = card.BillingAddress.CountryCode
        };

        return new
        {
            number = card.Number,
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            name = card.Name,
            billing_address = billing
        };
    }

    private static bool TryGetFirstAuthorization(JsonElement orderRoot, out JsonElement auth)
    {
        auth = default;
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array && auths.GetArrayLength() > 0)
            {
                auth = auths[0];
                return true;
            }
        }
        return false;
    }

    private static (string? brand, string? last4) ReadCardDescriptor(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            var brand = GetString(card, "brand");
            var last4 = GetString(card, "last_digits") ?? GetString(card, "last_4");
            return (brand, last4);
        }
        return (null, null);
    }

    private static (decimal? gross, decimal? fee, decimal? net, string currency) ReadReceivableBreakdown(
        JsonElement root, decimal fallbackAmount, string fallbackCurrency)
    {
        if (root.TryGetProperty("seller_receivable_breakdown", out var b))
        {
            var (gross, cur) = ReadMoney(b, "gross_amount");
            var (fee, _) = ReadMoney(b, "paypal_fee");
            var (net, _) = ReadMoney(b, "net_amount");
            return (gross, fee, net, cur ?? fallbackCurrency);
        }
        return (null, null, null, fallbackCurrency);
    }

    private static (decimal? value, string? currency) ReadMoney(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money))
        {
            var value = ParseDecimal(GetString(money, "value"));
            var currency = GetString(money, "currency_code");
            return (value, currency);
        }
        return (null, null);
    }

    private static PayPalTransaction? ParseTransaction(JsonElement detail)
    {
        if (!detail.TryGetProperty("transaction_info", out var info))
            return null;

        var id = GetString(info, "transaction_id") ?? string.Empty;
        var eventCode = GetString(info, "transaction_event_code");
        var status = GetString(info, "transaction_status");
        var (amount, currency) = ReadMoney(info, "transaction_amount");
        var (fee, _) = ReadMoney(info, "fee_amount");
        var date = ParseDate(GetString(info, "transaction_initiation_date"))
                   ?? ParseDate(GetString(info, "transaction_updated_date"));
        var custom = GetString(info, "custom_field");
        var invoice = GetString(info, "invoice_id");
        var reference = GetString(info, "paypal_reference_id");

        return new PayPalTransaction(id, eventCode, status, amount, fee, currency, date, custom, invoice, reference);
    }

    private void EnsureNoBrowserChallenge(JsonElement orderRoot)
    {
        var status = GetString(orderRoot, "status");
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (order status PAYER_ACTION_REQUIRED). " +
                "This browser-less integration cannot complete that step.");

        if (orderRoot.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(GetString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))
                    throw new PaymentChallengeRequiredException(
                        "PayPal returned a payer-action link requiring browser approval for this card payment. " +
                        "This browser-less integration cannot complete that step.");
            }
        }
    }

    private static bool IsBrowserChallengeError(string? name, string? issue) =>
        string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(issue, "PAYER_AUTHENTICATION_REQUIRED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(issue, "3DS_CHALLENGE_REQUIRED", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private static string Money(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture);

    private static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static JsonDocument EmptyDocument() => JsonDocument.Parse("{}");
}
