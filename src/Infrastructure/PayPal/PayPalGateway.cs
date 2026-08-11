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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The concrete PayPal REST integration. Every method maps to documented PayPal endpoints
/// (Orders v2, Payments v2, Vault v3, Transaction Search v1). It manages the OAuth token,
/// idempotency headers, and error translation. Card data and secrets are never logged.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const int MaxTransactionWindowDays = 31;
    private const int TransactionPageSize = 500;

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    // ---------------------------------------------------------------------
    // Authorization (hold)
    // ---------------------------------------------------------------------
    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeOrderCommand command, CancellationToken cancellationToken = default)
    {
        // 1) Create a PayPal order with intent=AUTHORIZE. custom_id carries the eShop order id so
        //    the transaction can be reconciled later; invoice_id is unique per merchant account.
        var createBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"order-{command.OrderId}",
                    custom_id = command.OrderId.ToString(CultureInfo.InvariantCulture),
                    invoice_id = $"eshop-{command.IdempotencyKey}",
                    amount = Money(command.Amount, command.Currency)
                }
            }
        };

        using var createResp = await SendAsync(
            HttpMethod.Post, "/v2/checkout/orders", createBody,
            idempotencyKey: $"{command.IdempotencyKey}-create", cancellationToken: cancellationToken);
        var createDoc = await ReadJsonAsync(createResp, cancellationToken);
        var payPalOrderId = createDoc.RootElement.GetProperty("id").GetString()!;

        // 2) Authorize the order with the funding instrument (raw card or a vaulted card).
        var authBody = new { payment_source = new { card = BuildCardSource(command.Instrument) } };

        using var authResp = await SendAsync(
            HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", authBody,
            idempotencyKey: $"{command.IdempotencyKey}-authorize", cancellationToken: cancellationToken);
        var authDoc = await ReadJsonAsync(authResp, cancellationToken);

        EnsureNoBrowserChallenge(authDoc.RootElement, "authorizing the card");

        var authorization = FindFirstPayment(authDoc.RootElement, "authorizations")
            ?? throw new PaymentException(
                "PayPal did not return an authorization for the card payment. The hold could not be placed.");

        var authorizationId = authorization.GetProperty("id").GetString()!;
        var status = GetStringOrNull(authorization, "status") ?? "UNKNOWN";
        DateTimeOffset? expiresAt = TryGetDateTime(authorization, "expiration_time");

        _logger.LogInformation(
            "PayPal authorization {AuthorizationId} created for order {OrderId} with status {Status}.",
            authorizationId, command.OrderId, status);

        return new AuthorizationResult(payPalOrderId, authorizationId, status, expiresAt);
    }

    // ---------------------------------------------------------------------
    // Capture (take the money)
    // ---------------------------------------------------------------------
    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { amount = Money(amount, currency), final_capture = true };

        HttpResponseMessage resp;
        try
        {
            resp = await SendAsync(
                HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body,
                idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.IndicatesExpiredAuthorization)
        {
            throw new PayPalAuthorizationExpiredException(ex.Message);
        }

        using (resp)
        {
            var doc = await ReadJsonAsync(resp, cancellationToken);
            var root = doc.RootElement;

            var captureId = root.GetProperty("id").GetString()!;
            var status = GetStringOrNull(root, "status") ?? "UNKNOWN";
            var gross = ParseMoney(root, "amount") ?? amount;

            decimal? fee = null;
            decimal? net = null;
            if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
            {
                fee = ParseMoney(breakdown, "paypal_fee");
                net = ParseMoney(breakdown, "net_amount");
                gross = ParseMoney(breakdown, "gross_amount") ?? gross;
            }

            _logger.LogInformation(
                "PayPal capture {CaptureId} for authorization {AuthorizationId} status {Status}, fee {Fee}, net {Net}.",
                captureId, authorizationId, status, fee, net);

            return new CaptureResult(captureId, status, gross, fee, net, currency);
        }
    }

    // ---------------------------------------------------------------------
    // Reauthorize (renew a stale hold)
    // ---------------------------------------------------------------------
    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { amount = Money(amount, currency) };

        using var resp = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
        var doc = await ReadJsonAsync(resp, cancellationToken);
        var root = doc.RootElement;

        var newAuthId = GetStringOrNull(root, "id") ?? authorizationId;
        var status = GetStringOrNull(root, "status") ?? "UNKNOWN";
        DateTimeOffset? expiresAt = TryGetDateTime(root, "expiration_time");

        _logger.LogInformation(
            "PayPal reauthorization produced authorization {AuthorizationId} status {Status}.", newAuthId, status);

        // Reauthorization does not return a PayPal order id; the existing one on the payment stands.
        return new AuthorizationResult(string.Empty, newAuthId, status, expiresAt);
    }

    // ---------------------------------------------------------------------
    // Void (release the hold)
    // ---------------------------------------------------------------------
    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var resp = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", body: null,
            idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
        _logger.LogInformation("PayPal authorization {AuthorizationId} voided.", authorizationId);
    }

    // ---------------------------------------------------------------------
    // Refund
    // ---------------------------------------------------------------------
    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Full refund => empty body; partial refund => amount object.
        object? body = amount.HasValue ? new { amount = Money(amount.Value, currency) } : null;

        using var resp = await SendAsync(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body,
            idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
        var doc = await ReadJsonAsync(resp, cancellationToken);
        var root = doc.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = GetStringOrNull(root, "status") ?? "UNKNOWN";
        var refundedAmount = ParseMoney(root, "amount") ?? amount ?? 0m;

        _logger.LogInformation(
            "PayPal refund {RefundId} for capture {CaptureId} status {Status} amount {Amount}.",
            refundId, captureId, status, refundedAmount);

        return new RefundResult(refundId, status, refundedAmount, currency);
    }

    // ---------------------------------------------------------------------
    // Vault a card
    // ---------------------------------------------------------------------
    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string customerReference, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            payment_source = new { card = BuildCardVaultSource(card) },
            customer = new { merchant_customer_id = customerReference }
        };

        using var resp = await SendAsync(
            HttpMethod.Post, "/v3/vault/payment-tokens", body,
            idempotencyKey: Guid.NewGuid().ToString(), cancellationToken: cancellationToken);
        var doc = await ReadJsonAsync(resp, cancellationToken);
        var root = doc.RootElement;

        EnsureNoBrowserChallenge(root, "saving the card");

        var vaultId = root.GetProperty("id").GetString()!;
        string? last4 = null, brand = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var respCard))
        {
            last4 = GetStringOrNull(respCard, "last_digits");
            brand = GetStringOrNull(respCard, "brand");
            expiry = GetStringOrNull(respCard, "expiry");
            name = GetStringOrNull(respCard, "name");
        }

        _logger.LogInformation("Vaulted card token {VaultId} ({Brand} ****{Last4}).", vaultId, brand, last4);
        return new VaultCardResult(vaultId, last4, brand, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var resp = await SendAsync(
            HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", body: null,
            idempotencyKey: null, cancellationToken: cancellationToken);
        _logger.LogInformation("Deleted vaulted card token {VaultId}.", vaultId);
    }

    // ---------------------------------------------------------------------
    // Transaction search (reconciliation) — covers the whole range
    // ---------------------------------------------------------------------
    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var results = new List<PayPalTransaction>();

        // Transaction Search caps each request at a 31-day window; chunk the range accordingly.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(MaxTransactionWindowDays);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await ReadTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ReadTransactionWindowAsync(DateTimeOffset start, DateTimeOffset end, List<PayPalTransaction> sink, CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var path = $"/v1/reporting/transactions?start_date={Rfc3339(start)}&end_date={Rfc3339(end)}" +
                       $"&fields=transaction_info&page_size={TransactionPageSize}&page={page}";

            using var resp = await SendAsync(HttpMethod.Get, path, body: null, idempotencyKey: null, cancellationToken: cancellationToken);
            var doc = await ReadJsonAsync(resp, cancellationToken);
            var root = doc.RootElement;

            totalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : 1;

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    sink.Add(new PayPalTransaction(
                        TransactionId: GetStringOrNull(info, "transaction_id") ?? string.Empty,
                        Status: GetStringOrNull(info, "transaction_status"),
                        Amount: ParseMoney(info, "transaction_amount"),
                        Currency: ParseMoneyCurrency(info, "transaction_amount"),
                        InitiatedAt: TryGetDateTime(info, "transaction_initiation_date"),
                        InvoiceId: GetStringOrNull(info, "invoice_id"),
                        CustomField: GetStringOrNull(info, "custom_field"),
                        EventCode: GetStringOrNull(info, "transaction_event_code")));
                }
            }

            page++;
        }
        while (page <= totalPages && !cancellationToken.IsCancellationRequested);
    }

    // ---------------------------------------------------------------------
    // Request/response plumbing
    // ---------------------------------------------------------------------
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken cancellationToken, bool isRetryAfter401 = false)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isRetryAfter401)
        {
            // Token may have expired mid-flight; refresh once and retry.
            response.Dispose();
            InvalidateToken();
            return await SendAsync(method, path, body, idempotencyKey, cancellationToken, isRetryAfter401: true);
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, path, cancellationToken);
        }

        return response;
    }

    private async Task ThrowApiExceptionAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Dispose();

        string name = "UNKNOWN", message = response.ReasonPhrase ?? "PayPal request failed", debugId = "", issue = "", description = "";
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            name = GetStringOrNull(root, "name") ?? name;
            message = GetStringOrNull(root, "message") ?? message;
            debugId = GetStringOrNull(root, "debug_id") ?? "";
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                var first = details[0];
                issue = GetStringOrNull(first, "issue") ?? "";
                description = GetStringOrNull(first, "description") ?? "";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the reason phrase.
        }

        var summary = $"PayPal call to {path} failed with {(int)response.StatusCode} {name}" +
                      (string.IsNullOrEmpty(issue) ? "" : $" ({issue})") +
                      $": {(string.IsNullOrEmpty(description) ? message : description)}" +
                      (string.IsNullOrEmpty(debugId) ? "" : $" [debug_id={debugId}]");

        // debug_id is safe to log/surface; card data is never part of an error body we emit.
        _logger.LogError("{Summary}", summary);

        throw new PayPalApiException(summary, response.StatusCode, name, issue);
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

            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("PayPal token request failed with {Status}.", (int)response.StatusCode);
                throw new PaymentException(
                    $"Could not obtain a PayPal access token ({(int)response.StatusCode}). " +
                    "Check the PayPal:ClientId / PayPal:ClientSecret / PayPal:Environment configuration.");
            }

            using var doc = await ReadJsonAsync(response, cancellationToken);
            var root = doc.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3200;
            // Refresh a minute early to avoid using a token that expires mid-request.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _accessToken = null;
        _tokenExpiresAt = DateTimeOffset.MinValue;
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------
    private object BuildCardSource(PaymentInstrument instrument)
    {
        if (!string.IsNullOrEmpty(instrument.VaultId))
        {
            return new { vault_id = instrument.VaultId };
        }

        var card = instrument.Card
            ?? throw new PaymentException("A payment instrument must supply either card details or a saved card id.");

        return new
        {
            number = card.Number,
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            name = card.Name,
            billing_address = BuildBillingAddress(card.BillingAddress)
        };
    }

    private object BuildCardVaultSource(CardDetails card)
    {
        return new
        {
            number = card.Number,
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            name = card.Name,
            billing_address = BuildBillingAddress(card.BillingAddress)
        };
    }

    private static object? BuildBillingAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }
        return new
        {
            address_line_1 = address.AddressLine1,
            address_line_2 = address.AddressLine2,
            admin_area_2 = address.AdminArea2,
            admin_area_1 = address.AdminArea1,
            postal_code = address.PostalCode,
            country_code = address.CountryCode
        };
    }

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (stream.Length == 0)
        {
            return JsonDocument.Parse("{}");
        }
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static JsonElement? FindFirstPayment(JsonElement orderRoot, string collectionName)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty(collectionName, out var coll) &&
                coll.ValueKind == JsonValueKind.Array && coll.GetArrayLength() > 0)
            {
                return coll[0];
            }
        }
        return null;
    }

    private void EnsureNoBrowserChallenge(JsonElement root, string action)
    {
        var status = GetStringOrNull(root, "status");
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalChallengeRequiredException(
                $"PayPal requires the shopper to approve {action} in a browser (status PAYER_ACTION_REQUIRED). " +
                "This browserless integration cannot complete a challenge round-trip.");
        }

        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = GetStringOrNull(link, "rel");
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rel, "approve", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PayPalChallengeRequiredException(
                        $"PayPal returned a '{rel}' link requiring browser approval while {action}. " +
                        "This browserless integration cannot complete a challenge round-trip.");
                }
            }
        }
    }

    private static string? GetStringOrNull(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? ParseMoney(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money) &&
            money.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static string? ParseMoneyCurrency(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var money) ? GetStringOrNull(money, "currency_code") : null;

    private static DateTimeOffset? TryGetDateTime(JsonElement element, string property)
    {
        var raw = GetStringOrNull(element, property);
        if (raw is not null && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static string Rfc3339(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}
