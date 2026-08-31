using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal REST client covering OAuth, Orders v2, Payments v2, Vault v3 and
/// Transaction Search v1. Request and response bodies are never logged because
/// some of them carry full card details; only ids, statuses and PayPal debug
/// ids are logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    private string BaseUrl => _settings.GetBaseUrl();

    public async Task<string> CreateOrderAsync(decimal amount, string currency, string customId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new[]
            {
                new
                {
                    ReferenceId = customId,
                    CustomId = customId,
                    InvoiceId = invoiceId,
                    Amount = new { CurrencyCode = currency, Value = FormatMoney(amount) }
                }
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, cancellationToken);
        var id = doc.RootElement.GetProperty("id").GetString();
        _logger.LogInformation("PayPal order {PayPalOrderId} created (custom id {CustomId})", id, customId);
        return id!;
    }

    public Task<PayPalAuthorization> AuthorizeOrderWithCardAsync(string payPalOrderId, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            PaymentSource = new
            {
                Card = new
                {
                    card.Name,
                    card.Number,
                    card.Expiry,
                    card.SecurityCode,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        return AuthorizeOrderAsync(payPalOrderId, body, idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorization> AuthorizeOrderWithVaultedCardAsync(string payPalOrderId, string vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            PaymentSource = new
            {
                Card = new
                {
                    VaultId = vaultTokenId,
                    StoredCredential = new
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "ONE_TIME"
                    }
                }
            }
        };

        return AuthorizeOrderAsync(payPalOrderId, body, idempotencyKey, cancellationToken);
    }

    private async Task<PayPalAuthorization> AuthorizeOrderAsync(string payPalOrderId, object body, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", body, idempotencyKey, cancellationToken);
        var root = doc.RootElement;

        var orderStatus = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || HasPayerActionLink(root))
        {
            throw new PaymentActionRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This integration is API-only and does not implement an approval round-trip.");
        }

        if (!TryGetFirstAuthorization(root, out var authorization))
        {
            throw new PaymentProcessingException(
                $"PayPal did not return an authorization for order {payPalOrderId} (order status: {orderStatus ?? "unknown"}).");
        }

        var result = MapAuthorization(authorization);
        result.PayPalOrderId = payPalOrderId;
        _logger.LogInformation("PayPal order {PayPalOrderId} authorized: authorization {AuthorizationId} status {Status}",
            payPalOrderId, result.AuthorizationId, result.Status);
        return result;
    }

    public async Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return MapAuthorization(doc.RootElement);
    }

    public async Task<PayPalCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            Amount = new { CurrencyCode = currency, Value = FormatMoney(amount) },
            FinalCapture = true
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, cancellationToken);
        var root = doc.RootElement;

        var capture = new PayPalCapture
        {
            CaptureId = root.GetProperty("id").GetString()!,
            Status = root.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty,
            Amount = ReadMoney(root, "amount", out var ccy),
            Currency = ccy ?? currency
        };

        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            capture.PayPalFee = ReadOptionalMoney(breakdown, "paypal_fee");
            capture.NetAmount = ReadOptionalMoney(breakdown, "net_amount");
        }

        _logger.LogInformation("Authorization {AuthorizationId} captured: capture {CaptureId} status {Status}",
            authorizationId, capture.CaptureId, capture.Status);
        return capture;
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            Amount = new { CurrencyCode = currency, Value = FormatMoney(amount) }
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, cancellationToken);
        var result = MapAuthorization(doc.RootElement);
        _logger.LogInformation("Authorization {AuthorizationId} reauthorized: status {Status}", authorizationId, result.Status);
        return result;
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, idempotencyKey, cancellationToken);
        _logger.LogInformation("Authorization {AuthorizationId} voided", authorizationId);
    }

    public async Task<PayPalRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue
            ? new
            {
                Amount = new { CurrencyCode = currency, Value = FormatMoney(amount.Value) },
                NoteToPayer = noteToPayer
            }
            : string.IsNullOrEmpty(noteToPayer)
                ? null
                : (object)new { NoteToPayer = noteToPayer };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, cancellationToken);
        var root = doc.RootElement;

        var refund = new PayPalRefund
        {
            RefundId = root.GetProperty("id").GetString()!,
            Status = root.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty,
            Amount = ReadMoney(root, "amount", out var ccy),
            Currency = ccy ?? currency
        };

        _logger.LogInformation("Capture {CaptureId} refunded: refund {RefundId} status {Status}", captureId, refund.RefundId, refund.Status);
        return refund;
    }

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            PaymentSource = new
            {
                Card = new
                {
                    card.Name,
                    card.Number,
                    card.Expiry,
                    card.SecurityCode,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            },
            Customer = new
            {
                MerchantCustomerId = merchantCustomerId
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, idempotencyKey, cancellationToken);
        var root = doc.RootElement;

        var vaulted = new VaultedCard
        {
            VaultTokenId = root.GetProperty("id").GetString()!
        };

        if (root.TryGetProperty("payment_source", out var source) &&
            source.TryGetProperty("card", out var cardEl))
        {
            vaulted.Brand = ReadOptionalString(cardEl, "brand");
            vaulted.LastDigits = ReadOptionalString(cardEl, "last_digits");
            vaulted.Expiry = ReadOptionalString(cardEl, "expiry");
            vaulted.CardholderName = ReadOptionalString(cardEl, "name");
        }

        _logger.LogInformation("Card vaulted for customer {MerchantCustomerId}: token {VaultTokenId}", merchantCustomerId, vaulted.VaultTokenId);
        return vaulted;
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
        _logger.LogInformation("Vault token {VaultTokenId} deleted", vaultTokenId);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new ArgumentException("The 'to' timestamp must be after the 'from' timestamp.", nameof(to));
        }

        if (to - from > TimeSpan.FromDays(31))
        {
            throw new ArgumentException("PayPal transaction search supports a maximum range of 31 days.", nameof(to));
        }

        var transactions = new List<PayPalTransaction>();
        const int pageSize = 100;

        var (pageTransactions, totalPages) = await ListTransactionsPageAsync(from, to, page: 1, pageSize, cancellationToken);
        transactions.AddRange(pageTransactions);

        for (var page = 2; page <= totalPages; page++)
        {
            (pageTransactions, _) = await ListTransactionsPageAsync(from, to, page, pageSize, cancellationToken);
            transactions.AddRange(pageTransactions);
        }

        _logger.LogInformation("Transaction search {From} - {To}: {Count} transactions over {Pages} page(s)",
            from, to, transactions.Count, Math.Max(totalPages, 1));
        return transactions;
    }

    private async Task<(IReadOnlyList<PayPalTransaction> Transactions, int TotalPages)> ListTransactionsPageAsync(
        DateTimeOffset from, DateTimeOffset to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query =
            $"start_date={Uri.EscapeDataString(FormatTimestamp(from))}" +
            $"&end_date={Uri.EscapeDataString(FormatTimestamp(to))}" +
            $"&fields=all&balance_affecting_records_only=N" +
            $"&page_size={pageSize}&page={page}&total_required=true";

        using var doc = await SendAsync(HttpMethod.Get, $"/v1/reporting/transactions?{query}", null, null, cancellationToken);
        var root = doc.RootElement;

        var totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var pages) ? pages : 1;
        var transactions = new List<PayPalTransaction>();

        if (root.TryGetProperty("transaction_details", out var details))
        {
            foreach (var detail in details.EnumerateArray())
            {
                if (!detail.TryGetProperty("transaction_info", out var info))
                {
                    continue;
                }

                transactions.Add(new PayPalTransaction
                {
                    TransactionId = ReadOptionalString(info, "transaction_id") ?? string.Empty,
                    ReferenceId = ReadOptionalString(info, "paypal_reference_id"),
                    ReferenceIdType = ReadOptionalString(info, "paypal_reference_id_type"),
                    EventCode = ReadOptionalString(info, "transaction_event_code"),
                    Status = ReadOptionalString(info, "transaction_status"),
                    Amount = ReadOptionalMoney(info, "transaction_amount"),
                    Currency = ReadCurrency(info, "transaction_amount"),
                    Fee = ReadOptionalMoney(info, "fee_amount"),
                    InitiationDate = ReadOptionalTimestamp(info, "transaction_initiation_date"),
                    UpdatedDate = ReadOptionalTimestamp(info, "transaction_updated_date")
                });
            }
        }

        return (transactions, totalPages);
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

            _settings.Validate();

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with status {StatusCode}", (int)response.StatusCode);
                throw new PayPalApiException((int)response.StatusCode, null,
                    "PayPal rejected the client credentials; verify PayPal:ClientId / PayPal:ClientSecret.", null);
            }

            using var doc = JsonDocument.Parse(payload);
            _accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

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
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError((int)response.StatusCode, payload);
        }

        return string.IsNullOrWhiteSpace(payload)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(payload);
    }

    private PayPalApiException ParseError(int statusCode, string payload)
    {
        string? name = null;
        string? debugId = null;
        string? message = null;
        string? issues = null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            name = ReadOptionalString(root, "name");
            debugId = ReadOptionalString(root, "debug_id");
            message = ReadOptionalString(root, "message");

            if (root.TryGetProperty("details", out var details))
            {
                var parts = new List<string>();
                foreach (var detail in details.EnumerateArray())
                {
                    var issue = ReadOptionalString(detail, "issue");
                    var description = ReadOptionalString(detail, "description");
                    if (!string.IsNullOrEmpty(issue))
                    {
                        parts.Add(string.IsNullOrEmpty(description) ? issue! : $"{issue}: {description}");
                    }
                }

                if (parts.Count > 0)
                {
                    issues = string.Join("; ", parts);
                }
            }
        }
        catch (JsonException)
        {
            // Payload was not JSON; fall through to the generic message.
        }

        var fullMessage = $"PayPal API error {statusCode} ({name ?? "unknown"})" +
                          (string.IsNullOrEmpty(issues) ? string.Empty : $": {issues}") +
                          (string.IsNullOrEmpty(message) ? string.Empty : $" — {message}");

        _logger.LogWarning("PayPal API call failed: status {StatusCode}, error {ErrorName}, debug id {DebugId}", statusCode, name, debugId);
        return new PayPalApiException(statusCode, name, fullMessage, debugId, issues);
    }

    private static PayPalAuthorization MapAuthorization(JsonElement element)
    {
        return new PayPalAuthorization
        {
            AuthorizationId = element.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Status = ReadOptionalString(element, "status") ?? string.Empty,
            Amount = ReadMoney(element, "amount", out var ccy),
            Currency = ccy ?? string.Empty,
            ExpiresAt = ReadOptionalTimestamp(element, "expiration_time")
        };
    }

    private static bool TryGetFirstAuthorization(JsonElement order, out JsonElement authorization)
    {
        authorization = default;
        if (!order.TryGetProperty("purchase_units", out var units) || units.GetArrayLength() == 0)
        {
            return false;
        }

        if (!units[0].TryGetProperty("payments", out var payments) ||
            !payments.TryGetProperty("authorizations", out var authorizations) ||
            authorizations.GetArrayLength() == 0)
        {
            return false;
        }

        authorization = authorizations[0];
        return true;
    }

    private static bool HasPayerActionLink(JsonElement element)
    {
        if (element.TryGetProperty("links", out var links))
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(ReadOptionalString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static object? MapAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new
        {
            address.AddressLine1,
            address.AddressLine2,
            address.AdminArea2,
            address.AdminArea1,
            address.PostalCode,
            address.CountryCode
        };
    }

    private static string FormatMoney(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? ReadOptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal ReadMoney(JsonElement element, string property, out string? currency)
    {
        currency = null;
        if (!element.TryGetProperty(property, out var money))
        {
            return 0m;
        }

        currency = ReadOptionalString(money, "currency_code");
        var value = ReadOptionalString(money, "value");
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;
    }

    private static decimal? ReadOptionalMoney(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var money) || money.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var value = ReadOptionalString(money, "value");
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    private static string? ReadCurrency(JsonElement element, string property) =>
        element.TryGetProperty(property, out var money) && money.ValueKind == JsonValueKind.Object
            ? ReadOptionalString(money, "currency_code")
            : null;

    private static DateTimeOffset? ReadOptionalTimestamp(JsonElement element, string property)
    {
        var value = ReadOptionalString(element, property);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
            ? timestamp
            : null;
    }
}
