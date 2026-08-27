using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Plain-HTTP client for the PayPal REST APIs used by this integration:
/// Orders v2 (authorize), Payments v2 (capture/reauthorize/void/refund),
/// Payment Method Tokens v3 (vault) and Transaction Search v1 (reporting).
/// Request bodies that carry card data are never logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // PayPal transaction search supports a maximum range of 31 days.
    private static readonly TimeSpan MaxReportingRange = TimeSpan.FromDays(30);

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(settings.GetBaseUrl() + "/");
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        string referenceId, string invoiceId, decimal amount, string currency,
        PayPalCardDetails? card, string? vaultTokenId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var createOrderBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = referenceId,
                    invoice_id = invoiceId,
                    amount = new { currency_code = currency, value = FormatMoney(amount) }
                }
            }
        };

        using var orderDoc = await SendAsync(HttpMethod.Post, "v2/checkout/orders",
            createOrderBody, idempotencyKey + "-order", cancellationToken);
        var orderId = orderDoc.RootElement.GetProperty("id").GetString()!;
        var orderStatus = orderDoc.RootElement.GetProperty("status").GetString();
        ThrowIfPayerActionRequired(orderStatus);

        object paymentSource = card is not null
            ? new { payment_source = new { card = BuildCardPayload(card) } }
            : new { payment_source = new { card = new { vault_id = vaultTokenId } } };

        using var authDoc = await SendAsync(HttpMethod.Post, $"v2/checkout/orders/{orderId}/authorize",
            paymentSource, idempotencyKey + "-auth", cancellationToken);

        var status = authDoc.RootElement.GetProperty("status").GetString();
        ThrowIfPayerActionRequired(status);

        var authorization = authDoc.RootElement
            .GetProperty("purchase_units")[0]
            .GetProperty("payments")
            .GetProperty("authorizations")[0];

        var authStatus = authorization.GetProperty("status").GetString()!;
        if (!string.Equals(authStatus, "CREATED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(authStatus, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDeclinedException($"PayPal did not authorize the payment; authorization status is {authStatus}.");
        }

        return new PayPalAuthorizationResult(
            orderId,
            authorization.GetProperty("id").GetString()!,
            authStatus,
            ParseMoney(authorization.GetProperty("amount").GetProperty("value").GetString()!),
            authorization.GetProperty("amount").GetProperty("currency_code").GetString()!,
            authorization.TryGetProperty("create_time", out var ct) ? ct.GetDateTimeOffset() : DateTimeOffset.UtcNow);
    }

    public async Task<PayPalAuthorizationInfo> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}",
            null, null, cancellationToken);
        return ParseAuthorizationInfo(doc.RootElement);
    }

    public async Task<PayPalAuthorizationInfo> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currency, value = FormatMoney(amount) } };
        using var doc = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, cancellationToken);
        return ParseAuthorizationInfo(doc.RootElement);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { final_capture = true };
        using var doc = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, cancellationToken);

        var root = doc.RootElement;
        var captureId = root.GetProperty("id").GetString()!;
        if (root.TryGetProperty("seller_receivable_breakdown", out _))
        {
            return ParseCapture(root);
        }

        // The capture response can be minimal; fetch the full capture record
        // for the amounts and PayPal's fee breakdown.
        return await GetCaptureAsync(captureId, cancellationToken);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(
        string captureId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"v2/payments/captures/{captureId}",
            null, null, cancellationToken);
        return ParseCapture(doc.RootElement);
    }

    public async Task<string?> GetCapturedIdForOrderAsync(
        string payPalOrderId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"v2/checkout/orders/{payPalOrderId}",
            null, null, cancellationToken);
        foreach (var unit in doc.RootElement.GetProperty("purchase_units").EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("captures", out var captures)
                && captures.GetArrayLength() > 0)
            {
                return captures[0].GetProperty("id").GetString();
            }
        }
        return null;
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root)
    {
        var breakdown = root.GetProperty("seller_receivable_breakdown");
        return new PayPalCaptureResult(
            root.GetProperty("id").GetString()!,
            root.GetProperty("status").GetString()!,
            ParseMoney(breakdown.GetProperty("gross_amount").GetProperty("value").GetString()!),
            ParseMoney(breakdown.GetProperty("paypal_fee").GetProperty("value").GetString()!),
            ParseMoney(breakdown.GetProperty("net_amount").GetProperty("value").GetString()!),
            breakdown.GetProperty("gross_amount").GetProperty("currency_code").GetString()!);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void",
            null, null, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue
            ? new { amount = new { currency_code = currency, value = FormatMoney(amount.Value) } }
            : null;

        using var doc = await SendAsync(HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund", body, idempotencyKey, cancellationToken);

        var root = doc.RootElement;
        if (!root.TryGetProperty("amount", out _))
        {
            // The refund response can be minimal; fetch the full refund record.
            var refundId = root.GetProperty("id").GetString()!;
            using var refundDoc = await SendAsync(HttpMethod.Get, $"v2/payments/refunds/{refundId}",
                null, null, cancellationToken);
            root = refundDoc.RootElement.Clone();
        }

        return new PayPalRefundResult(
            root.GetProperty("id").GetString()!,
            root.TryGetProperty("status", out var st) ? st.GetString()! : "COMPLETED",
            ParseMoney(root.GetProperty("amount").GetProperty("value").GetString()!),
            root.GetProperty("amount").GetProperty("currency_code").GetString()!);
    }

    public async Task<PayPalVaultedCardResult> VaultCardAsync(
        PayPalCardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var setupBody = new { payment_source = new { card = BuildCardPayload(card) } };
        using var setupDoc = await SendAsync(HttpMethod.Post, "v3/vault/setup-tokens",
            setupBody, idempotencyKey + "-setup", cancellationToken);

        var setupStatus = setupDoc.RootElement.TryGetProperty("status", out var ss) ? ss.GetString() : null;
        ThrowIfPayerActionRequired(setupStatus);
        var setupTokenId = setupDoc.RootElement.GetProperty("id").GetString()!;

        var tokenBody = new
        {
            payment_source = new
            {
                token = new { id = setupTokenId, type = "SETUP_TOKEN" }
            }
        };
        using var tokenDoc = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens",
            tokenBody, idempotencyKey + "-token", cancellationToken);

        var root = tokenDoc.RootElement;
        var cardElement = root.GetProperty("payment_source").GetProperty("card");
        return new PayPalVaultedCardResult(
            root.GetProperty("id").GetString()!,
            root.GetProperty("customer").GetProperty("id").GetString()!,
            cardElement.TryGetProperty("brand", out var brand) ? brand.GetString() : null,
            cardElement.TryGetProperty("last_digits", out var lastDigits) ? lastDigits.GetString() : null,
            cardElement.TryGetProperty("expiry", out var expiry) ? expiry.GetString() : null,
            cardElement.TryGetProperty("name", out var name) ? name.GetString() : null);
    }

    public async Task DeleteVaultedCardAsync(
        string vaultTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultTokenId}",
                null, null, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // Already gone from PayPal's vault.
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = new List<PayPalTransaction>();

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportingRange < to ? windowStart + MaxReportingRange : to;

            var page = 1;
            var totalPages = 1;
            while (page <= totalPages)
            {
                var path = "v1/reporting/transactions"
                    + $"?start_date={Uri.EscapeDataString(FormatReportingDate(windowStart))}"
                    + $"&end_date={Uri.EscapeDataString(FormatReportingDate(windowEnd))}"
                    + "&fields=all&page_size=500&page=" + page;

                using var doc = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var root = doc.RootElement;

                if (root.TryGetProperty("total_pages", out var tp))
                {
                    totalPages = tp.GetInt32();
                }

                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var info = detail.GetProperty("transaction_info");
                        transactions.Add(new PayPalTransaction(
                            info.GetProperty("transaction_id").GetString()!,
                            info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null,
                            info.TryGetProperty("transaction_status", out var st) ? st.GetString() : null,
                            info.TryGetProperty("transaction_amount", out var amount)
                                ? ParseMoney(amount.GetProperty("value").GetString()!)
                                : null,
                            info.TryGetProperty("transaction_amount", out var amount2)
                                ? amount2.GetProperty("currency_code").GetString()
                                : null,
                            info.TryGetProperty("transaction_initiation_date", out var date)
                                ? date.GetDateTimeOffset()
                                : null));
                    }
                }

                page++;
            }

            windowStart = windowEnd;
        }

        return transactions;
    }

    private static object BuildCardPayload(PayPalCardDetails card)
    {
        return new
        {
            number = card.Number,
            expiry = $"{card.ExpiryYear:D4}-{card.ExpiryMonth:D2}",
            security_code = card.SecurityCode,
            name = card.CardholderName,
            billing_address = card.BillingAddress is null ? null : new
            {
                address_line_1 = card.BillingAddress.AddressLine1,
                admin_area_2 = card.BillingAddress.AdminArea2,
                admin_area_1 = card.BillingAddress.AdminArea1,
                postal_code = card.BillingAddress.PostalCode,
                country_code = card.BillingAddress.CountryCode
            }
        };
    }

    private static PayPalAuthorizationInfo ParseAuthorizationInfo(JsonElement element)
    {
        return new PayPalAuthorizationInfo(
            element.GetProperty("id").GetString()!,
            element.GetProperty("status").GetString()!,
            ParseMoney(element.GetProperty("amount").GetProperty("value").GetString()!),
            element.GetProperty("amount").GetProperty("currency_code").GetString()!,
            element.TryGetProperty("create_time", out var ct) ? ct.GetDateTimeOffset() : null);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return JsonDocument.Parse("{}");
        }
        return JsonDocument.Parse(content);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToException(response.StatusCode, content);
            }

            using var doc = JsonDocument.Parse(content);
            _accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private PaymentGatewayException ToException(HttpStatusCode statusCode, string content)
    {
        string? name = null;
        string? issue = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
            message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            if (doc.RootElement.TryGetProperty("details", out var details) && details.GetArrayLength() > 0)
            {
                issue = details[0].TryGetProperty("issue", out var i) ? i.GetString() : null;
                var description = details[0].TryGetProperty("description", out var d) ? d.GetString() : null;
                if (!string.IsNullOrEmpty(description))
                {
                    message = description;
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with what we have.
        }

        _logger.LogWarning("PayPal API error {StatusCode} {Name} {Issue}: {Message}",
            (int)statusCode, name, issue, message);

        var fullMessage = $"PayPal API error {(int)statusCode} ({name ?? "unknown"}"
            + (issue is not null ? $"/{issue}" : string.Empty) + $"): {message ?? "no details"}";

        if (issue is not null && issue.Contains("DECLINED", StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentDeclinedException(fullMessage, name, issue);
        }
        return new PaymentGatewayException(fullMessage, name, issue) { HttpStatusCode = statusCode };
    }

    private static void ThrowIfPayerActionRequired(string? status)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this payment in a browser, which this integration does not support.");
        }
    }

    private static string FormatMoney(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string value) =>
        decimal.Parse(value, CultureInfo.InvariantCulture);

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
