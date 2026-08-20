using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalApiClient : IPayPalPaymentsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "HUF", "TWD"
    };

    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalApiClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalApiClient(HttpClient http, IOptions<PayPalOptions> options, ILogger<PayPalApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public string Currency
    {
        get
        {
            EnsureConfigured();
            return _options.Currency.ToUpperInvariant();
        }
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        CardPaymentDetails card,
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new JsonObject
        {
            ["card"] = BuildCardObject(card)
        };
        return AuthorizeAsync(orderId, amount, paymentSource, invoiceId, $"eshop-pay-card-{orderId}-{invoiceId}", cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string vaultId,
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new JsonObject
        {
            ["card"] = new JsonObject { ["vault_id"] = vaultId }
        };
        return AuthorizeAsync(orderId, amount, paymentSource, invoiceId, $"eshop-pay-vault-{orderId}-{invoiceId}", cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            idempotencyKey: null,
            preferRepresentation: false,
            cancellationToken);

        var auth = Deserialize<PayPalAuthorizationResource>(json)
                   ?? throw new PayPalClientException("PayPal returned an empty authorization.");
        EnsureNotPayerAction(auth.Status, json);
        return ToAuthorizationDetails(auth);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = Money(amount)
        };

        var json = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        var auth = Deserialize<PayPalAuthorizationResource>(json)
                   ?? throw new PayPalClientException("PayPal returned an empty reauthorization.");
        return new PayPalAuthorizationResult(
            PayPalOrderId: string.Empty,
            AuthorizationId: auth.Id ?? throw new PayPalClientException("PayPal reauthorization did not include an id."),
            Status: auth.Status ?? "CREATED",
            CreateTime: ParseTime(auth.CreateTime),
            ExpirationTime: ParseTime(auth.ExpirationTime));
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = Money(amount),
            ["final_capture"] = true
        };

        var json = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        var capture = Deserialize<PayPalCaptureResource>(json)
                      ?? throw new PayPalClientException("PayPal returned an empty capture.");
        var captured = ParseAmount(capture.Amount?.Value)
                       ?? ParseAmount(capture.SellerReceivableBreakdown?.GrossAmount?.Value)
                       ?? amount;
        var fee = ParseAmount(capture.SellerReceivableBreakdown?.PaypalFee?.Value) ?? 0m;
        var net = ParseAmount(capture.SellerReceivableBreakdown?.NetAmount?.Value) ?? (captured - fee);
        var currency = capture.Amount?.CurrencyCode ?? Currency;

        return new PayPalCaptureResult(
            capture.Id ?? throw new PayPalClientException("PayPal capture did not include an id."),
            capture.Status ?? "COMPLETED",
            captured,
            fee,
            net,
            currency);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: new JsonObject(),
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        JsonNode body = amount is null
            ? new JsonObject()
            : new JsonObject { ["amount"] = Money(amount.Value) };

        var json = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        var refund = Deserialize<PayPalRefundResource>(json)
                     ?? throw new PayPalClientException("PayPal returned an empty refund.");
        return new PayPalRefundResult(
            refund.Id ?? throw new PayPalClientException("PayPal refund did not include an id."),
            refund.Status ?? "COMPLETED",
            ParseAmount(refund.Amount?.Value) ?? amount ?? 0m,
            refund.Amount?.CurrencyCode ?? Currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentDetails card,
        string? paypalCustomerId,
        CancellationToken cancellationToken = default)
    {
        var setupBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["card"] = BuildCardObject(card)
            }
        };

        if (!string.IsNullOrWhiteSpace(paypalCustomerId))
        {
            setupBody["customer"] = new JsonObject { ["id"] = paypalCustomerId };
        }

        var setupJson = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupBody,
            Guid.NewGuid().ToString("N"),
            preferRepresentation: false,
            cancellationToken);

        var setup = Deserialize<PayPalSetupTokenResponse>(setupJson)
                    ?? throw new PayPalClientException("PayPal returned an empty setup token.");

        if (string.Equals(setup.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a browser challenge to save this card. This API does not support an approval round-trip.");
        }

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalClientException(
                $"PayPal setup token status was '{setup.Status}', expected APPROVED.");
        }

        var tokenBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject
                {
                    ["id"] = setup.Id,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        var tokenJson = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenBody,
            Guid.NewGuid().ToString("N"),
            preferRepresentation: false,
            cancellationToken);

        var token = Deserialize<PayPalPaymentTokenResponse>(tokenJson)
                    ?? throw new PayPalClientException("PayPal returned an empty payment token.");

        var cardSource = token.PaymentSource?.Card;
        var lastDigits = cardSource?.LastDigits
                         ?? LastFour(card.Number)
                         ?? throw new PayPalClientException("PayPal payment token did not include card last digits.");

        return new PayPalVaultedCard(
            token.Id ?? throw new PayPalClientException("PayPal payment token did not include an id."),
            lastDigits,
            cardSource?.Brand,
            cardSource?.Expiry ?? card.Expiry,
            cardSource?.Name ?? card.Name,
            token.Customer?.Id ?? setup.Customer?.Id);
    }

    public async Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{paymentTokenId}",
            body: null,
            idempotencyKey: null,
            preferRepresentation: false,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from;
        var maxWindow = TimeSpan.FromDays(31) - TimeSpan.FromSeconds(1);

        while (windowStart <= to)
        {
            var windowEnd = windowStart + maxWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await AddWindowAsync(results, windowStart, windowEnd, cancellationToken);
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
            var query =
                $"start_date={Uri.EscapeDataString(FormatTimestamp(start))}" +
                $"&end_date={Uri.EscapeDataString(FormatTimestamp(end))}" +
                $"&fields=all&page_size=500&page={page}&balance_affecting_records_only=N";

            string json;
            try
            {
                json = await SendAsync(
                    HttpMethod.Get,
                    $"/v1/reporting/transactions?{query}",
                    body: null,
                    idempotencyKey: null,
                    preferRepresentation: false,
                    cancellationToken);
            }
            catch (PayPalClientException ex) when (ex.StatusCode == 404)
            {
                return;
            }

            var pageResult = Deserialize<PayPalTransactionSearchResponse>(json);
            if (pageResult?.TransactionDetails is not null)
            {
                foreach (var detail in pageResult.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction(
                        info.TransactionId ?? string.Empty,
                        info.PaypalReferenceId,
                        info.InvoiceId,
                        info.CustomField,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseTime(info.TransactionInitiationDate),
                        ParseAmount(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        ParseAmount(info.FeeAmount?.Value)));
                }
            }

            totalPages = pageResult?.TotalPages is > 0 ? pageResult.TotalPages.Value : 1;
            page++;
        } while (page <= totalPages);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        JsonObject paymentSource,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var createBody = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = $"order-{orderId}",
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = orderId.ToString(CultureInfo.InvariantCulture),
                    ["amount"] = Money(amount)
                }
            },
            ["payment_source"] = paymentSource
        };

        var createJson = await SendAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createBody,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        var created = Deserialize<PayPalOrderResource>(createJson)
                      ?? throw new PayPalClientException("PayPal returned an empty order.");
        EnsureNotPayerAction(created.Status, createJson);

        var authorization = ExtractAuthorization(created);
        if (authorization is not null)
        {
            return ToAuthorizationResult(created.Id, authorization);
        }

        var authorizeJson = await SendAsync(
            HttpMethod.Post,
            $"/v2/checkout/orders/{created.Id}/authorize",
            new JsonObject(),
            $"{idempotencyKey}-authorize",
            preferRepresentation: true,
            cancellationToken);

        var authorized = Deserialize<PayPalOrderResource>(authorizeJson)
                         ?? throw new PayPalClientException("PayPal returned an empty authorize response.");
        EnsureNotPayerAction(authorized.Status, authorizeJson);

        authorization = ExtractAuthorization(authorized)
                        ?? throw new PayPalClientException(
                            "PayPal authorized the order but did not return an authorization id.");
        return ToAuthorizationResult(authorized.Id ?? created.Id, authorization);
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string relativeUrl,
        JsonNode? body,
        string? idempotencyKey,
        bool preferRepresentation,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var token = await GetAccessTokenAsync(cancellationToken);
        var url = Combine(ResolveBaseUrl(), relativeUrl);

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Url}", method, RedactUrl(relativeUrl));

        using var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return content;
        }

        var error = TryParseError(content);
        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var message = error?.Message
                      ?? $"PayPal request failed with {(int)response.StatusCode} {response.StatusCode}.";
        if (!string.IsNullOrWhiteSpace(issue))
        {
            message = $"{message} Issue: {issue}.";
        }

        _logger.LogWarning(
            "PayPal {Method} {Url} failed with {Status} debug_id={DebugId} issue={Issue}",
            method,
            RedactUrl(relativeUrl),
            (int)response.StatusCode,
            error?.DebugId,
            issue);

        if (string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || content.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a browser challenge to complete this card payment. This API does not support an approval round-trip.");
        }

        throw new PayPalClientException(message, (int)response.StatusCode, issue, error?.DebugId, error?.Name);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            var tokenUrl = Combine(ResolveBaseUrl(), "/v1/oauth2/token");
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _http.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = TryParseError(content);
                throw new PayPalClientException(
                    error?.Message ?? "PayPal rejected the client credentials request.",
                    (int)response.StatusCode,
                    error?.Details?.FirstOrDefault()?.Issue,
                    error?.DebugId,
                    error?.Name);
            }

            var token = Deserialize<PayPalTokenResponse>(content)
                        ?? throw new PayPalClientException("PayPal returned an empty access token.");
            _accessToken = token.AccessToken
                           ?? throw new PayPalClientException("PayPal token response did not include access_token.");
            var lifetime = TimeSpan.FromSeconds(Math.Max(token.ExpiresIn - 60, 30));
            _tokenExpiresAt = DateTimeOffset.UtcNow.Add(lifetime);
            return _accessToken;
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
            throw new OrderPaymentException(
                "PayPal is not configured. Set PayPal:ClientId and PayPal:ClientSecret (from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET).",
                500);
        }

        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new OrderPaymentException(
                "PayPal is not configured. Set PayPal:Currency (from PAYPAL_CURRENCY).",
                500);
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Environment))
        {
            throw new OrderPaymentException(
                "PayPal is not configured. Set PayPal:Environment (from PAYPAL_ENVIRONMENT) or PayPal:BaseUrl.",
                500);
        }
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return _options.BaseUrl.TrimEnd('/');
        }

        var environment = _options.Environment.Trim();
        if (environment.Equals("live", StringComparison.OrdinalIgnoreCase)
            || environment.Equals("production", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        if (environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.sandbox.paypal.com";
        }

        throw new OrderPaymentException(
            $"Unsupported PayPal:Environment '{_options.Environment}'. Use sandbox, live, or set PayPal:BaseUrl.",
            500);
    }

    private JsonObject Money(decimal amount)
    {
        return new JsonObject
        {
            ["currency_code"] = Currency,
            ["value"] = FormatAmount(amount)
        };
    }

    private string FormatAmount(decimal amount)
    {
        var rounded = decimal.Round(amount, ZeroDecimalCurrencies.Contains(Currency) ? 0 : 2, MidpointRounding.AwayFromZero);
        return ZeroDecimalCurrencies.Contains(Currency)
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static JsonObject BuildCardObject(CardPaymentDetails card)
    {
        var cardObject = new JsonObject
        {
            ["number"] = new string(card.Number.Where(char.IsDigit).ToArray()),
            ["expiry"] = card.Expiry
        };

        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            cardObject["security_code"] = card.SecurityCode;
        }

        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            cardObject["name"] = card.Name;
        }

        if (card.BillingAddress is not null)
        {
            var address = new JsonObject();
            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1))
            {
                address["address_line_1"] = card.BillingAddress.AddressLine1;
            }

            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine2))
            {
                address["address_line_2"] = card.BillingAddress.AddressLine2;
            }

            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AdminArea2))
            {
                address["admin_area_2"] = card.BillingAddress.AdminArea2;
            }

            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AdminArea1))
            {
                address["admin_area_1"] = card.BillingAddress.AdminArea1;
            }

            if (!string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode))
            {
                address["postal_code"] = card.BillingAddress.PostalCode;
            }

            if (!string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            {
                address["country_code"] = card.BillingAddress.CountryCode;
            }

            if (address.Count > 0)
            {
                cardObject["billing_address"] = address;
            }
        }

        return cardObject;
    }

    private static PayPalAuthorizationResource? ExtractAuthorization(PayPalOrderResource order) =>
        order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationResource>())
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Id));

    private static PayPalAuthorizationResult ToAuthorizationResult(string? orderId, PayPalAuthorizationResource authorization) =>
        new(
            orderId ?? throw new PayPalClientException("PayPal order id was missing."),
            authorization.Id!,
            authorization.Status ?? "CREATED",
            ParseTime(authorization.CreateTime),
            ParseTime(authorization.ExpirationTime));

    private static PayPalAuthorizationDetails ToAuthorizationDetails(PayPalAuthorizationResource authorization) =>
        new(
            authorization.Id ?? throw new PayPalClientException("PayPal authorization id was missing."),
            authorization.Status ?? "CREATED",
            ParseTime(authorization.CreateTime),
            ParseTime(authorization.ExpirationTime));

    private static void EnsureNotPayerAction(string? status, string raw)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a browser challenge to complete this card payment. This API does not support an approval round-trip.");
        }
    }

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static PayPalErrorResponse? TryParseError(string json)
    {
        try
        {
            return Deserialize<PayPalErrorResponse>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Combine(string baseUrl, string relative)
    {
        if (relative.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return relative;
        }

        return $"{baseUrl.TrimEnd('/')}/{relative.TrimStart('/')}";
    }

    private static string RedactUrl(string url)
    {
        // Paths contain PayPal resource ids, never card data.
        return url;
    }

    private static string? LastFour(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private sealed class PayPalTokenResponse
    {
        public string? AccessToken { get; set; }
        public int ExpiresIn { get; set; }
    }

    private sealed class PayPalOrderResource
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
    }

    private sealed class PayPalPurchaseUnit
    {
        public PayPalPaymentsContainer? Payments { get; set; }
    }

    private sealed class PayPalPaymentsContainer
    {
        public List<PayPalAuthorizationResource>? Authorizations { get; set; }
        public List<PayPalCaptureResource>? Captures { get; set; }
    }

    private sealed class PayPalAuthorizationResource
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public string? CreateTime { get; set; }
        public string? ExpirationTime { get; set; }
        public PayPalMoney? Amount { get; set; }
    }

    private sealed class PayPalCaptureResource
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalMoney? Amount { get; set; }
        public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
    }

    private sealed class PayPalSellerReceivableBreakdown
    {
        public PayPalMoney? GrossAmount { get; set; }
        public PayPalMoney? PaypalFee { get; set; }
        public PayPalMoney? NetAmount { get; set; }
    }

    private sealed class PayPalRefundResource
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalMoney? Amount { get; set; }
    }

    private sealed class PayPalMoney
    {
        [JsonPropertyName("currency_code")]
        public string? CurrencyCode { get; set; }
        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }

    private sealed class PayPalSetupTokenResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalCustomer? Customer { get; set; }
        public PayPalPaymentSource? PaymentSource { get; set; }
    }

    private sealed class PayPalPaymentTokenResponse
    {
        public string? Id { get; set; }
        public PayPalCustomer? Customer { get; set; }
        public PayPalPaymentSource? PaymentSource { get; set; }
    }

    private sealed class PayPalCustomer
    {
        public string? Id { get; set; }
    }

    private sealed class PayPalPaymentSource
    {
        public PayPalCardSource? Card { get; set; }
    }

    private sealed class PayPalCardSource
    {
        public string? LastDigits { get; set; }
        public string? Brand { get; set; }
        public string? Expiry { get; set; }
        public string? Name { get; set; }
    }

    private sealed class PayPalErrorResponse
    {
        public string? Name { get; set; }
        public string? Message { get; set; }
        public string? DebugId { get; set; }
        public List<PayPalErrorDetail>? Details { get; set; }
    }

    private sealed class PayPalErrorDetail
    {
        public string? Issue { get; set; }
        public string? Description { get; set; }
        public string? Field { get; set; }
    }

    private sealed class PayPalTransactionSearchResponse
    {
        public int? TotalPages { get; set; }
        public int? TotalItems { get; set; }
        public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
    }

    private sealed class PayPalTransactionDetail
    {
        public PayPalTransactionInfo? TransactionInfo { get; set; }
    }

    private sealed class PayPalTransactionInfo
    {
        public string? TransactionId { get; set; }
        public string? PaypalReferenceId { get; set; }
        public string? InvoiceId { get; set; }
        public string? CustomField { get; set; }
        public string? TransactionEventCode { get; set; }
        public string? TransactionStatus { get; set; }
        public string? TransactionInitiationDate { get; set; }
        public PayPalMoney? TransactionAmount { get; set; }
        public PayPalMoney? FeeAmount { get; set; }
    }
}
