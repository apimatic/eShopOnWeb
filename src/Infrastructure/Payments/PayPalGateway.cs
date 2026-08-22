using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private const string TokenCacheKey = "paypal-access-token";
    private static readonly TimeSpan TokenSkew = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly PayPalOptions _options;

    public PayPalGateway(
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<PayPalGateway> logger,
        IOptions<PayPalOptions> options)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(
        string invoiceId,
        decimal amount,
        string currency,
        CardPaymentSource? card,
        string? vaultId,
        string? paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var createBody = new CheckoutOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            {
                new PurchaseUnitRequest
                {
                    InvoiceId = invoiceId,
                    CustomId = invoiceId,
                    Amount = new MoneyRequest
                    {
                        CurrencyCode = currency,
                        Value = PayPalMoney.Format(amount, currency)
                    }
                }
            }
        };

        using var createResponse = await SendAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createBody,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        using var createDocument = await ReadJsonAsync(createResponse, cancellationToken);
        var paypalOrderId = GetString(createDocument.RootElement, "id")
            ?? throw new PaymentException("PayPal did not return an order id.", 502);

        var authorizeBody = new OrderAuthorizeRequest
        {
            PaymentSource = new PaymentSourceRequest
            {
                Card = BuildCardRequest(card, vaultId, paypalCustomerId)
            }
        };

        using var authorizeResponse = await SendAsync(
            HttpMethod.Post,
            $"/v2/checkout/orders/{paypalOrderId}/authorize",
            authorizeBody,
            $"{requestId}-auth",
            preferRepresentation: true,
            cancellationToken);

        using var document = await ReadJsonAsync(authorizeResponse, cancellationToken);
        var root = document.RootElement;

        var orderStatus = GetString(root, "status");
        EnsureNoPayerAction(orderStatus, root);

        var authorization = FindAuthorization(root)
            ?? throw new PaymentException("PayPal authorized the order but did not return an authorization id.", 502);

        var authStatus = GetString(authorization, "status") ?? "CREATED";
        var authId = GetString(authorization, "id")
            ?? throw new PaymentException("PayPal authorized the order but did not return an authorization id.", 502);

        return new PayPalAuthorizationResult(
            GetString(root, "id") ?? paypalOrderId,
            orderStatus ?? string.Empty,
            authId,
            authStatus,
            GetDate(authorization, "expiration_time"),
            GetDate(authorization, "create_time"),
            GetMoneyValue(authorization, "amount") ?? amount,
            GetMoneyCurrency(authorization, "amount") ?? currency);
    }

    public async Task<PayPalAuthorizationSnapshot> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            preferRepresentation: false,
            cancellationToken);

        using var document = await ReadJsonAsync(response, cancellationToken);
        return ReadAuthorizationSnapshot(document.RootElement);
    }

    public async Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new MoneyRequest
            {
                CurrencyCode = currency,
                Value = PayPalMoney.Format(amount, currency)
            }
        };

        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        using var document = await ReadJsonAsync(response, cancellationToken);
        return ReadAuthorizationSnapshot(document.RootElement);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequest
        {
            Amount = new MoneyRequest
            {
                CurrencyCode = currency,
                Value = PayPalMoney.Format(amount, currency)
            },
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        using var document = await ReadJsonAsync(response, cancellationToken);
        var root = document.RootElement;
        var captureId = GetString(root, "id")
            ?? throw new PaymentException("PayPal captured the payment but did not return a capture id.", 502);

        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = GetMoneyValue(breakdown, "paypal_fee");
            net = GetMoneyValue(breakdown, "net_amount");
        }

        return new PayPalCaptureResult(
            captureId,
            GetString(root, "status") ?? "COMPLETED",
            GetMoneyValue(root, "amount") ?? amount,
            fee,
            net,
            GetMoneyCurrency(root, "amount") ?? currency);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: null,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return;

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object? body = amount is null
            ? new { }
            : new RefundRequest
            {
                Amount = new MoneyRequest
                {
                    CurrencyCode = currency,
                    Value = PayPalMoney.Format(amount.Value, currency)
                }
            };

        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            preferRepresentation: true,
            cancellationToken);

        using var document = await ReadJsonAsync(response, cancellationToken);
        var root = document.RootElement;
        var refundId = GetString(root, "id")
            ?? throw new PaymentException("PayPal refunded the payment but did not return a refund id.", 502);

        return new PayPalRefundResult(
            refundId,
            GetString(root, "status") ?? "COMPLETED",
            GetMoneyValue(root, "amount") ?? amount ?? 0m,
            GetMoneyCurrency(root, "amount") ?? currency);
    }

    public async Task<PayPalVaultResult> VaultCardAsync(
        string paypalCustomerId,
        CardPaymentSource card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var setupBody = new VaultCustomerRequest
        {
            Customer = new VaultCustomer { Id = paypalCustomerId },
            PaymentSource = new VaultPaymentSource
            {
                Card = BuildVaultCard(card)
            }
        };

        using var setupResponse = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupBody,
            requestId,
            preferRepresentation: false,
            cancellationToken);

        using var setupDocument = await ReadJsonAsync(setupResponse, cancellationToken);
        var setupRoot = setupDocument.RootElement;
        EnsureNoPayerAction(GetString(setupRoot, "status"), setupRoot);

        var setupTokenId = GetString(setupRoot, "id")
            ?? throw new PaymentException("PayPal did not return a setup token id.", 502);

        var paymentTokenBody = new VaultCustomerRequest
        {
            Customer = new VaultCustomer { Id = paypalCustomerId },
            PaymentSource = new VaultPaymentSource
            {
                Token = new VaultTokenRequest
                {
                    Id = setupTokenId,
                    Type = "SETUP_TOKEN"
                }
            }
        };

        using var tokenResponse = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            paymentTokenBody,
            $"{requestId}-token",
            preferRepresentation: false,
            cancellationToken);

        using var tokenDocument = await ReadJsonAsync(tokenResponse, cancellationToken);
        var tokenRoot = tokenDocument.RootElement;
        EnsureNoPayerAction(GetString(tokenRoot, "status"), tokenRoot);

        var vaultId = GetString(tokenRoot, "id")
            ?? throw new PaymentException("PayPal did not return a payment token id.", 502);

        string? lastDigits = null;
        string? brand = null;
        string? expiry = null;
        string? name = null;
        if (tokenRoot.TryGetProperty("payment_source", out var source) && source.TryGetProperty("card", out var cardEl))
        {
            lastDigits = GetString(cardEl, "last_digits");
            brand = GetString(cardEl, "brand");
            expiry = GetString(cardEl, "expiry");
            name = GetString(cardEl, "name");
        }

        string? customerId = paypalCustomerId;
        if (tokenRoot.TryGetProperty("customer", out var customer))
            customerId = GetString(customer, "id") ?? paypalCustomerId;

        return new PayPalVaultResult(vaultId, lastDigits, brand, expiry, name, customerId);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{vaultId}",
            body: null,
            requestId: null,
            preferRepresentation: false,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return;

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from.ToUniversalTime();
        var end = to.ToUniversalTime();
        var maxWindow = TimeSpan.FromDays(31);

        while (windowStart <= end)
        {
            var windowEnd = windowStart + maxWindow - TimeSpan.FromSeconds(1);
            if (windowEnd > end)
                windowEnd = end;

            await AddTransactionsForWindowAsync(results, windowStart, windowEnd, cancellationToken);
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task AddTransactionsForWindowAsync(
        List<PayPalReportedTransaction> results,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var page = 1;
        var totalPages = 1;
        do
        {
            var query =
                $"start_date={Uri.EscapeDataString(FormatPayPalDate(from))}" +
                $"&end_date={Uri.EscapeDataString(FormatPayPalDate(to))}" +
                $"&page_size=500&page={page}&fields=all&balance_affecting_records_only=N";

            JsonDocument document;
            try
            {
                using var response = await SendAsync(
                    HttpMethod.Get,
                    $"/v1/reporting/transactions?{query}",
                    body: null,
                    requestId: null,
                    preferRepresentation: false,
                    cancellationToken);

                document = await ReadJsonAsync(response, cancellationToken);
            }
            catch (PayPalApiException ex) when (
                ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase)
                || ex.HasIssueContaining("DATA_NOT_AVAILABLE"))
            {
                _logger.LogInformation("PayPal reporting has no data for {From} to {To}: {Message}", from, to, ex.Message);
                return;
            }

            using (document)
            {
                var root = document.RootElement;

                if (root.TryGetProperty("total_pages", out var pagesEl) && pagesEl.TryGetInt32(out var pages))
                    totalPages = Math.Max(pages, 1);

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info))
                            continue;

                        var transactionId = GetString(info, "transaction_id");
                        if (string.IsNullOrEmpty(transactionId))
                            continue;

                        results.Add(new PayPalReportedTransaction(
                            transactionId,
                            GetString(info, "paypal_reference_id"),
                            GetString(info, "paypal_reference_id_type"),
                            GetString(info, "transaction_event_code"),
                            GetString(info, "transaction_status"),
                            GetString(info, "invoice_id"),
                            GetString(info, "custom_field"),
                            GetMoneyValue(info, "transaction_amount"),
                            GetMoneyCurrency(info, "transaction_amount"),
                            GetDate(info, "transaction_initiation_date"),
                            GetMoneyValue(info, "fee_amount")));
                    }
                }
            }

            page++;
        } while (page <= totalPages);
    }

    private static CardRequest BuildCardRequest(CardPaymentSource? card, string? vaultId, string? paypalCustomerId)
    {
        if (!string.IsNullOrEmpty(vaultId))
        {
            return new CardRequest
            {
                VaultId = vaultId,
                StoredCredential = new StoredCredentialRequest
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "UNSCHEDULED",
                    Usage = "SUBSEQUENT"
                },
                Attributes = string.IsNullOrEmpty(paypalCustomerId)
                    ? null
                    : new CardAttributesRequest
                    {
                        Customer = new CardCustomerRequest { Id = paypalCustomerId }
                    }
            };
        }

        if (card == null)
            throw new PaymentException("Card details are required when not paying with a saved card.");

        return new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToBillingAddress(card.BillingAddress)
        };
    }

    private static VaultCardRequest BuildVaultCard(CardPaymentSource card) =>
        new()
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToBillingAddress(card.BillingAddress)
        };

    private static BillingAddressRequest? ToBillingAddress(CardBillingAddress? address)
    {
        if (address == null)
            return null;

        return new BillingAddressRequest
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static PayPalAuthorizationSnapshot ReadAuthorizationSnapshot(JsonElement root) =>
        new(
            GetString(root, "id") ?? string.Empty,
            GetString(root, "status") ?? string.Empty,
            GetDate(root, "expiration_time"),
            GetDate(root, "create_time"),
            GetMoneyValue(root, "amount"),
            GetMoneyCurrency(root, "amount"));

    private static JsonElement? FindAuthorization(JsonElement order)
    {
        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments))
                continue;
            if (!payments.TryGetProperty("authorizations", out var auths) || auths.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var auth in auths.EnumerateArray())
                return auth;
        }

        return null;
    }

    private static void EnsureNoPayerAction(string? status, JsonElement root)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalPayerActionRequiredException(
                "PayPal required a shopper to approve this card payment in a browser (for example 3-D Secure). This API does not support that round-trip.");
        }

        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = GetString(link, "rel");
                if (rel != null && (rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase)
                    || rel.Equals("approve", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new PayPalPayerActionRequiredException(
                        "PayPal required a shopper to approve this card payment in a browser. This API does not support that round-trip.");
                }
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string pathAndQuery,
        object? body,
        string? requestId,
        bool preferRepresentation,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, Combine(GetBaseUrl(), pathAndQuery));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        if (preferRepresentation)
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, RedactPath(pathAndQuery));

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode == 401)
        {
            _cache.Remove(TokenCacheKey);
            token = await GetAccessTokenAsync(cancellationToken);
            response.Dispose();
            request = new HttpRequestMessage(method, Combine(GetBaseUrl(), pathAndQuery));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(requestId))
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            if (preferRepresentation)
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            response = await _httpClient.SendAsync(request, cancellationToken);
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return response;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("PayPal error {Status}: {Payload}", (int)response.StatusCode, RedactSecrets(payload));
        throw ParseApiException((int)response.StatusCode, payload);
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
            return JsonDocument.Parse("{}");

        return JsonDocument.Parse(payload);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
            return cached;

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new PaymentException("PayPal client credentials are not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(GetBaseUrl(), "/v1/oauth2/token"));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal token request failed with {Status}.", (int)response.StatusCode);
            throw new PaymentException("Could not authenticate with PayPal. Check PayPal:ClientId and PayPal:ClientSecret.", 502);
        }

        using var document = JsonDocument.Parse(payload);
        var token = GetString(document.RootElement, "access_token")
            ?? throw new PaymentException("PayPal token response did not include access_token.", 502);
        var expiresIn = 300;
        if (document.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds))
            expiresIn = seconds;

        var lifetime = TimeSpan.FromSeconds(Math.Max(30, expiresIn)) - TokenSkew;
        _cache.Set(TokenCacheKey, token, lifetime);
        return token;
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
            return _options.BaseUrl.TrimEnd('/');

        if (string.Equals(_options.Environment, "live", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_options.Environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }

    private static string Combine(string baseUrl, string pathAndQuery)
    {
        if (!pathAndQuery.StartsWith('/'))
            pathAndQuery = "/" + pathAndQuery;
        return baseUrl + pathAndQuery;
    }

    private static string FormatPayPalDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static string RedactPath(string pathAndQuery)
    {
        var q = pathAndQuery.IndexOf('?');
        return q >= 0 ? pathAndQuery[..q] : pathAndQuery;
    }

    private static string RedactSecrets(string payload)
    {
        if (string.IsNullOrEmpty(payload))
            return payload;

        return System.Text.RegularExpressions.Regex.Replace(
            payload,
            "\"(number|security_code)\"\\s*:\\s*\"[^\"]*\"",
            "\"$1\":\"[redacted]\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static PayPalApiException ParseApiException(int statusCode, string payload)
    {
        string? name = null;
        string message = "PayPal request failed.";
        string? debugId = null;
        var issues = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            var root = document.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message") ?? message;
            debugId = GetString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var issue = GetString(detail, "issue");
                    var description = GetString(detail, "description");
                    if (!string.IsNullOrEmpty(issue))
                        issues.Add(issue);
                    if (!string.IsNullOrEmpty(description))
                        issues.Add(description);
                }
            }
        }
        catch (JsonException)
        {
            // PayPal sometimes returns non-JSON on gateway failures.
        }

        if (issues.Count == 0 && name != null)
            issues.Add(name);

        if (issues.Count > 0)
            message = $"{message} ({string.Join("; ", issues.Distinct())})";

        return new PayPalApiException(statusCode, name, message, debugId, issues);
    }

    private static string SummarizeError(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            var name = GetString(document.RootElement, "name");
            var debug = GetString(document.RootElement, "debug_id");
            return $"{name} debug_id={debug}";
        }
        catch (JsonException)
        {
            return "non-json body";
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        if (!element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var raw = GetString(element, name);
        if (string.IsNullOrEmpty(raw))
            return null;
        if (DateTimeOffset.TryParse(raw, out var parsed))
            return parsed;
        return null;
    }

    private static decimal? GetMoneyValue(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var money) || money.ValueKind != JsonValueKind.Object)
            return null;
        var raw = GetString(money, "value");
        if (string.IsNullOrEmpty(raw))
            return null;
        return PayPalMoney.Parse(raw);
    }

    private static string? GetMoneyCurrency(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var money) || money.ValueKind != JsonValueKind.Object)
            return null;
        return GetString(money, "currency_code");
    }

    private sealed class CheckoutOrderRequest
    {
        public string Intent { get; set; } = "AUTHORIZE";
        public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
        public PaymentSourceRequest? PaymentSource { get; set; }
    }

    private sealed class PurchaseUnitRequest
    {
        public MoneyRequest? Amount { get; set; }
        public string? InvoiceId { get; set; }
        public string? CustomId { get; set; }
    }

    private sealed class MoneyRequest
    {
        public string CurrencyCode { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private sealed class PaymentSourceRequest
    {
        public CardRequest? Card { get; set; }
    }

    private sealed class CardRequest
    {
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? Expiry { get; set; }
        public string? SecurityCode { get; set; }
        public BillingAddressRequest? BillingAddress { get; set; }
        public string? VaultId { get; set; }
        public CardAttributesRequest? Attributes { get; set; }
        public StoredCredentialRequest? StoredCredential { get; set; }
    }

    private sealed class CardAttributesRequest
    {
        public CardCustomerRequest? Customer { get; set; }
        public CardVerificationRequest? Verification { get; set; }
    }

    private sealed class CardCustomerRequest
    {
        public string? Id { get; set; }
    }

    private sealed class CardVerificationRequest
    {
        public string? Method { get; set; }
    }

    private sealed class StoredCredentialRequest
    {
        public string? PaymentInitiator { get; set; }
        public string? PaymentType { get; set; }
        public string? Usage { get; set; }
    }

    private sealed class BillingAddressRequest
    {
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? AdminArea2 { get; set; }
        public string? AdminArea1 { get; set; }
        public string? PostalCode { get; set; }
        public string? CountryCode { get; set; }
    }

    private sealed class OrderAuthorizeRequest
    {
        public PaymentSourceRequest? PaymentSource { get; set; }
    }

    private sealed class CaptureRequest
    {
        public MoneyRequest? Amount { get; set; }
        public string? InvoiceId { get; set; }
        public bool FinalCapture { get; set; }
    }

    private sealed class ReauthorizeRequest
    {
        public MoneyRequest? Amount { get; set; }
    }

    private sealed class RefundRequest
    {
        public MoneyRequest? Amount { get; set; }
    }

    private sealed class VaultCustomerRequest
    {
        public VaultCustomer? Customer { get; set; }
        public VaultPaymentSource? PaymentSource { get; set; }
    }

    private sealed class VaultCustomer
    {
        public string? Id { get; set; }
    }

    private sealed class VaultPaymentSource
    {
        public VaultCardRequest? Card { get; set; }
        public VaultTokenRequest? Token { get; set; }
    }

    private sealed class VaultCardRequest
    {
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? Expiry { get; set; }
        public string? SecurityCode { get; set; }
        public BillingAddressRequest? BillingAddress { get; set; }
    }

    private sealed class VaultTokenRequest
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = "SETUP_TOKEN";
    }
}
