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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private const string TokenCacheKey = "paypal:access_token";
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public PayPalGateway(
        HttpClient http,
        IOptions<PayPalOptions> options,
        IMemoryCache cache,
        ILogger<PayPalGateway> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string customId,
        string requestId,
        CardDetails card,
        CancellationToken cancellationToken)
    {
        var paymentSource = new PayPalPaymentSource
        {
            Card = new PayPalCardPaymentSource
            {
                Name = card.Name,
                Number = NormalizeCardNumber(card.Number),
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = MapAddress(card.BillingAddress),
                Attributes = new PayPalCardAttributes
                {
                    Verification = new PayPalCardVerification { Method = "SCA_WHEN_REQUIRED" }
                }
            }
        };

        return AuthorizeAsync(amount, currency, invoiceId, customId, requestId, paymentSource, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string customId,
        string requestId,
        string vaultId,
        CancellationToken cancellationToken)
    {
        var paymentSource = new PayPalPaymentSource
        {
            Card = new PayPalCardPaymentSource
            {
                VaultId = vaultId,
                StoredCredential = new PayPalStoredCredential()
            }
        };

        return AuthorizeAsync(amount, currency, invoiceId, customId, requestId, paymentSource, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        using var doc = await SendAsync(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            sensitive: false,
            cancellationToken);

        var root = doc.RootElement;
        return new PayPalAuthorizationDetails(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            ParseTime(OptionalString(root, "expiration_time")),
            ParseMoney(root, "amount"));
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        string paypalOrderId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken)
    {
        var body = new PayPalReauthorizeRequest
        {
            Amount = new PayPalMoney
            {
                CurrencyCode = currency,
                Value = FormatAmount(amount, currency)
            }
        };

        using var doc = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            sensitive: false,
            cancellationToken);

        var root = doc.RootElement;
        return new PayPalAuthorizationResult(
            paypalOrderId,
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            ParseTime(OptionalString(root, "expiration_time")),
            ParseMoney(root, "amount"));
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var body = new PayPalCaptureRequest
        {
            Amount = new PayPalMoney
            {
                CurrencyCode = currency,
                Value = FormatAmount(amount, currency)
            },
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        using var doc = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            sensitive: false,
            cancellationToken);

        var root = doc.RootElement;
        var captured = ParseMoney(root, "amount");
        var fee = 0m;
        var net = captured;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = ParseMoney(breakdown, "paypal_fee");
            var parsedNet = ParseMoney(breakdown, "net_amount");
            if (parsedNet > 0)
            {
                net = parsedNet;
            }
        }

        return new PayPalCaptureResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            captured,
            fee,
            net);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(
                HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/void",
                body: new { },
                requestId,
                sensitive: false,
                cancellationToken,
                allowEmpty: true);
        }
        catch (PayPalApiException ex) when (
            string.Equals(ex.Issue, "AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ex.Issue, "PREVIOUSLY_VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("PayPal authorization {AuthorizationId} was already voided.", authorizationId);
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken)
    {
        object body;
        if (amount.HasValue)
        {
            body = new PayPalRefundRequest
            {
                Amount = new PayPalMoney
                {
                    CurrencyCode = currency,
                    Value = FormatAmount(amount.Value, currency)
                }
            };
        }
        else
        {
            body = new PayPalRefundRequest();
        }

        using var doc = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            sensitive: false,
            cancellationToken);

        var root = doc.RootElement;
        return new PayPalRefundResult(
            RequiredString(root, "id"),
            RequiredString(root, "status"),
            ParseMoney(root, "amount"));
    }

    public async Task<PayPalVaultedCardResult> VaultCardAsync(
        CardDetails card,
        string merchantCustomerId,
        string? paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var body = new PayPalVaultRequest
        {
            PaymentSource = new PayPalVaultPaymentSource
            {
                Card = new PayPalVaultCard
                {
                    Name = card.Name,
                    Number = NormalizeCardNumber(card.Number),
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            },
            Customer = new PayPalVaultCustomer
            {
                Id = string.IsNullOrWhiteSpace(paypalCustomerId) ? null : paypalCustomerId,
                MerchantCustomerId = merchantCustomerId
            }
        };

        using var doc = await SendAsync(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            body,
            requestId,
            sensitive: true,
            cancellationToken);

        var root = doc.RootElement;
        ThrowIfPayerActionRequired(root, "vaulting a card");

        string? brand = null;
        string? lastDigits = null;
        string? expiry = null;
        string? cardholderName = null;
        if (root.TryGetProperty("payment_source", out var source) &&
            source.TryGetProperty("card", out var cardEl))
        {
            brand = OptionalString(cardEl, "brand");
            lastDigits = OptionalString(cardEl, "last_digits");
            expiry = OptionalString(cardEl, "expiry");
            cardholderName = OptionalString(cardEl, "name");
        }

        string? customerId = paypalCustomerId;
        if (root.TryGetProperty("customer", out var customer))
        {
            customerId = OptionalString(customer, "id") ?? customerId;
        }

        return new PayPalVaultedCardResult(
            RequiredString(root, "id"),
            customerId,
            brand,
            lastDigits,
            expiry,
            cardholderName);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(
                HttpMethod.Delete,
                $"v3/vault/payment-tokens/{paymentTokenId}",
                body: null,
                requestId: null,
                sensitive: false,
                cancellationToken,
                allowEmpty: true);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("PayPal payment token was already deleted.");
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from.ToUniversalTime();
        var end = to.ToUniversalTime();

        while (windowStart <= end)
        {
            var windowEnd = windowStart.AddDays(30);
            if (windowEnd > end)
            {
                windowEnd = end;
            }

            await AddWindowAsync(results, windowStart, windowEnd, cancellationToken);
            if (windowEnd == end)
            {
                break;
            }

            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task AddWindowAsync(
        List<PayPalReportedTransaction> results,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var startDate = FormatPayPalTime(start);
            var endDate = FormatPayPalTime(end);
            var path =
                "v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(startDate)}" +
                $"&end_date={Uri.EscapeDataString(endDate)}" +
                "&fields=all" +
                "&balance_affecting_records_only=N" +
                "&page_size=500" +
                $"&page={page}";

            using var doc = await SendAsync(
                HttpMethod.Get,
                path,
                body: null,
                requestId: null,
                sensitive: false,
                cancellationToken);

            var root = doc.RootElement;
            totalPages = root.TryGetProperty("total_pages", out var pagesEl) && pagesEl.TryGetInt32(out var pages)
                ? pages
                : 1;

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in details.EnumerateArray())
                {
                    results.Add(MapReportedTransaction(item));
                }
            }

            page++;
        } while (page <= totalPages);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string customId,
        string requestId,
        PayPalPaymentSource paymentSource,
        CancellationToken cancellationToken)
    {
        var createBody = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            {
                new PayPalPurchaseUnit
                {
                    ReferenceId = "default",
                    InvoiceId = invoiceId,
                    CustomId = customId,
                    Description = $"eShopOnWeb order {customId}",
                    Amount = new PayPalAmount
                    {
                        CurrencyCode = currency,
                        Value = FormatAmount(amount, currency)
                    }
                }
            }
        };

        using var created = await SendAsync(
            HttpMethod.Post,
            "v2/checkout/orders",
            createBody,
            requestId,
            sensitive: false,
            cancellationToken);

        var createdRoot = created.RootElement;
        ThrowIfPayerActionRequired(createdRoot, "creating the PayPal order");
        var paypalOrderId = RequiredString(createdRoot, "id");

        var authorizeBody = new PayPalAuthorizeRequest { PaymentSource = paymentSource };
        using var authorized = await SendAsync(
            HttpMethod.Post,
            $"v2/checkout/orders/{paypalOrderId}/authorize",
            authorizeBody,
            $"{requestId}-authorize",
            sensitive: true,
            cancellationToken);

        var authRoot = authorized.RootElement;
        ThrowIfPayerActionRequired(authRoot, "authorizing the card payment");

        var authorization = GetAuthorizationElement(authRoot)
            ?? throw new PaymentException("PayPal authorized the order but did not return an authorization id.");

        var status = RequiredString(authorization, "status");
        if (string.Equals(status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException("PayPal denied the card authorization.");
        }

        var authorizedAmount = ParseMoney(authorization, "amount");
        if (authorizedAmount != amount)
        {
            throw new PaymentException(
                $"PayPal held {authorizedAmount} {currency} but the order total is {amount} {currency}.");
        }

        return new PayPalAuthorizationResult(
            paypalOrderId,
            RequiredString(authorization, "id"),
            status,
            ParseTime(OptionalString(authorization, "expiration_time")),
            authorizedAmount);
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId,
        bool sensitive,
        CancellationToken cancellationToken,
        bool allowEmpty = false)
    {
        EnsureConfigured();
        var token = await GetAccessTokenAsync(cancellationToken);
        return await SendWithTokenAsync(method, relativePath, body, requestId, sensitive, token, cancellationToken, allowEmpty, retryOnUnauthorized: true);
    }

    private async Task<JsonDocument> SendWithTokenAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId,
        bool sensitive,
        string token,
        CancellationToken cancellationToken,
        bool allowEmpty,
        bool retryOnUnauthorized)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body is not null && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, PayPalJson.Options),
                Encoding.UTF8,
                "application/json");
        }

        if (sensitive)
        {
            _logger.LogDebug("PayPal {Method} {Path} includes a payment source; request body is not logged.", method, RedactPath(relativePath));
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal {Method} {Path} failed to send.", method, RedactPath(relativePath));
            throw new PaymentException($"Unable to reach PayPal ({method} {RedactPath(relativePath)}).", ex);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation(
            "PayPal {Method} {Path} returned {StatusCode}",
            method,
            RedactPath(relativePath),
            (int)response.StatusCode);

        if (response.StatusCode == HttpStatusCode.Unauthorized && retryOnUnauthorized)
        {
            _cache.Remove(TokenCacheKey);
            var fresh = await GetAccessTokenAsync(cancellationToken);
            return await SendWithTokenAsync(method, relativePath, body, requestId, sensitive, fresh, cancellationToken, allowEmpty, retryOnUnauthorized: false);
        }

        if ((int)response.StatusCode >= 400)
        {
            var apiEx = PayPalApiException.FromResponse(response.StatusCode, responseBody);
            _logger.LogWarning(
                "PayPal {Method} {Path} error {StatusCode} debug_id={DebugId} issue={Issue}",
                method,
                RedactPath(relativePath),
                (int)response.StatusCode,
                apiEx.DebugId,
                apiEx.Issue);
            throw MapException(apiEx);
        }

        if (allowEmpty && string.IsNullOrWhiteSpace(responseBody))
        {
            return JsonDocument.Parse("{}");
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return JsonDocument.Parse("{}");
        }

        try
        {
            return JsonDocument.Parse(responseBody);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "PayPal returned non-JSON for {Path}", RedactPath(relativePath));
            throw new PaymentException("PayPal returned a response that was not valid JSON.");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(TokenCacheKey, out cached) && !string.IsNullOrEmpty(cached))
            {
                return cached;
            }

            EnsureConfigured();
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new PaymentException("Unable to reach PayPal to request an access token.", ex);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var apiEx = PayPalApiException.FromResponse(response.StatusCode, body);
                _logger.LogWarning("PayPal token request failed {StatusCode} debug_id={DebugId}", (int)response.StatusCode, apiEx.DebugId);
                throw new PaymentException("PayPal authentication failed. Check PayPal:ClientId and PayPal:ClientSecret.");
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(body, PayPalJson.Options)
                ?? throw new PaymentException("PayPal token response was empty.");
            if (string.IsNullOrEmpty(token.AccessToken))
            {
                throw new PaymentException("PayPal token response did not include an access_token.");
            }

            var lifetime = TimeSpan.FromSeconds(Math.Max(30, token.ExpiresIn - 60));
            _cache.Set(TokenCacheKey, token.AccessToken, lifetime);
            return token.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PaymentException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
        }

        if (_http.BaseAddress is null)
        {
            throw new PaymentException("PayPal HTTP client BaseAddress is not configured.");
        }
    }

    private static PaymentException MapException(PayPalApiException ex)
    {
        if (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new PaymentNotFoundException(ex.Message);
        }

        if (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return new PaymentConflictException(ex.Message);
        }

        if ((int)ex.StatusCode == 422 || ex.StatusCode == HttpStatusCode.BadRequest)
        {
            return new PaymentValidationException(ex.Message);
        }

        return new PaymentException(ex.Message, ex);
    }

    private static void ThrowIfPayerActionRequired(JsonElement resource, string operation)
    {
        if (resource.TryGetProperty("status", out var status) &&
            string.Equals(status.GetString(), "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                $"PayPal required a shopper approval step while {operation}. A browser challenge is not supported by this integration.");
        }

        if (HasRel(resource, "payer-action"))
        {
            throw new PayerActionRequiredException(
                $"PayPal returned a payer-action link while {operation}. A browser challenge is not supported by this integration.");
        }

        if (resource.TryGetProperty("payment_source", out var source) &&
            source.TryGetProperty("card", out var card) &&
            card.TryGetProperty("authentication_result", out var auth) &&
            auth.TryGetProperty("three_d_secure", out var threeDs) &&
            threeDs.TryGetProperty("authentication_status", out var authStatus) &&
            string.Equals(authStatus.GetString(), "C", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                $"PayPal required a 3-D Secure challenge while {operation}. A browser challenge is not supported by this integration.");
        }
    }

    private static bool HasRel(JsonElement resource, string rel)
    {
        if (!resource.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var link in links.EnumerateArray())
        {
            if (link.TryGetProperty("rel", out var relEl) &&
                string.Equals(relEl.GetString(), rel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonElement? GetAuthorizationElement(JsonElement order)
    {
        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array || units.GetArrayLength() == 0)
        {
            return null;
        }

        var unit = units[0];
        if (!unit.TryGetProperty("payments", out var payments))
        {
            return null;
        }

        if (!payments.TryGetProperty("authorizations", out var auths) || auths.ValueKind != JsonValueKind.Array || auths.GetArrayLength() == 0)
        {
            return null;
        }

        return auths[0];
    }

    private static PayPalReportedTransaction MapReportedTransaction(JsonElement item)
    {
        JsonElement info = item;
        if (item.TryGetProperty("transaction_info", out var nested))
        {
            info = nested;
        }

        return new PayPalReportedTransaction(
            OptionalString(info, "transaction_id"),
            OptionalString(info, "paypal_reference_id"),
            OptionalString(info, "invoice_id"),
            OptionalString(info, "custom_field"),
            OptionalString(info, "transaction_event_code"),
            OptionalString(info, "transaction_status"),
            TryParseMoney(info, "transaction_amount"),
            info.TryGetProperty("transaction_amount", out var amt) ? OptionalString(amt, "currency_code") : null,
            ParseTime(OptionalString(info, "transaction_initiation_date")));
    }

    private static PayPalBillingAddress MapAddress(CardBillingAddress address) => new()
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.AdminArea2,
        AdminArea1 = address.AdminArea1,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode
    };

    private static string NormalizeCardNumber(string number) =>
        number.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

    internal static string FormatAmount(decimal amount, string currency)
    {
        if (ZeroDecimalCurrencies.Contains(currency))
        {
            return decimal.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatPayPalTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string RequiredString(JsonElement element, string name)
    {
        var value = OptionalString(element, name);
        if (string.IsNullOrEmpty(value))
        {
            throw new PaymentException($"PayPal response was missing '{name}'.");
        }

        return value;
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static decimal ParseMoney(JsonElement parent, string name)
    {
        return TryParseMoney(parent, name) ?? 0m;
    }

    private static decimal? TryParseMoney(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var money))
        {
            return null;
        }

        if (money.ValueKind == JsonValueKind.Object && money.TryGetProperty("value", out var valueEl))
        {
            if (decimal.TryParse(valueEl.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string RedactPath(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? path[..q] : path;
    }
}
