using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// PayPal REST gateway. Talks to the Payments API (Orders v2, Payments v2, Vault v3,
/// Transaction Search) over the configured base address. Full card details pass
/// through to PayPal only; they are never persisted and never logged here.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalPaymentGateway(HttpClient httpClient, PayPalSettings settings, IAppLogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables, e.g. via .NET user-secrets).");

        _httpClient.BaseAddress = new Uri(ResolveBaseUrl(settings));
    }

    public static string ResolveBaseUrl(PayPalSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            return settings.BaseUrl!.TrimEnd('/');
        return string.Equals(settings.Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency,
        string referenceId, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = referenceId,
                    invoice_id = invoiceId,
                    custom_id = invoiceId,
                    amount = new { currency_code = currency, value = FormatMoney(amount) }
                }
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, cancellationToken);
        var root = doc.RootElement;
        return new PayPalOrderResult
        {
            Id = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString() ?? string.Empty
        };
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId,
        CardDetails? card, string? vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object paymentSource;
        if (vaultTokenId is not null)
        {
            paymentSource = new
            {
                card = new
                {
                    vault_id = vaultTokenId,
                    stored_credential = new
                    {
                        payment_initiator = "CUSTOMER",
                        payment_type = "ONE_TIME"
                    }
                }
            };
        }
        else if (card is not null)
        {
            paymentSource = new
            {
                card = new
                {
                    name = card.Name,
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    billing_address = card.BillingAddress is null ? null : new
                    {
                        address_line_1 = card.BillingAddress.AddressLine1,
                        address_line_2 = card.BillingAddress.AddressLine2,
                        admin_area_2 = card.BillingAddress.City,
                        admin_area_1 = card.BillingAddress.State,
                        postal_code = card.BillingAddress.PostalCode,
                        country_code = card.BillingAddress.CountryCode
                    }
                }
            };
        }
        else
        {
            throw new ArgumentException("Either card details or a vault token id must be supplied.");
        }

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
            new { payment_source = paymentSource }, idempotencyKey, cancellationToken);

        var root = doc.RootElement;
        var authorization = root
            .GetProperty("purchase_units")[0]
            .GetProperty("payments")
            .GetProperty("authorizations")[0];

        return ParseAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}",
            null, null, cancellationToken);
        return ParseAuthorization(doc.RootElement);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = currency, value = FormatMoney(amount) }
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, idempotencyKey, cancellationToken);
        return ParseAuthorization(doc.RootElement);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = currency, value = FormatMoney(amount) },
            invoice_id = invoiceId,
            final_capture = true
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, idempotencyKey, cancellationToken);

        var root = doc.RootElement;
        var result = new PayPalCaptureResult
        {
            Id = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString() ?? string.Empty,
            Amount = ParseMoney(root.GetProperty("amount")),
            Currency = root.GetProperty("amount").GetProperty("currency_code").GetString() ?? currency
        };

        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("paypal_fee", out var fee))
                result.PayPalFee = ParseMoney(fee);
            if (breakdown.TryGetProperty("net_amount", out var net))
                result.NetAmount = ParseMoney(net);
        }

        return result;
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            null, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        object? body = amount is null && noteToPayer is null
            ? new { }
            : new
            {
                amount = amount is null ? null : new { currency_code = currency, value = FormatMoney(amount.Value) },
                note_to_payer = noteToPayer
            };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, idempotencyKey, cancellationToken);

        var root = doc.RootElement;
        var result = new PayPalRefundResult
        {
            Id = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString() ?? string.Empty
        };
        if (root.TryGetProperty("amount", out var amountElement))
        {
            result.Amount = ParseMoney(amountElement);
            result.Currency = amountElement.GetProperty("currency_code").GetString();
        }
        if (root.TryGetProperty("seller_payable_breakdown", out var breakdown)
            && breakdown.TryGetProperty("total_refunded_amount", out var totalRefunded))
        {
            result.TotalRefundedAmount = ParseMoney(totalRefunded);
        }
        return result;
    }

    public async Task<PayPalVaultTokenResult> VaultCardAsync(CardDetails card, string customerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            payment_source = new
            {
                card = new
                {
                    name = card.Name,
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    billing_address = card.BillingAddress is null ? null : new
                    {
                        address_line_1 = card.BillingAddress.AddressLine1,
                        address_line_2 = card.BillingAddress.AddressLine2,
                        admin_area_2 = card.BillingAddress.City,
                        admin_area_1 = card.BillingAddress.State,
                        postal_code = card.BillingAddress.PostalCode,
                        country_code = card.BillingAddress.CountryCode
                    }
                }
            },
            customer = new { merchant_customer_id = customerId }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, idempotencyKey, cancellationToken);

        var root = doc.RootElement;
        var result = new PayPalVaultTokenResult
        {
            Id = root.GetProperty("id").GetString()!
        };
        if (root.TryGetProperty("payment_source", out var source)
            && source.TryGetProperty("card", out var cardElement))
        {
            result.Brand = GetStringOrNull(cardElement, "brand");
            result.LastDigits = GetStringOrNull(cardElement, "last_digits");
            result.Expiry = GetStringOrNull(cardElement, "expiry");
            result.CardholderName = GetStringOrNull(cardElement, "name");
        }
        return result;
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // The reporting API accepts a maximum range of 31 days per call.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;
            await ListTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ListTransactionWindowAsync(DateTimeOffset from, DateTimeOffset to,
        List<PayPalTransaction> results, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var page = 1;
        while (true)
        {
            var path = "/v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(FormatTimestamp(from))}" +
                $"&end_date={Uri.EscapeDataString(FormatTimestamp(to))}" +
                $"&fields=all&balance_affecting_records_only=N&page_size={pageSize}&page={page}";

            using var doc = await SendTransactionSearchAsync(path, cancellationToken);
            if (doc is null)
                break; // PayPal reports "no data" for this window; nothing to collect.
            var root = doc.RootElement;

            var count = 0;
            if (root.TryGetProperty("transaction_details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    count++;
                    if (detail.TryGetProperty("transaction_info", out var info))
                        results.Add(ParseTransaction(info));
                }
            }

            var totalPages = root.TryGetProperty("total_pages", out var totalPagesElement)
                && totalPagesElement.TryGetInt32(out var parsedTotalPages)
                ? parsedTotalPages
                : (int?)null;

            if (totalPages is not null ? page >= totalPages : count < pageSize)
                break;
            page++;
        }
    }

    /// <summary>
    /// Transaction Search answers 404 INVALID_REQUEST ("Data for the given start date
    /// is not available") for windows that simply have no data yet; that is an empty
    /// page, not a failure.
    /// </summary>
    private async Task<JsonDocument?> SendTransactionSearchAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderStatusCode == 404
            && ex.ProviderErrorName == "INVALID_REQUEST")
        {
            return null;
        }
    }

    private static PayPalTransaction ParseTransaction(JsonElement info)
    {
        return new PayPalTransaction
        {
            TransactionId = GetStringOrNull(info, "transaction_id") ?? string.Empty,
            ReferenceId = GetStringOrNull(info, "paypal_reference_id"),
            ReferenceIdType = GetStringOrNull(info, "paypal_reference_id_type"),
            EventCode = GetStringOrNull(info, "transaction_event_code"),
            Status = GetStringOrNull(info, "transaction_status"),
            Amount = info.TryGetProperty("transaction_amount", out var amount) ? ParseMoney(amount) : null,
            Currency = info.TryGetProperty("transaction_amount", out var amountForCurrency)
                ? GetStringOrNull(amountForCurrency, "currency_code") : null,
            FeeAmount = info.TryGetProperty("fee_amount", out var fee) ? ParseMoney(fee) : null,
            InitiationDate = ParseTimestamp(GetStringOrNull(info, "transaction_initiation_date")),
            UpdatedDate = ParseTimestamp(GetStringOrNull(info, "transaction_updated_date")),
            InvoiceId = GetStringOrNull(info, "invoice_id"),
            CustomField = GetStringOrNull(info, "custom_field")
        };
    }

    private static PayPalAuthorizationResult ParseAuthorization(JsonElement element)
    {
        var result = new PayPalAuthorizationResult
        {
            Id = element.GetProperty("id").GetString()!,
            Status = element.GetProperty("status").GetString() ?? string.Empty
        };
        if (element.TryGetProperty("amount", out var amount))
        {
            result.Amount = ParseMoney(amount);
            result.Currency = GetStringOrNull(amount, "currency_code");
        }
        result.ExpirationTime = ParseTimestamp(GetStringOrNull(element, "expiration_time"));
        return result;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(idempotencyKey))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToGatewayException((int)response.StatusCode, method, path, content);
        }

        // Never log payloads here: some of them traverse card data.
        _logger.LogInformation($"PayPal {method} {path} -> {(int)response.StatusCode}");

        return string.IsNullOrWhiteSpace(content)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(content);
    }

    private PaymentGatewayException ToGatewayException(int statusCode, HttpMethod method, string path, string content)
    {
        string? name = null, message = null, debugId = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            name = GetStringOrNull(root, "name");
            message = GetStringOrNull(root, "message");
            debugId = GetStringOrNull(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.GetArrayLength() > 0)
            {
                var issue = GetStringOrNull(details[0], "issue");
                var description = GetStringOrNull(details[0], "description");
                if (issue is not null)
                    message = $"{message} [{issue}: {description}]";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with what we have.
        }

        _logger.LogWarning($"PayPal {method} {path} failed with {statusCode}: {name} {debugId}");
        return new PaymentGatewayException(
            $"PayPal request failed ({statusCode} {name}): {message ?? "no details returned"}.",
            name, debugId, statusCode);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials",
                Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ToGatewayException((int)response.StatusCode, HttpMethod.Post, "/v1/oauth2/token", content);

            using var doc = JsonDocument.Parse(content);
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

    private static string FormatMoney(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(JsonElement money) =>
        decimal.Parse(money.GetProperty("value").GetString()!, CultureInfo.InvariantCulture);

    private static string? GetStringOrNull(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
}
