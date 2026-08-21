using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalPaymentsClient : IPayPalPaymentsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalPaymentsClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalPaymentsClient(HttpClient http, IOptions<PayPalOptions> options, ILogger<PayPalPaymentsClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency
    {
        get
        {
            EnsureConfigured();
            return _options.Currency.ToUpperInvariant();
        }
    }

    public async Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        string requestId,
        CardPaymentSource? card,
        string? vaultId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = BuildCardPaymentSource(card, vaultId);
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["custom_id"] = customId,
                    ["invoice_id"] = invoiceId,
                    ["amount"] = Money(currency, amount)
                }
            },
            ["payment_source"] = paymentSource
        };

        using var created = await SendJsonAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            body,
            requestId,
            cancellationToken);

        EnsureNoPayerAction(created.RootElement);

        var authorization = TryReadAuthorization(created.RootElement);
        if (authorization is not null)
        {
            return authorization;
        }

        var orderId = RequireString(created.RootElement, "id");
        using var authorized = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/checkout/orders/{orderId}/authorize",
            new Dictionary<string, object?> { ["payment_source"] = paymentSource },
            $"{requestId}-authorize",
            cancellationToken);

        EnsureNoPayerAction(authorized.RootElement);
        return TryReadAuthorization(authorized.RootElement)
            ?? throw new CheckoutException(502, "PayPal authorized the order but did not return an authorization id.");
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return ReadStandaloneAuthorization(doc.RootElement, authorizationId);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            new Dictionary<string, object?> { ["amount"] = Money(currency, amount) },
            requestId,
            cancellationToken);
        return ReadStandaloneAuthorization(doc.RootElement, authorizationId);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(currency, amount),
            ["final_capture"] = true
        };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken);

        var root = doc.RootElement;
        var captureId = RequireString(root, "id");
        var status = ReadString(root, "status") ?? "COMPLETED";
        var capturedAmount = ReadMoney(root, "amount") ?? amount;
        var capturedCurrency = ReadString(root, "amount", "currency_code") ?? currency;
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = ReadMoney(breakdown, "paypal_fee");
            net = ReadMoney(breakdown, "net_amount");
        }

        return new PayPalCaptureResult(captureId, status, capturedAmount, capturedCurrency, fee, net);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void");
        request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        using var response = await SendWithAuthAsync(request, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.OK)
        {
            return;
        }

        await ThrowPayPalError(response, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object body = amount.HasValue
            ? new Dictionary<string, object?> { ["amount"] = Money(currency, amount.Value) }
            : new Dictionary<string, object?>();

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken);

        var root = doc.RootElement;
        var refundId = RequireString(root, "id");
        var status = ReadString(root, "status") ?? "COMPLETED";
        var refunded = ReadMoney(root, "amount") ?? amount ?? 0m;
        var refundCurrency = ReadString(root, "amount", "currency_code") ?? currency;
        return new PayPalRefundResult(refundId, status, refunded, refundCurrency);
    }

    public async Task<PayPalVaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?>
            {
                ["id"] = paypalCustomerId,
                ["merchant_customer_id"] = SanitizeMerchantCustomerId(merchantCustomerId)
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardObject(card)
            }
        };

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, requestId, cancellationToken);
        var root = doc.RootElement;
        var tokenId = RequireString(root, "id");
        var customerId = ReadString(root, "customer", "id");
        string lastDigits = string.Empty, brand = string.Empty, expiry = string.Empty, name = card.Name;
        if (root.TryGetProperty("payment_source", out var source) && source.TryGetProperty("card", out var cardEl))
        {
            lastDigits = ReadString(cardEl, "last_digits") ?? LastDigits(card.Number);
            brand = ReadString(cardEl, "brand") ?? "CARD";
            expiry = ReadString(cardEl, "expiry") ?? card.Expiry;
            name = ReadString(cardEl, "name") ?? card.Name;
        }
        else
        {
            lastDigits = LastDigits(card.Number);
            brand = "CARD";
            expiry = card.Expiry;
        }

        return new PayPalVaultedCardResult(tokenId, lastDigits, brand, expiry, name, customerId);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}");
        using var response = await SendWithAuthAsync(request, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        await ThrowPayPalError(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var cursor = from.ToUniversalTime();
        var end = to.ToUniversalTime();
        if (end < cursor)
        {
            return results;
        }

        while (cursor <= end)
        {
            var windowEnd = cursor.AddDays(31);
            if (windowEnd > end)
            {
                windowEnd = end;
            }

            await AddWindowTransactions(results, cursor, windowEnd, cancellationToken);
            if (windowEnd == end)
            {
                break;
            }

            cursor = windowEnd;
        }

        return results;
    }

    private async Task AddWindowTransactions(
        List<PayPalReportedTransaction> results,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query = Query(
                ("start_date", FormatTimestamp(start)),
                ("end_date", FormatTimestamp(end)),
                ("fields", "all"),
                ("page_size", "100"),
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("balance_affecting_records_only", "N"));

            using var doc = await SendJsonAsync(HttpMethod.Get, $"/v1/reporting/transactions{query}", null, null, cancellationToken);
            var root = doc.RootElement;
            totalPages = root.TryGetProperty("total_pages", out var pagesEl) && pagesEl.TryGetInt32(out var pages) ? pages : 1;
            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in details.EnumerateArray())
                {
                    var info = item.TryGetProperty("transaction_info", out var txnInfo) ? txnInfo : item;
                    var id = ReadString(info, "transaction_id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction(
                        id,
                        ReadString(info, "paypal_reference_id"),
                        ReadString(info, "invoice_id"),
                        ReadString(info, "custom_field"),
                        ReadString(info, "transaction_event_code"),
                        ReadString(info, "transaction_status"),
                        ReadMoney(info, "transaction_amount"),
                        ReadString(info, "transaction_amount", "currency_code"),
                        ReadTimestamp(info, "transaction_initiation_date")));
                }
            }

            page++;
        } while (page <= totalPages);
    }

    private static Dictionary<string, object?> BuildCardPaymentSource(CardPaymentSource? card, string? vaultId)
    {
        if (!string.IsNullOrWhiteSpace(vaultId))
        {
            return new Dictionary<string, object?>
            {
                ["card"] = new Dictionary<string, object?>
                {
                    ["vault_id"] = vaultId,
                    ["stored_credential"] = new Dictionary<string, object?>
                    {
                        ["payment_initiator"] = "CUSTOMER",
                        ["payment_type"] = "UNSCHEDULED",
                        ["usage"] = "SUBSEQUENT"
                    }
                }
            };
        }

        if (card is null)
        {
            throw new CheckoutException(400, "Card details or a saved payment method are required.");
        }

        return new Dictionary<string, object?> { ["card"] = BuildCardObject(card) };
    }

    private static Dictionary<string, object?> BuildCardObject(CardPaymentSource card)
    {
        var cardObject = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name
        };

        if (card.BillingAddress is not null)
        {
            cardObject["billing_address"] = new Dictionary<string, object?>
            {
                ["country_code"] = card.BillingAddress.CountryCode,
                ["address_line_1"] = card.BillingAddress.AddressLine1,
                ["address_line_2"] = card.BillingAddress.AddressLine2,
                ["admin_area_2"] = card.BillingAddress.AdminArea2,
                ["admin_area_1"] = card.BillingAddress.AdminArea1,
                ["postal_code"] = card.BillingAddress.PostalCode
            };
        }

        return cardObject;
    }

    private PayPalAuthorizationResult? TryReadAuthorization(JsonElement root)
    {
        var orderId = ReadString(root, "id");
        var orderStatus = ReadString(root, "status");
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return null;
        }

        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments) ||
                !payments.TryGetProperty("authorizations", out var auths) ||
                auths.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var auth in auths.EnumerateArray())
            {
                var authId = ReadString(auth, "id");
                if (string.IsNullOrWhiteSpace(authId))
                {
                    continue;
                }

                return new PayPalAuthorizationResult(
                    orderId,
                    orderStatus ?? "COMPLETED",
                    authId,
                    ReadString(auth, "status") ?? "CREATED",
                    ReadMoney(auth, "amount") ?? 0m,
                    ReadString(auth, "amount", "currency_code") ?? Currency,
                    ReadTimestamp(auth, "create_time"),
                    ReadTimestamp(auth, "expiration_time"));
            }
        }

        return null;
    }

    private PayPalAuthorizationResult ReadStandaloneAuthorization(JsonElement root, string fallbackId)
    {
        var authId = ReadString(root, "id") ?? fallbackId;
        var relatedOrder = ReadString(root, "supplementary_data", "related_ids", "order_id") ?? string.Empty;
        return new PayPalAuthorizationResult(
            relatedOrder,
            ReadString(root, "status") ?? "CREATED",
            authId,
            ReadString(root, "status") ?? "CREATED",
            ReadMoney(root, "amount") ?? 0m,
            ReadString(root, "amount", "currency_code") ?? Currency,
            ReadTimestamp(root, "create_time"),
            ReadTimestamp(root, "expiration_time"));
    }

    private static void EnsureNoPayerAction(JsonElement root)
    {
        var status = ReadString(root, "status");
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(ReadString(root, "id") ?? string.Empty);
        }
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await SendWithAuthAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToCheckoutException(response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(payload);
    }

    private async Task<HttpResponseMessage> SendWithAuthAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        request.RequestUri = request.RequestUri is { IsAbsoluteUri: true }
            ? request.RequestUri
            : new Uri(_options.GetApiBaseUrl() + request.RequestUri!.OriginalString);

        var token = await GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        InvalidateToken();
        throw new CheckoutException(502, "PayPal rejected the access token. Retry the request.");
    }

    private async Task ThrowPayPalError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        throw ToCheckoutException(response.StatusCode, payload);
    }

    private CheckoutException ToCheckoutException(System.Net.HttpStatusCode statusCode, string payload)
    {
        var status = (int)statusCode;
        string? name = null, message = null, debugId = null, issue = null, description = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            var root = doc.RootElement;
            name = ReadString(root, "name");
            message = ReadString(root, "message");
            debugId = ReadString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    issue = ReadString(detail, "issue");
                    description = ReadString(detail, "description");
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // PayPal sometimes returns non-JSON on gateway failures.
        }

        _logger.LogWarning("PayPal request failed. Status {Status} name {Name} debug_id {DebugId} issue {Issue}",
            status, name, debugId, issue);

        var mapped = status is >= 400 and < 500 ? status : 502;
        var text = description ?? message ?? "PayPal request failed.";
        if (!string.IsNullOrWhiteSpace(issue))
        {
            text = $"{issue}: {text}";
        }

        if (!string.IsNullOrWhiteSpace(debugId))
        {
            text = $"{text} (PayPal debug_id {debugId})";
        }

        return new CheckoutException(mapped, text);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return _accessToken!;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return _accessToken!;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.GetApiBaseUrl() + "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToCheckoutException(response.StatusCode, payload);
            }

            using var doc = JsonDocument.Parse(payload);
            _accessToken = RequireString(doc.RootElement, "access_token");
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds)
                ? seconds
                : 300;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
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

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new CheckoutException(500, "PayPal is not configured. Set PayPal:ClientId, PayPal:ClientSecret, and PayPal:Currency.");
        }
    }

    private static Dictionary<string, string> Money(string currency, decimal amount) => new()
    {
        ["currency_code"] = currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static string Query(params (string Key, string Value)[] pairs)
    {
        var q = string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return "?" + q;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string LastDigits(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var cleaned = new string(buyerId.Where(ch => char.IsLetterOrDigit(ch) || "-_.^*$@#".Contains(ch)).ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "customer";
        }

        return cleaned.Length <= 64 ? cleaned : cleaned[..64];
    }

    private static string RequireString(JsonElement element, string name) =>
        ReadString(element, name) ?? throw new CheckoutException(502, $"PayPal response was missing '{name}'.");

    private static string? ReadString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind is JsonValueKind.String or JsonValueKind.Number ? current.ToString() : null;
    }

    private static decimal? ReadMoney(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var money))
        {
            return null;
        }

        if (money.ValueKind == JsonValueKind.Object)
        {
            var value = ReadString(money, "value");
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }

        return decimal.TryParse(money.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var direct) ? direct : null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string name)
    {
        var value = ReadString(element, name);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
