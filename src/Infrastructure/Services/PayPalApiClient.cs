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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// PayPal Payments API client (Orders v2, Payments v2, Vault v3, Transaction Search v1).
/// Full card details pass through to PayPal only; they are never logged here and never
/// persisted. Error logs carry PayPal's error name/issue/debug id only.
/// </summary>
public class PayPalApiClient : IPayPalClient
{
    private const string TokenCacheKey = "paypal:oauth2:access-token";
    private static readonly TimeSpan TokenExpirySafetyMargin = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxTransactionSearchWindow = TimeSpan.FromDays(31);
    private const int TransactionSearchPageSize = 100;

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalApiClient> _logger;

    public PayPalApiClient(HttpClient httpClient, IMemoryCache cache, IOptions<PayPalSettings> settings, ILogger<PayPalApiClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(e.g. from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables via user-secrets).");
        }
    }

    private string BaseUrl => !string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? _settings.BaseUrl!.TrimEnd('/')
        : string.Equals(_settings.Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PayPal token request failed with status {StatusCode}", response.StatusCode);
            throw new PayPalApiException(response.StatusCode, null, null, null,
                $"PayPal credential/token request failed with status {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        _cache.Set(TokenCacheKey, token, DateTimeOffset.UtcNow.AddSeconds(expiresIn).Subtract(TokenExpirySafetyMargin));
        return token;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonObject? body, string? requestId, CancellationToken cancellationToken, bool retryOnUnauthorized = true)
    {
        var token = await GetAccessTokenAsync(forceRefresh: false, cancellationToken);

        using var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && retryOnUnauthorized)
        {
            await GetAccessTokenAsync(forceRefresh: true, cancellationToken);
            return await SendAsync(method, path, body, requestId, cancellationToken, retryOnUnauthorized: false);
        }

        if (!response.IsSuccessStatusCode)
        {
            string? name = null, message = null, debugId = null, issue = null;
            try
            {
                using var errorDoc = JsonDocument.Parse(responseBody);
                var root = errorDoc.RootElement;
                name = GetString(root, "name");
                message = GetString(root, "message");
                debugId = GetString(root, "debug_id");
                if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
                {
                    issue = GetString(details[0], "issue");
                }
            }
            catch (JsonException)
            {
                // Non-JSON error body; the status code is all we log.
            }

            _logger.LogWarning("PayPal {Method} {Path} failed: {StatusCode} {ErrorName} {Issue} (debug id {DebugId})",
                method, path, (int)response.StatusCode, name, issue, debugId);
            throw new PayPalApiException(response.StatusCode, name, issue, debugId,
                $"PayPal {method} {path} failed ({(int)response.StatusCode}): {message ?? name ?? response.ReasonPhrase}");
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return JsonDocument.Parse("{}");
        }
        return JsonDocument.Parse(responseBody);
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string customId, string invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["amount"] = new JsonObject
                    {
                        ["currency_code"] = currency,
                        ["value"] = Money(amount)
                    },
                    ["custom_id"] = customId,
                    ["invoice_id"] = invoiceId
                }
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken);
        return new PayPalOrderResult(
            doc.RootElement.GetProperty("id").GetString()!,
            GetString(doc.RootElement, "status") ?? "UNKNOWN");
    }

    public async Task<PayPalAuthorizeResult> AuthorizeOrderAsync(string payPalOrderId, CardPaymentSource? card, string? vaultId, string requestId, CancellationToken cancellationToken = default)
    {
        JsonObject cardSource;
        if (card is not null)
        {
            cardSource = BuildCardJson(card);
        }
        else
        {
            cardSource = new JsonObject
            {
                ["vault_id"] = vaultId,
                ["stored_credential"] = new JsonObject
                {
                    ["payment_initiator"] = "CUSTOMER",
                    ["payment_type"] = "UNSCHEDULED"
                }
            };
        }

        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = cardSource }
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", body, requestId, cancellationToken);
        var root = doc.RootElement;

        string? authorizationId = null, authorizationStatus = null, resultCurrency = null;
        decimal? amount = null;
        DateTimeOffset? expiration = null;

        if (root.TryGetProperty("purchase_units", out var units) && units.GetArrayLength() > 0 &&
            units[0].TryGetProperty("payments", out var payments) &&
            payments.TryGetProperty("authorizations", out var authorizations) &&
            authorizations.GetArrayLength() > 0)
        {
            var auth = authorizations[0];
            authorizationId = GetString(auth, "id");
            authorizationStatus = GetString(auth, "status");
            expiration = GetDate(auth, "expiration_time");
            if (auth.TryGetProperty("amount", out var amountElement))
            {
                amount = GetDecimal(amountElement, "value");
                resultCurrency = GetString(amountElement, "currency_code");
            }
        }

        string? cardBrand = null, cardLastDigits = null;
        if (root.TryGetProperty("payment_source", out var paymentSource) &&
            paymentSource.TryGetProperty("card", out var cardElement))
        {
            cardBrand = GetString(cardElement, "brand");
            cardLastDigits = GetString(cardElement, "last_digits");
        }

        var (requiresAction, actionUrl) = GetBuyerAction(root);

        return new PayPalAuthorizeResult(
            GetString(root, "id") ?? payPalOrderId,
            GetString(root, "status") ?? "UNKNOWN",
            authorizationId,
            authorizationStatus,
            amount,
            resultCurrency,
            expiration,
            requiresAction,
            actionUrl,
            cardBrand,
            cardLastDigits);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return ParseAuthorization(doc.RootElement);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["currency_code"] = currency,
                ["value"] = Money(amount)
            },
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId, cancellationToken);
        return ParseCapture(doc.RootElement, currency);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["currency_code"] = currency,
                ["value"] = Money(amount)
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, cancellationToken);
        return ParseAuthorization(doc.RootElement);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", new JsonObject(), requestId, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string? noteToPayer, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (amount.HasValue)
        {
            body["amount"] = new JsonObject
            {
                ["currency_code"] = currency,
                ["value"] = Money(amount.Value)
            };
        }
        if (!string.IsNullOrEmpty(noteToPayer))
        {
            body["note_to_payer"] = noteToPayer;
        }

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, requestId, cancellationToken);
        var root = doc.RootElement;
        decimal refundAmount = amount ?? 0m;
        string refundCurrency = currency;
        if (root.TryGetProperty("amount", out var amountElement))
        {
            refundAmount = GetDecimal(amountElement, "value") ?? refundAmount;
            refundCurrency = GetString(amountElement, "currency_code") ?? refundCurrency;
        }
        return new PayPalRefundResult(
            root.GetProperty("id").GetString()!,
            GetString(root, "status") ?? "UNKNOWN",
            refundAmount,
            refundCurrency);
    }

    public async Task<PayPalSetupTokenResult> CreateSetupTokenAsync(CardPaymentSource card, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = BuildCardJson(card) }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", body, requestId, cancellationToken);
        var (requiresAction, actionUrl) = GetBuyerAction(doc.RootElement);
        return new PayPalSetupTokenResult(
            doc.RootElement.GetProperty("id").GetString()!,
            GetString(doc.RootElement, "status") ?? "UNKNOWN",
            requiresAction,
            actionUrl);
    }

    public async Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string setupTokenId, string merchantCustomerId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject
                {
                    ["id"] = setupTokenId,
                    ["type"] = "SETUP_TOKEN"
                }
            },
            ["customer"] = new JsonObject
            {
                ["merchant_customer_id"] = merchantCustomerId
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, cancellationToken);
        var root = doc.RootElement;

        string? brand = null, lastDigits = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var paymentSource) &&
            paymentSource.TryGetProperty("card", out var card))
        {
            brand = GetString(card, "brand");
            lastDigits = GetString(card, "last_digits");
            expiry = GetString(card, "expiry");
            name = GetString(card, "name");
        }

        return new PayPalPaymentTokenResult(root.GetProperty("id").GetString()!, brand, lastDigits, expiry, name);
    }

    public async Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransactionRecord>();

        // The API window is limited (max 31 days); chunk the requested range, then page
        // through every page of each chunk so the whole range is covered.
        for (var windowStart = from; windowStart < to; windowStart = windowStart.Add(MaxTransactionSearchWindow))
        {
            var windowEnd = windowStart.Add(MaxTransactionSearchWindow);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            while (true)
            {
                var path = "/v1/reporting/transactions" +
                           $"?start_date={Uri.EscapeDataString(Timestamp(windowStart))}" +
                           $"&end_date={Uri.EscapeDataString(Timestamp(windowEnd))}" +
                           $"&fields=all&page_size={TransactionSearchPageSize}&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var root = doc.RootElement;

                var returned = 0;
                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    returned = details.GetArrayLength();
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (detail.TryGetProperty("transaction_info", out var info))
                        {
                            results.Add(ParseTransaction(info));
                        }
                    }
                }

                var totalPages = GetInt(root, "total_pages");
                if (totalPages.HasValue)
                {
                    if (page >= totalPages.Value)
                    {
                        break;
                    }
                }
                else if (returned < TransactionSearchPageSize)
                {
                    break;
                }

                page++;
            }
        }

        return results;
    }

    private static PayPalTransactionRecord ParseTransaction(JsonElement info)
    {
        decimal? amount = null, fee = null;
        string? currency = null;
        if (info.TryGetProperty("transaction_amount", out var amountElement))
        {
            amount = GetDecimal(amountElement, "value");
            currency = GetString(amountElement, "currency_code");
        }
        if (info.TryGetProperty("fee_amount", out var feeElement))
        {
            fee = GetDecimal(feeElement, "value");
        }

        return new PayPalTransactionRecord(
            GetString(info, "transaction_id") ?? string.Empty,
            GetString(info, "transaction_event_code"),
            GetString(info, "transaction_status"),
            amount,
            currency,
            fee,
            GetString(info, "invoice_id"),
            GetString(info, "custom_field"),
            GetString(info, "paypal_reference_id"),
            GetString(info, "paypal_reference_id_type"),
            GetDate(info, "transaction_initiation_date"),
            GetDate(info, "transaction_updated_date"));
    }

    private static PayPalAuthorizationDetails ParseAuthorization(JsonElement root)
    {
        decimal amount = 0m;
        var currency = string.Empty;
        if (root.TryGetProperty("amount", out var amountElement))
        {
            amount = GetDecimal(amountElement, "value") ?? 0m;
            currency = GetString(amountElement, "currency_code") ?? string.Empty;
        }
        return new PayPalAuthorizationDetails(
            root.GetProperty("id").GetString()!,
            GetString(root, "status") ?? "UNKNOWN",
            amount,
            currency,
            GetDate(root, "expiration_time"));
    }

    private static PayPalCaptureResult ParseCapture(JsonElement root, string fallbackCurrency)
    {
        decimal amount = 0m;
        var currency = fallbackCurrency;
        if (root.TryGetProperty("amount", out var amountElement))
        {
            amount = GetDecimal(amountElement, "value") ?? 0m;
            currency = GetString(amountElement, "currency_code") ?? fallbackCurrency;
        }

        decimal? fee = null, net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("paypal_fee", out var feeElement))
            {
                fee = GetDecimal(feeElement, "value");
            }
            if (breakdown.TryGetProperty("net_amount", out var netElement))
            {
                net = GetDecimal(netElement, "value");
            }
        }

        return new PayPalCaptureResult(
            root.GetProperty("id").GetString()!,
            GetString(root, "status") ?? "UNKNOWN",
            amount,
            currency,
            fee,
            net);
    }

    private static JsonObject BuildCardJson(CardPaymentSource card)
    {
        var json = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrEmpty(card.SecurityCode))
        {
            json["security_code"] = card.SecurityCode;
        }
        if (!string.IsNullOrEmpty(card.Name))
        {
            json["name"] = card.Name;
        }
        if (card.BillingAddress is not null)
        {
            var address = new JsonObject
            {
                ["country_code"] = card.BillingAddress.CountryCode
            };
            if (!string.IsNullOrEmpty(card.BillingAddress.AddressLine1)) address["address_line_1"] = card.BillingAddress.AddressLine1;
            if (!string.IsNullOrEmpty(card.BillingAddress.AddressLine2)) address["address_line_2"] = card.BillingAddress.AddressLine2;
            if (!string.IsNullOrEmpty(card.BillingAddress.City)) address["admin_area_2"] = card.BillingAddress.City;
            if (!string.IsNullOrEmpty(card.BillingAddress.State)) address["admin_area_1"] = card.BillingAddress.State;
            if (!string.IsNullOrEmpty(card.BillingAddress.PostalCode)) address["postal_code"] = card.BillingAddress.PostalCode;
            json["billing_address"] = address;
        }
        return json;
    }

    private static (bool RequiresAction, string? Url) GetBuyerAction(JsonElement root)
    {
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(GetString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, GetString(link, "href"));
                }
            }
        }
        return (false, null);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? GetDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetDecimal(out var parsed) => parsed,
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? GetDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
