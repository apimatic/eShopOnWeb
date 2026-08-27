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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST gateway (Orders v2, Payments v2, Vault v3, Transaction Search v1).
/// Full card details pass through here in memory only and are never logged or persisted.
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions SnakeCase = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(e.g. from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables via user-secrets).");

        _httpClient.BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/");
    }

    public async Task<GatewayAuthorizationResult> AuthorizeCardPaymentAsync(decimal amount, string currency, CardDetails card,
        string? customId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var paymentSource = BuildCardPaymentSource(card);
        return await CreateAndAuthorizeOrderAsync(amount, currency, paymentSource, customId, idempotencyKey, cancellationToken);
    }

    public async Task<GatewayAuthorizationResult> AuthorizeVaultedCardPaymentAsync(decimal amount, string currency, string vaultTokenId,
        string? customId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object>
        {
            ["card"] = new Dictionary<string, object> { ["vault_id"] = vaultTokenId }
        };
        return await CreateAndAuthorizeOrderAsync(amount, currency, paymentSource, customId, idempotencyKey, cancellationToken);
    }

    public async Task<GatewayAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        var root = doc.RootElement;
        return new GatewayAuthorizationDetails
        {
            AuthorizationId = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString()!,
            Amount = ParseMoney(root.GetProperty("amount"), out var currency),
            Currency = currency,
            ExpiresAt = ParseDate(root, "expiration_time")
        };
    }

    public async Task<GatewayCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = currency, value = FormatAmount(amount) },
            final_capture = true
        };

        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, cancellationToken);
        var root = doc.RootElement;

        decimal gross = amount, fee = 0m, net = amount;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("gross_amount", out var g)) gross = ParseMoney(g, out _);
            if (breakdown.TryGetProperty("paypal_fee", out var f)) fee = ParseMoney(f, out _);
            if (breakdown.TryGetProperty("net_amount", out var n)) net = ParseMoney(n, out _);
        }

        return new GatewayCaptureResult
        {
            CaptureId = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString()!,
            GrossAmount = gross,
            PayPalFee = fee,
            NetAmount = net,
            Currency = currency
        };
    }

    public async Task<GatewayAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currency, value = FormatAmount(amount) } };

        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, cancellationToken);
        var root = doc.RootElement;
        return new GatewayAuthorizationResult
        {
            AuthorizationId = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString()!,
            Amount = ParseMoney(root.GetProperty("amount"), out var renewedCurrency),
            Currency = renewedCurrency,
            ExpiresAt = ParseDate(root, "expiration_time")
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken);
    }

    public async Task<GatewayRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object>
        {
            ["custom_id"] = idempotencyKey
        };
        if (amount.HasValue)
            body["amount"] = new Dictionary<string, object> { ["currency_code"] = currency, ["value"] = FormatAmount(amount.Value) };
        if (!string.IsNullOrEmpty(noteToPayer))
            body["note_to_payer"] = noteToPayer;

        using var doc = await SendAsync(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", body, idempotencyKey, cancellationToken);
        var root = doc.RootElement;

        decimal refundedAmount = amount ?? 0m;
        string refundedCurrency = currency;
        if (root.TryGetProperty("amount", out var amountElement))
            refundedAmount = ParseMoney(amountElement, out refundedCurrency);

        return new GatewayRefundResult
        {
            RefundId = root.GetProperty("id").GetString()!,
            Status = root.TryGetProperty("status", out var status) ? status.GetString()! : "COMPLETED",
            Amount = refundedAmount,
            Currency = refundedCurrency
        };
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var setupBody = new Dictionary<string, object>
        {
            ["payment_source"] = BuildCardPaymentSource(card)
        };

        using var setupDoc = await SendAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupBody, idempotencyKey, cancellationToken);
        var setupRoot = setupDoc.RootElement;
        var setupTokenId = setupRoot.GetProperty("id").GetString()!;
        var setupStatus = setupRoot.TryGetProperty("status", out var ss) ? ss.GetString() : null;
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new PaymentGatewayException($"PayPal did not approve the card for saving (setup token status: '{setupStatus}').");

        var tokenBody = new
        {
            payment_source = new
            {
                token = new { id = setupTokenId, type = "SETUP_TOKEN" }
            }
        };

        using var tokenDoc = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", tokenBody, idempotencyKey + "-confirm", cancellationToken);
        var tokenRoot = tokenDoc.RootElement;

        string? brand = null, lastDigits = null, expiry = null, name = null;
        if (tokenRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardElement))
        {
            brand = GetStringOrNull(cardElement, "brand");
            lastDigits = GetStringOrNull(cardElement, "last_digits");
            expiry = GetStringOrNull(cardElement, "expiry");
            name = GetStringOrNull(cardElement, "name");
        }

        return new GatewayVaultedCard
        {
            VaultTokenId = tokenRoot.GetProperty("id").GetString()!,
            CustomerId = tokenRoot.TryGetProperty("customer", out var customer) ? GetStringOrNull(customer, "id") : null,
            Brand = brand,
            LastDigits = lastDigits,
            Expiry = expiry,
            CardholderName = name
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
    }

    public async Task<GatewayTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var path = "v1/reporting/transactions" +
            $"?start_date={Uri.EscapeDataString(FormatSearchDate(from))}" +
            $"&end_date={Uri.EscapeDataString(FormatSearchDate(to))}" +
            "&fields=transaction_info" +
            $"&page_size={pageSize}&page={page}";

        using var doc = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
        var root = doc.RootElement;

        var transactions = new List<GatewayTransaction>();
        if (root.TryGetProperty("transaction_details", out var details))
        {
            foreach (var detail in details.EnumerateArray())
            {
                if (!detail.TryGetProperty("transaction_info", out var info))
                    continue;

                decimal txnAmount = 0m;
                var txnCurrency = string.Empty;
                if (info.TryGetProperty("transaction_amount", out var amountElement))
                    txnAmount = ParseMoney(amountElement, out txnCurrency);

                transactions.Add(new GatewayTransaction
                {
                    TransactionId = GetStringOrNull(info, "transaction_id") ?? string.Empty,
                    ReferenceId = GetStringOrNull(info, "paypal_reference_id"),
                    EventCode = GetStringOrNull(info, "transaction_event_code"),
                    Status = GetStringOrNull(info, "transaction_status"),
                    Amount = txnAmount,
                    Currency = txnCurrency,
                    Fee = info.TryGetProperty("fee_amount", out var fee) ? ParseMoney(fee, out _) : null,
                    Time = ParseDate(info, "transaction_initiation_date"),
                    CustomField = GetStringOrNull(info, "custom_field")
                });
            }
        }

        return new GatewayTransactionPage
        {
            Transactions = transactions,
            Page = root.TryGetProperty("page", out var p) ? p.GetInt32() : page,
            TotalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : page,
            TotalItems = root.TryGetProperty("total_items", out var ti) ? ti.GetInt32() : transactions.Count
        };
    }

    private async Task<GatewayAuthorizationResult> CreateAndAuthorizeOrderAsync(decimal amount, string currency,
        Dictionary<string, object> paymentSource, string? customId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var createBody = new Dictionary<string, object>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["amount"] = new Dictionary<string, object>
                    {
                        ["currency_code"] = currency,
                        ["value"] = FormatAmount(amount)
                    },
                    ["custom_id"] = customId ?? string.Empty
                }
            },
            ["payment_source"] = paymentSource
        };

        using var orderDoc = await SendAsync(HttpMethod.Post, "v2/checkout/orders", createBody, idempotencyKey, cancellationToken);
        var orderRoot = orderDoc.RootElement;
        var payPalOrderId = orderRoot.GetProperty("id").GetString()!;
        var orderStatus = orderRoot.TryGetProperty("status", out var os) ? os.GetString() : null;

        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PaymentGatewayException(
                "PayPal requires the shopper to approve this card payment in a browser (PAYER_ACTION_REQUIRED). " +
                "This integration does not support an approval round-trip.");

        JsonElement authorization;
        // With a payment_source supplied, PayPal processes single-step: the create-order
        // response may already carry the authorization. Only call /authorize when it does not.
        if (TryGetAuthorization(orderRoot, out var existing))
        {
            authorization = existing.Clone();
        }
        else
        {
            using var authDoc = await SendAsync(HttpMethod.Post, $"v2/checkout/orders/{payPalOrderId}/authorize",
                new { }, idempotencyKey + "-authorize", cancellationToken);
            if (!TryGetAuthorization(authDoc.RootElement, out var authorized))
                throw new PaymentGatewayException($"PayPal order {payPalOrderId} returned no authorization after authorize call.");
            authorization = authorized.Clone();
        }

        return new GatewayAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization.GetProperty("id").GetString()!,
            Status = authorization.GetProperty("status").GetString()!,
            Amount = ParseMoney(authorization.GetProperty("amount"), out var authCurrency),
            Currency = authCurrency,
            ExpiresAt = ParseDate(authorization, "expiration_time")
        };
    }

    private static bool TryGetAuthorization(JsonElement orderRoot, out JsonElement authorization)
    {
        authorization = default;
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.GetArrayLength() == 0)
            return false;
        var unit = units[0];
        if (!unit.TryGetProperty("payments", out var payments))
            return false;
        if (!payments.TryGetProperty("authorizations", out var authorizations) || authorizations.GetArrayLength() == 0)
            return false;
        authorization = authorizations[0];
        return true;
    }

    private static Dictionary<string, object> BuildCardPaymentSource(CardDetails card)
    {
        var cardSource = new Dictionary<string, object>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrEmpty(card.Name))
            cardSource["name"] = card.Name;
        if (!string.IsNullOrEmpty(card.SecurityCode))
            cardSource["security_code"] = card.SecurityCode;
        if (card.BillingAddress != null)
        {
            var address = new Dictionary<string, object>
            {
                ["country_code"] = card.BillingAddress.CountryCode
            };
            if (!string.IsNullOrEmpty(card.BillingAddress.AddressLine1)) address["address_line_1"] = card.BillingAddress.AddressLine1;
            if (!string.IsNullOrEmpty(card.BillingAddress.AddressLine2)) address["address_line_2"] = card.BillingAddress.AddressLine2;
            if (!string.IsNullOrEmpty(card.BillingAddress.AdminArea1)) address["admin_area_1"] = card.BillingAddress.AdminArea1;
            if (!string.IsNullOrEmpty(card.BillingAddress.AdminArea2)) address["admin_area_2"] = card.BillingAddress.AdminArea2;
            if (!string.IsNullOrEmpty(card.BillingAddress.PostalCode)) address["postal_code"] = card.BillingAddress.PostalCode;
            cardSource["billing_address"] = address;
        }

        return new Dictionary<string, object> { ["card"] = cardSource };
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey,
        CancellationToken cancellationToken, bool isRetryAfterAuthRefresh = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(idempotencyKey))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        if (body != null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, SnakeCase), Encoding.UTF8, "application/json");
        else if (method == HttpMethod.Post)
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isRetryAfterAuthRefresh)
        {
            ResetToken();
            return await SendAsync(method, path, body, idempotencyKey, cancellationToken, isRetryAfterAuthRefresh: true);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Error payloads contain provider error names/messages only; never log request bodies (card data).
            string name = "", message = content;
            try
            {
                using var errorDoc = JsonDocument.Parse(content);
                name = GetStringOrNull(errorDoc.RootElement, "name") ?? "";
                message = GetStringOrNull(errorDoc.RootElement, "message") ?? content;
                if (errorDoc.RootElement.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    var issues = details.EnumerateArray()
                        .Select(d => $"{GetStringOrNull(d, "issue")}: {GetStringOrNull(d, "description")}")
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                    if (issues.Count > 0)
                        message = $"{message} [{string.Join("; ", issues)}]";
                }
            }
            catch (JsonException) { }

            _logger.LogWarning("PayPal {Method} {Path} failed with {StatusCode} {Name}: {Message}",
                method, path, (int)response.StatusCode, name, message);
            throw new PaymentGatewayException($"PayPal error {(int)response.StatusCode} {name}: {message}");
        }

        if (string.IsNullOrWhiteSpace(content))
            return JsonDocument.Parse("{}");

        return JsonDocument.Parse(content);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new PaymentGatewayException($"PayPal authentication failed with status {(int)response.StatusCode}.");

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

    private void ResetToken()
    {
        _accessToken = null;
        _tokenExpiresAt = DateTimeOffset.MinValue;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(JsonElement money, out string currency)
    {
        currency = money.TryGetProperty("currency_code", out var c) ? c.GetString() ?? string.Empty : string.Empty;
        var value = money.TryGetProperty("value", out var v) ? v.GetString() : null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    private static DateTimeOffset? ParseDate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;
        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    private static string? GetStringOrNull(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
