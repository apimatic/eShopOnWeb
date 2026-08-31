using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> against the REST APIs
/// (Orders v2, Payments v2, Vault v3, Transaction Search v1).
/// Full card numbers transit through here to PayPal only; they are never logged or stored.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    public const string HttpClientName = "paypal";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    // Run-unique prefix for PayPal-Request-Id values: keeps retries idempotent within a run
    // without colliding with keys a previous run (same order ids) already consumed at PayPal.
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];

    public PayPalPaymentGateway(IHttpClientFactory httpClientFactory, IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _settings.Validate();
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeOrderAsync(int orderId, decimal amount, string currency,
        PaymentSourceDto paymentSource, string requestId, CancellationToken cancellationToken = default)
    {
        var createOrderBody = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = $"eshop-order-{orderId}",
                    ["custom_id"] = $"eshop-order-{orderId}",
                    // invoice_id must be unique within the merchant account; custom_id carries the stable order reference.
                    ["invoice_id"] = $"eshop-order-{orderId}-{Guid.NewGuid():N}",
                    ["description"] = $"eShopOnWeb order {orderId}",
                    ["amount"] = Money(amount, currency)
                }
            }
        };

        using var orderDoc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", createOrderBody,
            $"{requestId}-order", cancellationToken);
        var payPalOrderId = orderDoc.RootElement.GetProperty("id").GetString()
            ?? throw new PayPalApiException(200, null, null, null, "PayPal create-order response did not include an order id.");

        JsonObject authorizeBody;
        if (paymentSource.VaultTokenId != null)
        {
            authorizeBody = new JsonObject
            {
                ["payment_source"] = new JsonObject
                {
                    ["card"] = new JsonObject { ["vault_id"] = paymentSource.VaultTokenId }
                }
            };
        }
        else
        {
            authorizeBody = new JsonObject
            {
                ["payment_source"] = new JsonObject
                {
                    ["card"] = BuildCardJson(paymentSource.Card!)
                }
            };
        }

        using var authorizeDoc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
            authorizeBody, $"{requestId}-authorize", cancellationToken);

        var authorization = authorizeDoc.RootElement
            .GetProperty("purchase_units")[0]
            .GetProperty("payments")
            .GetProperty("authorizations")[0];

        return new AuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization.GetProperty("id").GetString()!,
            Status = authorization.GetProperty("status").GetString()!,
            Amount = ParseMoney(authorization.GetProperty("amount")),
            Currency = authorization.GetProperty("amount").GetProperty("currency_code").GetString()!,
            ExpirationTime = ParseDate(authorization, "expiration_time")
        };
    }

    public async Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}",
            null, null, cancellationToken);
        return ParseAuthorization(doc.RootElement);
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = Money(amount, currency),
            ["final_capture"] = true
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, requestId, cancellationToken);
        var root = doc.RootElement;

        var result = new CaptureResult
        {
            CaptureId = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString()!,
            GrossAmount = ParseMoney(root.GetProperty("amount")),
            Currency = root.GetProperty("amount").GetProperty("currency_code").GetString()!
        };

        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            result.GrossAmount = ParseMoney(breakdown.GetProperty("gross_amount"));
            result.PayPalFee = TryParseMoney(breakdown, "paypal_fee");
            result.NetAmount = TryParseMoney(breakdown, "net_amount");
        }

        return result;
    }

    public async Task<AuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["amount"] = Money(amount, currency) };
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, requestId, cancellationToken);
        return ParseAuthorization(doc.RootElement);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            new JsonObject(), requestId, cancellationToken);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (amount.HasValue)
        {
            body["amount"] = Money(amount.Value, currency);
        }
        if (!string.IsNullOrWhiteSpace(noteToPayer))
        {
            body["note_to_payer"] = noteToPayer;
        }

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, requestId, cancellationToken);
        var root = doc.RootElement;

        decimal? totalRefunded = null;
        if (root.TryGetProperty("seller_payable_breakdown", out var breakdown))
        {
            totalRefunded = TryParseMoney(breakdown, "total_refunded_amount");
        }

        return new RefundResult
        {
            RefundId = root.GetProperty("id").GetString()!,
            Status = root.GetProperty("status").GetString()!,
            Amount = ParseMoney(root.GetProperty("amount")),
            Currency = root.GetProperty("amount").GetProperty("currency_code").GetString()!,
            TotalRefundedAmount = totalRefunded
        };
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId, string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["card"] = BuildCardJson(card)
            },
            ["customer"] = new JsonObject
            {
                // customer.id is PayPal-generated; our own shopper reference goes in merchant_customer_id.
                ["merchant_customer_id"] = customerId
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, cancellationToken);
        var root = doc.RootElement;
        var cardJson = root.GetProperty("payment_source").GetProperty("card");

        return new VaultedCardResult
        {
            VaultTokenId = root.GetProperty("id").GetString()!,
            Brand = cardJson.TryGetProperty("brand", out var brand) ? brand.GetString() : null,
            LastDigits = cardJson.TryGetProperty("last_digits", out var lastDigits) ? lastDigits.GetString() : null,
            Expiry = cardJson.TryGetProperty("expiry", out var expiry) ? expiry.GetString() : null
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderTransaction>();

        // The API supports a maximum range of 31 days per request; chunk larger ranges.
        for (var windowStart = from; windowStart < to;)
        {
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;

            var page = 1;
            while (true)
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={FormatTimestamp(windowStart)}&end_date={FormatTimestamp(windowEnd)}" +
                    $"&fields=all&balance_affecting_records_only=N&page_size=100&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var root = doc.RootElement;

                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info))
                        {
                            continue;
                        }

                        results.Add(new ProviderTransaction
                        {
                            TransactionId = info.TryGetProperty("transaction_id", out var tid) ? tid.GetString() ?? "" : "",
                            ReferenceId = GetStringOrNull(info, "paypal_reference_id"),
                            ReferenceIdType = GetStringOrNull(info, "paypal_reference_id_type"),
                            EventCode = GetStringOrNull(info, "transaction_event_code"),
                            Status = GetStringOrNull(info, "transaction_status"),
                            Amount = TryParseMoney(info, "transaction_amount"),
                            Currency = info.TryGetProperty("transaction_amount", out var ta) && ta.TryGetProperty("currency_code", out var cc) ? cc.GetString() : null,
                            Fee = TryParseMoney(info, "fee_amount"),
                            InvoiceId = GetStringOrNull(info, "invoice_id"),
                            CustomField = GetStringOrNull(info, "custom_field"),
                            InitiationDate = ParseDate(info, "transaction_initiation_date"),
                            UpdatedDate = ParseDate(info, "transaction_updated_date")
                        });
                    }
                }

                var totalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : 1;
                if (page >= totalPages)
                {
                    break;
                }
                page++;
            }

            windowStart = windowEnd;
        }

        return results;
    }

    private static JsonObject BuildCardJson(CardDetails card)
    {
        var cardJson = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            cardJson["security_code"] = card.SecurityCode;
        }
        if (!string.IsNullOrWhiteSpace(card.HolderName))
        {
            cardJson["name"] = card.HolderName;
        }
        if (!string.IsNullOrWhiteSpace(card.CountryCode))
        {
            var address = new JsonObject { ["country_code"] = card.CountryCode };
            if (!string.IsNullOrWhiteSpace(card.AddressLine1)) address["address_line_1"] = card.AddressLine1;
            if (!string.IsNullOrWhiteSpace(card.AdminArea2)) address["admin_area_2"] = card.AdminArea2;
            if (!string.IsNullOrWhiteSpace(card.AdminArea1)) address["admin_area_1"] = card.AdminArea1;
            if (!string.IsNullOrWhiteSpace(card.PostalCode)) address["postal_code"] = card.PostalCode;
            cardJson["billing_address"] = address;
        }
        return cardJson;
    }

    private static AuthorizationDetails ParseAuthorization(JsonElement root) => new()
    {
        AuthorizationId = root.GetProperty("id").GetString()!,
        Status = root.GetProperty("status").GetString()!,
        Amount = ParseMoney(root.GetProperty("amount")),
        Currency = root.GetProperty("amount").GetProperty("currency_code").GetString()!,
        ExpirationTime = ParseDate(root, "expiration_time")
    };

    private static JsonObject Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal ParseMoney(JsonElement money) =>
        decimal.Parse(money.GetProperty("value").GetString()!, CultureInfo.InvariantCulture);

    private static decimal? TryParseMoney(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var money) && money.ValueKind == JsonValueKind.Object
            ? ParseMoney(money)
            : null;

    private static DateTimeOffset? ParseDate(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string? GetStringOrNull(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonObject? body,
        string? requestId, CancellationToken cancellationToken, bool isRetryAfterUnauthorized = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, $"{_settings.ApiBaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", $"{_runId}-{requestId}");
        }
        if (body != null)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            request.Content = new StringContent(body.ToJsonString(SerializerOptions), Encoding.UTF8, "application/json");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isRetryAfterUnauthorized)
        {
            InvalidateToken();
            return await SendAsync(method, path, body, requestId, cancellationToken, isRetryAfterUnauthorized: true);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string? name = null, issue = null, debugId = null, message = content;
            try
            {
                using var errorDoc = JsonDocument.Parse(content);
                var root = errorDoc.RootElement;
                name = GetStringOrNull(root, "name");
                debugId = GetStringOrNull(root, "debug_id");
                message = GetStringOrNull(root, "message") ?? content;
                if (root.TryGetProperty("details", out var details) && details.GetArrayLength() > 0)
                {
                    issue = GetStringOrNull(details[0], "issue");
                }
            }
            catch (JsonException)
            {
                // Non-JSON error body; keep the raw content as the message.
            }

            _logger.LogWarning(
                "PayPal call {Method} {Path} failed: {StatusCode} {ErrorName} {Issue} (debug_id {DebugId})",
                method, path, (int)response.StatusCode, name, issue, debugId);

            throw new PayPalApiException((int)response.StatusCode, name, issue, debugId,
                $"PayPal {method} {path} failed ({(int)response.StatusCode}): {name} {issue} - {message}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(content);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ApiBaseUrl}/v1/oauth2/token");
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8,
                "application/x-www-form-urlencoded");

            using var response = await client.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed: {StatusCode}", (int)response.StatusCode);
                throw new PayPalApiException((int)response.StatusCode, null, null, null,
                    $"PayPal token request failed ({(int)response.StatusCode}). Check PayPal:ClientId / PayPal:ClientSecret.");
            }

            using var doc = JsonDocument.Parse(content);
            _accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            // Refresh a minute early to avoid using a token at the edge of expiry.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
            return _accessToken;
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
}
