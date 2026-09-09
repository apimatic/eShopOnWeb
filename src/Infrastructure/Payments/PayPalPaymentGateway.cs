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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Talks to PayPal directly over HTTP, built to the OpenAPI specs in <c>api-specs/paypal</c>:
/// checkout orders v2 (create + authorize), payments v2 (capture / reauthorize / void / refund),
/// vault payment tokens v3 (save / delete a card), and transaction search v1 (reconciliation).
/// No third-party SDK is used. Full card details are never logged.
/// </summary>
public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    // PayPal caps a single transaction-search window at 31 days.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IPayPalAccessTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(HttpClient httpClient, IPayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalSettings> settings, ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(Money amount, CardDetails? card, string? vaultTokenId,
        string invoiceId, string customId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (card is null && string.IsNullOrEmpty(vaultTokenId))
            throw new ArgumentException("Either card details or a saved-card vault token id must be supplied.");

        object cardSource = card is not null
            ? new
            {
                number = card.Number,
                expiry = card.Expiry,
                security_code = card.SecurityCode,
                name = card.Name,
                billing_address = ToBillingAddress(card.BillingAddress)
            }
            : new { vault_id = vaultTokenId };

        var orderRequest = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = invoiceId,
                    custom_id = customId,
                    amount = ToAmount(amount)
                }
            },
            payment_source = new { card = cardSource }
        };

        // Create the checkout order (checkout_orders_v2: POST /v2/checkout/orders). PayPal-Request-Id
        // makes the single-step card create idempotent; Prefer=representation returns the full resource.
        using var createDoc = await SendAsync(HttpMethod.Post, "v2/checkout/orders", orderRequest,
            requestId: idempotencyKey + ".order", representation: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var orderId = createDoc.RootElement.GetProperty("id").GetString()!;
        var createStatus = GetString(createDoc.RootElement, "status");
        EnsureNoApprovalRequired(createStatus, orderId);

        // A single-step create with a direct card payment source (intent=AUTHORIZE) makes PayPal
        // process the card and create the authorization inline — so the hold is already present in the
        // create response. Only when it is absent (e.g. an approval-style flow) do we call the separate
        // authorize endpoint (checkout_orders_v2: POST /v2/checkout/orders/{id}/authorize).
        var authorization = FindFirstAuthorization(createDoc.RootElement)?.Clone();
        if (authorization is null)
        {
            try
            {
                using var authDoc = await SendAsync(HttpMethod.Post, $"v2/checkout/orders/{orderId}/authorize",
                    body: new { }, requestId: idempotencyKey + ".auth", representation: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                EnsureNoApprovalRequired(GetString(authDoc.RootElement, "status"), orderId);
                authorization = FindFirstAuthorization(authDoc.RootElement)?.Clone();
            }
            catch (PayPalApiException ex) when (ex.RawBody is not null
                && ex.RawBody.Contains("ORDER_ALREADY_AUTHORIZED", StringComparison.OrdinalIgnoreCase))
            {
                // The order was already authorized (e.g. an idempotent replay of a single-step card
                // create): read the existing hold back from the order.
                authorization = await GetOrderAuthorizationAsync(orderId, cancellationToken).ConfigureAwait(false);
            }
        }

        if (authorization is null)
        {
            throw new PayPalApiException(
                "PayPal did not return an authorization for the order; the card may have been declined.",
                (int)HttpStatusCode.BadGateway, null, createDoc.RootElement.GetRawText());
        }

        var auth = authorization.Value;

        var authId = auth.GetProperty("id").GetString()!;
        var authStatus = GetString(auth, "status") ?? "CREATED";
        if (string.Equals(authStatus, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalApiException(
                $"PayPal denied the authorization for order {orderId}.",
                (int)HttpStatusCode.PaymentRequired, "AUTHORIZATION_DENIED", auth.GetRawText());
        }

        var expiresAt = GetDateTime(auth, "expiration_time");
        _logger.LogInformation("Authorized PayPal order {OrderId} -> authorization {AuthId} ({Status}).",
            orderId, authId, authStatus);
        return new AuthorizationResult(orderId, authId, authStatus, expiresAt);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, Money amount, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = ToAmount(amount),
            invoice_id = invoiceId,
            final_capture = true
        };

        using var doc = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture", body,
            requestId: idempotencyKey, representation: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = doc.RootElement;
        var captureId = root.GetProperty("id").GetString()!;
        var status = GetString(root, "status") ?? "COMPLETED";

        decimal gross = amount.Amount;
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoneyValue(breakdown, "gross_amount") ?? gross;
            fee = ReadMoneyValue(breakdown, "paypal_fee");
            net = ReadMoneyValue(breakdown, "net_amount");
        }

        _logger.LogInformation("Captured authorization {AuthId} -> capture {CaptureId} ({Status}).",
            authorizationId, captureId, status);
        return new CaptureResult(captureId, status, gross, fee, net);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, Money amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { amount = ToAmount(amount) };
        try
        {
            using var doc = await SendAsync(HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/reauthorize", body,
                requestId: idempotencyKey, representation: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var root = doc.RootElement;
            var newAuthId = root.GetProperty("id").GetString()!;
            var status = GetString(root, "status") ?? "CREATED";
            var expiresAt = GetDateTime(root, "expiration_time");
            _logger.LogInformation("Reauthorized {OldAuthId} -> {NewAuthId} ({Status}).",
                authorizationId, newAuthId, status);
            return new AuthorizationResult(string.Empty, newAuthId, status, expiresAt);
        }
        catch (PayPalApiException ex)
        {
            // The authorization can no longer be renewed (past PayPal's reauthorization window, or
            // the instrument no longer supports it). Surface it in operator-actionable terms.
            throw new AuthorizationNotRenewableException(
                $"The payment authorization can no longer be renewed, so this order cannot be fulfilled. " +
                $"PayPal reported: {ex.PayPalName ?? "error"} - {ex.Message}. " +
                $"The shopper must place and pay for a new order.");
        }
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void", body: null,
            requestId: null, representation: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Voided authorization {AuthId}.", authorizationId);
    }

    public async Task<RefundResult> RefundAsync(string captureId, Money? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // Full refund => no body; partial refund => amount present (refund_request in payments_payment_v2).
        object? body = amount is null ? null : new { amount = ToAmount(amount) };

        using var doc = await SendAsync(HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund", body,
            requestId: idempotencyKey, representation: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = doc.RootElement;
        var refundId = root.GetProperty("id").GetString()!;
        var status = GetString(root, "status") ?? "COMPLETED";
        var refundedAmount = ReadMoneyValue(root, "amount") ?? amount?.Amount ?? 0m;

        _logger.LogInformation("Refunded capture {CaptureId} -> refund {RefundId} ({Status}).",
            captureId, refundId, status);
        return new RefundResult(refundId, status, refundedAmount);
    }

    public async Task<VaultResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.Name,
                    billing_address = ToBillingAddress(card.BillingAddress)
                }
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", body,
            requestId: Guid.NewGuid().ToString("N"), representation: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = doc.RootElement;
        var tokenId = root.GetProperty("id").GetString()!;

        string? brand = null, last4 = null, expiry = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var respCard))
        {
            brand = GetString(respCard, "brand");
            last4 = GetString(respCard, "last_digits");
            expiry = GetString(respCard, "expiry");
        }

        _logger.LogInformation("Vaulted a card -> token {TokenId} ({Brand} ****{Last4}).",
            tokenId, brand, last4);
        return new VaultResult(tokenId, brand, last4, expiry, card.Name);
    }

    public async Task DeleteVaultTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        using var request = await BuildRequestAsync(HttpMethod.Delete,
            $"v3/vault/payment-tokens/{vaultTokenId}", body: null, requestId: null, representation: false,
            cancellationToken).ConfigureAwait(false);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw BuildApiException(response, body, HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultTokenId}");
        }
        _logger.LogInformation("Deleted vault token {TokenId}.", vaultTokenId);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        if (to < from)
        {
            return results;
        }

        // Walk the range in <=31-day windows so the whole span is covered, not just the first window.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await SearchWindowAsync(windowStart, windowEnd, results, cancellationToken).ConfigureAwait(false);

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task SearchWindowAsync(DateTimeOffset from, DateTimeOffset to,
        List<PayPalTransaction> results, CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query =
                $"v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatRfc3339(from))}" +
                $"&end_date={Uri.EscapeDataString(FormatRfc3339(to))}" +
                $"&fields=transaction_info&page_size={SearchPageSize}&page={page}";

            using var doc = await SendAsync(HttpMethod.Get, query, body: null, requestId: null,
                representation: false, cancellationToken: cancellationToken).ConfigureAwait(false);

            var root = doc.RootElement;
            totalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : 1;

            if (root.TryGetProperty("transaction_details", out var details)
                && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    results.Add(new PayPalTransaction(
                        TransactionId: GetString(info, "transaction_id") ?? string.Empty,
                        Status: GetString(info, "transaction_status"),
                        Amount: ReadMoneyValue(info, "transaction_amount"),
                        Currency: ReadMoneyCurrency(info, "transaction_amount"),
                        Date: GetDateTime(info, "transaction_initiation_date"),
                        InvoiceId: GetString(info, "invoice_id"),
                        CustomField: GetString(info, "custom_field")));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ---- HTTP plumbing ----

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, bool representation, CancellationToken cancellationToken)
    {
        using var request = await BuildRequestAsync(method, path, body, requestId, representation, cancellationToken)
            .ConfigureAwait(false);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(response, responseBody, method, path);
        }

        // Some success responses (e.g. void, delete) carry no body.
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(responseBody);
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string path, object? body,
        string? requestId, bool representation, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (!string.IsNullOrEmpty(requestId))
        {
            // PayPal-Request-Id is capped at 108 chars by the spec.
            var id = requestId.Length > 108 ? requestId[..108] : requestId;
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", id);
        }

        if (representation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private PayPalApiException BuildApiException(HttpResponseMessage response, string body,
        HttpMethod method, string path)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        var issues = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            debugId = GetString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    var issue = GetString(d, "issue");
                    var desc = GetString(d, "description");
                    if (issue is not null || desc is not null)
                    {
                        issues.Add($"{issue}: {desc}");
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to the status line.
        }

        var detail = issues.Count > 0 ? $" ({string.Join("; ", issues)})" : string.Empty;
        var summary = $"PayPal {method} {path} failed with {(int)response.StatusCode}: " +
                      $"{name ?? "error"} - {message ?? response.ReasonPhrase}{detail}";
        _logger.LogError("{Summary} [debug_id={DebugId}]", summary, debugId);
        return new PayPalApiException(summary, (int)response.StatusCode, name, body);
    }

    private static void EnsureNoApprovalRequired(string? status, string orderId)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApprovalRequiredException(
                $"PayPal requires the shopper to approve the payment in a browser for order {orderId}. " +
                $"This flow does not support a browser approval round-trip.");
        }
    }

    private async Task<JsonElement?> GetOrderAuthorizationAsync(string orderId, CancellationToken cancellationToken)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"v2/checkout/orders/{orderId}", body: null,
            requestId: null, representation: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        // Clone so the value survives disposal of the JsonDocument.
        return FindFirstAuthorization(doc.RootElement)?.Clone();
    }

    private static JsonElement? FindFirstAuthorization(JsonElement root)
    {
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                {
                    return auth;
                }
            }
        }

        return null;
    }

    private object ToAmount(Money money) => new
    {
        currency_code = money.Currency,
        value = money.Amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static object? ToBillingAddress(BillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new
        {
            address_line_1 = address.AddressLine1,
            admin_area_2 = address.AdminArea2,
            admin_area_1 = address.AdminArea1,
            postal_code = address.PostalCode,
            country_code = address.CountryCode
        };
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? ReadMoneyValue(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money)
            && money.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? ReadMoneyCurrency(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var money) ? GetString(money, "currency_code") : null;

    private static DateTimeOffset? GetDateTime(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        if (raw is not null && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
