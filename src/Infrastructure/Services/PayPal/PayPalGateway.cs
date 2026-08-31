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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal REST gateway (Orders v2, Payments v2, Payment Method Tokens v3, Transaction Search v1).
/// Plain HTTPS against the PayPal API; card data is transmitted to PayPal only and is never
/// persisted or logged here.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private const int TransactionSearchPageSize = 500;
    private const int TransactionSearchMaxPages = 100;

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _settings.Validate();
        _httpClient.BaseAddress = new Uri(_settings.ResolvedBaseUrl);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardPaymentDetails card,
        string referenceId, string requestId, CancellationToken cancellationToken = default)
    {
        return await CreateAndAuthorizeOrderAsync(amount, currency, referenceId, requestId, cancellationToken,
            cardSource: BuildCardSource(card));
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultPaymentTokenId,
        string referenceId, string requestId, CancellationToken cancellationToken = default)
    {
        var cardSource = new Dictionary<string, object?>
        {
            ["vault_id"] = vaultPaymentTokenId,
            ["stored_credential"] = new Dictionary<string, object?>
            {
                ["payment_initiator"] = "CUSTOMER",
                ["payment_type"] = "ONE_TIME",
                ["usage"] = "SUBSEQUENT"
            }
        };

        return await CreateAndAuthorizeOrderAsync(amount, currency, referenceId, requestId, cancellationToken,
            cardSource: cardSource);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(amount, currency),
            ["final_capture"] = true
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, requestId, cancellationToken);

        var root = doc.RootElement;
        var breakdown = root.TryGetProperty("seller_receivable_breakdown", out var b) ? b : default;

        return new PayPalCaptureResult
        {
            CaptureId = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString() ?? string.Empty,
            GrossAmount = ReadMoney(root, "amount").amount,
            Currency = ReadMoney(root, "amount").currency ?? currency,
            PayPalFee = breakdown.ValueKind == JsonValueKind.Object ? ReadMoney(breakdown, "paypal_fee").amount : null,
            NetAmount = breakdown.ValueKind == JsonValueKind.Object ? ReadMoney(breakdown, "net_amount").amount : null
        };
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount, currency) };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, requestId, cancellationToken);

        var root = doc.RootElement;
        return new PayPalAuthorizationResult
        {
            AuthorizationId = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString() ?? string.Empty,
            Amount = ReadMoney(root, "amount").amount,
            Currency = ReadMoney(root, "amount").currency ?? currency,
            ExpiresAt = ReadDateTime(root, "expiration_time")
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            body: null, requestId, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string? noteToPayer, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = amount.HasValue ? Money(amount.Value, currency) : null, // omitted = refund in full
            ["note_to_payer"] = noteToPayer
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, requestId, cancellationToken);

        var root = doc.RootElement;
        return new PayPalRefundResult
        {
            RefundId = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString() ?? string.Empty,
            Amount = ReadMoney(root, "amount").amount,
            Currency = ReadMoney(root, "amount").currency ?? currency
        };
    }

    public async Task<PayPalPaymentTokenResult> VaultCardAsync(CardPaymentDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardSource(card)
            }
        };

        string setupTokenId;
        string? customerId;
        using (var setupDoc = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, requestId + "-setup", cancellationToken))
        {
            var root = setupDoc.RootElement;
            setupTokenId = root.GetProperty("id").GetString()!;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
            customerId = root.TryGetProperty("customer", out var c) && c.TryGetProperty("id", out var cid) ? cid.GetString() : null;

            if (!string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentValidationException(
                    $"PayPal did not approve vaulting the card (setup token status: {status ?? "unknown"}).");
            }
        }

        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = setupTokenId,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        using var tokenDoc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody, requestId + "-token", cancellationToken);
        var tokenRoot = tokenDoc.RootElement;

        var cardElement = tokenRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var ce)
            ? ce
            : default;

        return new PayPalPaymentTokenResult
        {
            PaymentTokenId = tokenRoot.GetProperty("id").GetString()!,
            CustomerId = customerId,
            Brand = cardElement.ValueKind == JsonValueKind.Object && cardElement.TryGetProperty("brand", out var brand)
                ? brand.GetString() ?? string.Empty : string.Empty,
            Last4 = cardElement.ValueKind == JsonValueKind.Object && cardElement.TryGetProperty("last_digits", out var last4)
                ? last4.GetString() ?? string.Empty : string.Empty,
            Expiry = cardElement.ValueKind == JsonValueKind.Object && cardElement.TryGetProperty("expiry", out var expiry)
                ? expiry.GetString() : null
        };
    }

    public async Task DeletePaymentTokenAsync(string vaultPaymentTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultPaymentTokenId}",
                body: null, requestId: null, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone from PayPal's vault; the local record is still removed by the caller.
            _logger.LogInformation("PayPal payment token was already deleted (debug id {DebugId}).", ex.DebugId);
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var transactions = new List<PayPalTransaction>();
        var startDate = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);
        var endDate = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

        var page = 1;
        while (page <= TransactionSearchMaxPages)
        {
            var path = "/v1/reporting/transactions" +
                       $"?start_date={Uri.EscapeDataString(startDate)}" +
                       $"&end_date={Uri.EscapeDataString(endDate)}" +
                       $"&fields=all&page_size={TransactionSearchPageSize}&page={page}";

            using var doc = await SendAsync(HttpMethod.Get, path, body: null, requestId: null, cancellationToken);
            var root = doc.RootElement;

            var countOnPage = 0;
            if (root.TryGetProperty("transaction_details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    countOnPage++;
                    if (detail.TryGetProperty("transaction_info", out var info))
                    {
                        transactions.Add(new PayPalTransaction
                        {
                            TransactionId = ReadString(info, "transaction_id"),
                            EventCode = ReadString(info, "transaction_event_code"),
                            Status = ReadString(info, "transaction_status"),
                            Amount = ReadMoney(info, "transaction_amount").amount,
                            Currency = ReadMoney(info, "transaction_amount").currency,
                            InitiationDate = ReadDateTime(info, "transaction_initiation_date"),
                            UpdatedDate = ReadDateTime(info, "transaction_updated_date"),
                            ReferenceId = ReadString(info, "paypal_reference_id")
                        });
                    }
                }
            }

            var totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var total) ? total : page;
            if (page >= totalPages || countOnPage == 0)
            {
                break;
            }
            page++;
        }

        return transactions;
    }

    /// <summary>
    /// Creates a PayPal order with intent=AUTHORIZE and ensures the funds are held.
    /// With direct card processing the create call itself completes the authorization
    /// (order status COMPLETED with the authorization embedded); otherwise the order is
    /// authorized with a follow-up call to /v2/checkout/orders/{id}/authorize.
    /// </summary>
    private async Task<PayPalAuthorizationResult> CreateAndAuthorizeOrderAsync(decimal amount, string currency,
        string referenceId, string requestId, CancellationToken cancellationToken, Dictionary<string, object?> cardSource)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["reference_id"] = referenceId,
                    ["custom_id"] = referenceId,
                    ["invoice_id"] = referenceId,
                    ["amount"] = Money(amount, currency)
                }
            },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = cardSource }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId + "-order", cancellationToken);
        var root = doc.RootElement;

        ThrowIfPayerActionRequired(root);
        var payPalOrderId = root.GetProperty("id").GetString()!;

        if (TryReadAuthorization(root, payPalOrderId, currency, out var authorization))
        {
            return authorization;
        }

        // The order was created but not yet authorized: authorize it explicitly.
        using var authorizeDoc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
            new Dictionary<string, object?>(), requestId + "-authorize", cancellationToken);

        ThrowIfPayerActionRequired(authorizeDoc.RootElement);
        if (TryReadAuthorization(authorizeDoc.RootElement, payPalOrderId, currency, out authorization))
        {
            return authorization;
        }

        throw new PayPalApiException(HttpStatusCode.BadGateway, null, null,
            "PayPal's authorize response did not contain an authorization.");
    }

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentValidationException(
                "PayPal requires the shopper to approve this payment in a browser (PAYER_ACTION_REQUIRED); " +
                "this integration only supports direct card payments without an approval round-trip.");
        }
    }

    private static bool TryReadAuthorization(JsonElement root, string payPalOrderId, string currency,
        out PayPalAuthorizationResult result)
    {
        result = null!;

        string? cardBrand = null;
        string? cardLast4 = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            cardBrand = ReadString(card, "brand");
            cardLast4 = ReadString(card, "last_digits");
        }

        if (!root.TryGetProperty("purchase_units", out var units) || units.GetArrayLength() == 0 ||
            !units[0].TryGetProperty("payments", out var payments) ||
            !payments.TryGetProperty("authorizations", out var authorizations) ||
            authorizations.GetArrayLength() == 0)
        {
            return false;
        }

        var authorization = authorizations[0];
        result = new PayPalAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization.GetProperty("id").GetString()!,
            Status = authorization.TryGetProperty("status", out var st) ? st.GetString() ?? string.Empty : string.Empty,
            Amount = ReadMoney(authorization, "amount").amount,
            Currency = ReadMoney(authorization, "amount").currency ?? currency,
            ExpiresAt = ReadDateTime(authorization, "expiration_time"),
            CardBrand = cardBrand,
            CardLast4 = cardLast4
        };
        return true;
    }

    private static Dictionary<string, object?> BuildCardSource(CardPaymentDetails card)
    {
        return new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.CardholderName,
            ["billing_address"] = card.BillingAddress is null ? null : new Dictionary<string, object?>
            {
                ["address_line_1"] = card.BillingAddress.AddressLine1,
                ["address_line_2"] = card.BillingAddress.AddressLine2,
                ["admin_area_2"] = card.BillingAddress.AdminArea2,
                ["admin_area_1"] = card.BillingAddress.AdminArea1,
                ["postal_code"] = card.BillingAddress.PostalCode,
                ["country_code"] = card.BillingAddress.CountryCode
            }
        };
    }

    private static Dictionary<string, object?> Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        var response = await SendCoreAsync(method, path, body, requestId, cancellationToken);

        // A stale cached token should not fail the call: refresh once and retry.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _accessToken = null;
            response = await SendCoreAsync(method, path, body, requestId, cancellationToken);
        }

        var content = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError(response.StatusCode, content);
        }

        // Never log content: responses can echo card metadata.
        return string.IsNullOrWhiteSpace(content)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(content);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PayPalApiException(HttpStatusCode.BadGateway, null, null, $"PayPal is unreachable: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PayPalApiException(HttpStatusCode.GatewayTimeout, null, null, $"PayPal did not respond in time: {ex.Message}");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw ParseError(response.StatusCode, content);
            }

            using var doc = JsonDocument.Parse(content);
            _accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PayPalApiException ParseError(HttpStatusCode statusCode, string content)
    {
        string? name = null;
        string? issue = null;
        string? debugId = null;
        var message = $"PayPal request failed with status {(int)statusCode}.";

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            name = ReadString(root, "name");
            debugId = ReadString(root, "debug_id");
            if (root.TryGetProperty("message", out var m))
            {
                message = m.GetString() ?? message;
            }
            if (root.TryGetProperty("details", out var details) && details.GetArrayLength() > 0)
            {
                issue = ReadString(details[0], "issue");
                var description = ReadString(details[0], "description");
                if (!string.IsNullOrEmpty(description))
                {
                    message = $"{message} ({issue}: {description})";
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g. a proxy error page); keep the generic message.
        }

        return new PayPalApiException(statusCode, name, issue, $"PayPal error {(int)statusCode}: {message}", debugId);
    }

    private static string ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static (decimal amount, string? currency) ReadMoney(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var money) || money.ValueKind != JsonValueKind.Object)
        {
            return (0m, null);
        }

        var amount = money.TryGetProperty("value", out var v) && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
        var currency = money.TryGetProperty("currency_code", out var c) ? c.GetString() : null;
        return (amount, currency);
    }

    private static DateTimeOffset? ReadDateTime(JsonElement element, string property)
    {
        var raw = element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}
