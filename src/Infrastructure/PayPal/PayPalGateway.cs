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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalGateway : IPayPalGateway
{
    private const string TokenCacheKey = "paypal:access_token";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(
        HttpClient httpClient,
        IOptions<PayPalOptions> options,
        IMemoryCache cache,
        ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<PayPalOrderResult> CreateOrderForAuthorizationAsync(
        PayPalCreateOrderRequest request,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = request.InvoiceId,
                    custom_id = request.CustomId,
                    description = request.Description,
                    amount = new
                    {
                        currency_code = request.Currency,
                        value = request.Amount
                    }
                }
            }
        };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            body,
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);

        return ParseOrderResult(doc.RootElement);
    }

    public async Task<PayPalOrderResult> AuthorizeOrderAsync(
        string payPalOrderId,
        object? paymentSource,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        object body = paymentSource is null
            ? new { }
            : new { payment_source = paymentSource };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            body,
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);

        return ParseOrderResult(doc.RootElement);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body: null,
            payPalRequestId: null,
            preferRepresentation: true,
            cancellationToken);

        return ParseAuthorization(doc.RootElement)
            ?? throw new PaymentException("PayPal authorization details were missing an id.", 502, "PAYPAL_MALFORMED_RESPONSE");
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        string currencyCode,
        string amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new
            {
                currency_code = currencyCode,
                value = amount
            }
        };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body,
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);

        return ParseAuthorization(doc.RootElement)
            ?? throw new PaymentException("PayPal reauthorization response was missing an id.", 502, "PAYPAL_MALFORMED_RESPONSE");
    }

    public async Task<PayPalCaptureDetails> CaptureAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new { final_capture = true };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body,
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);

        var capture = ParseCapture(doc.RootElement);
        if (capture is null)
        {
            throw new PaymentException("PayPal capture response was missing an id.", 502, "PAYPAL_MALFORMED_RESPONSE");
        }

        if (string.IsNullOrEmpty(capture.PayPalFee) && !string.IsNullOrEmpty(capture.Id))
        {
            return await GetCaptureAsync(capture.Id, cancellationToken);
        }

        return capture;
    }

    public async Task<PayPalCaptureDetails> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(
            HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}",
            body: null,
            payPalRequestId: null,
            preferRepresentation: true,
            cancellationToken);

        return ParseCapture(doc.RootElement)
            ?? throw new PaymentException("PayPal capture details were missing an id.", 502, "PAYPAL_MALFORMED_RESPONSE");
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            body: new { },
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);
    }

    public async Task<PayPalRefundDetails> RefundCaptureAsync(
        string captureId,
        string currencyCode,
        string? amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        object body = string.IsNullOrEmpty(amount)
            ? new { }
            : new { amount = new { currency_code = currencyCode, value = amount } };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body,
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);

        var root = doc.RootElement;
        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(id))
        {
            throw new PaymentException("PayPal refund response was missing an id.", 502, "PAYPAL_MALFORMED_RESPONSE");
        }

        var money = root.TryGetProperty("amount", out var amountEl) ? amountEl : default;
        return new PayPalRefundDetails
        {
            Id = id,
            Status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
            Amount = money.ValueKind == JsonValueKind.Object && money.TryGetProperty("value", out var valueEl) ? valueEl.GetString() : null,
            Currency = money.ValueKind == JsonValueKind.Object && money.TryGetProperty("currency_code", out var ccEl) ? ccEl.GetString() : null
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        PayPalVaultCardRequest request,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            customer = new { merchant_customer_id = request.MerchantCustomerId },
            payment_source = new { card = request.Card }
        };

        using var doc = await SendJsonAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);

        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        EnsureNoPayerActionRequired(root, status);

        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(id))
        {
            throw new PaymentException("PayPal did not return a payment token id for the saved card.", 502, "PAYPAL_MALFORMED_RESPONSE");
        }

        string? brand = null, lastDigits = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var source) &&
            source.TryGetProperty("card", out var card))
        {
            brand = card.TryGetProperty("brand", out var brandEl) ? brandEl.GetString() : null;
            lastDigits = card.TryGetProperty("last_digits", out var lastEl) ? lastEl.GetString() : null;
            expiry = card.TryGetProperty("expiry", out var expEl) ? expEl.GetString() : null;
            name = card.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        }

        string? customerId = null;
        if (root.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var custId))
        {
            customerId = custId.GetString();
        }

        return new PayPalVaultedCard
        {
            PaymentTokenId = id,
            CustomerId = customerId,
            Brand = brand,
            LastDigits = lastDigits,
            Expiry = expiry,
            CardholderName = name,
            Status = status
        };
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendJsonAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}",
            body: null,
            payPalRequestId: null,
            preferRepresentation: false,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var window in SplitDateRange(from, to, TimeSpan.FromDays(31)))
        {
            var page = 1;
            int? totalPages = null;
            do
            {
                var start = FormatReportingTimestamp(window.From);
                var end = FormatReportingTimestamp(window.To);
                var path =
                    $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=100&page={page}&balance_affecting_records_only=N";

                using var doc = await SendJsonAsync(
                    HttpMethod.Get,
                    path,
                    body: null,
                    payPalRequestId: null,
                    preferRepresentation: false,
                    cancellationToken);

                var root = doc.RootElement;
                if (root.TryGetProperty("total_pages", out var pagesEl) && pagesEl.TryGetInt32(out var pages))
                {
                    totalPages = pages;
                }

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in details.EnumerateArray())
                    {
                        var info = item.TryGetProperty("transaction_info", out var infoEl) ? infoEl : item;
                        results.Add(ParseReportedTransaction(info));
                    }
                }

                page++;
                if (totalPages is null)
                {
                    break;
                }
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        string? payPalRequestId,
        bool preferRepresentation,
        CancellationToken cancellationToken)
    {
        var payload = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        Exception? lastError = null;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0)
            {
                var delayMs = (int)(Math.Pow(2, attempt) * 150) + Random.Shared.Next(50, 200);
                await Task.Delay(delayMs, cancellationToken);
            }

            try
            {
                var token = await GetAccessTokenAsync(forceRefresh: attempt > 0 && lastError is PaymentException pe && pe.StatusCode == 401, cancellationToken);

                using var request = new HttpRequestMessage(method, CombineUrl(relativePath));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrEmpty(payPalRequestId))
                {
                    request.Headers.TryAddWithoutValidation("PayPal-Request-Id", payPalRequestId);
                }
                if (preferRepresentation)
                {
                    request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
                }

                if (payload is not null)
                {
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return JsonDocument.Parse("{}");
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    lastError = new PaymentException("PayPal rejected the access token.", 401, "PAYPAL_UNAUTHORIZED");
                    continue;
                }

                if ((int)response.StatusCode == 429 || ((int)response.StatusCode >= 500 && CanRetry(method, payPalRequestId)))
                {
                    _logger.LogWarning("PayPal returned {StatusCode} for {Method} {Path}. debug_id={DebugId}",
                        (int)response.StatusCode, method, SanitizePath(relativePath), ExtractDebugId(content));
                    lastError = MapPayPalError(response.StatusCode, content);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw MapPayPalError(response.StatusCode, content);
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    return JsonDocument.Parse("{}");
                }

                return JsonDocument.Parse(content);
            }
            catch (PaymentException) when (attempt < 3 && CanRetry(method, payPalRequestId))
            {
                throw;
            }
        }

        throw lastError ?? new PaymentException("PayPal request failed after retries.", 502, "PAYPAL_UNAVAILABLE");
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PaymentException("PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.", 500, "PAYPAL_NOT_CONFIGURED");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl("/v1/oauth2/token"));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal token request failed with {StatusCode}. debug_id={DebugId}",
                (int)response.StatusCode, ExtractDebugId(content));
            throw new PaymentException("Unable to authenticate with PayPal.", 502, "PAYPAL_AUTH_FAILED");
        }

        using var doc = JsonDocument.Parse(content);
        var accessToken = doc.RootElement.TryGetProperty("access_token", out var tokenEl) ? tokenEl.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new PaymentException("PayPal token response did not include an access_token.", 502, "PAYPAL_AUTH_FAILED");
        }

        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var expEl) && expEl.TryGetInt32(out var seconds)
            ? seconds
            : 300;
        var lifetime = TimeSpan.FromSeconds(Math.Max(30, expiresIn - 60));
        _cache.Set(TokenCacheKey, accessToken, lifetime);
        return accessToken;
    }

    private string CombineUrl(string relativePath)
    {
        var root = _options.ResolveBaseUrl();
        if (!relativePath.StartsWith('/'))
        {
            relativePath = "/" + relativePath;
        }
        return root + relativePath;
    }

    private PayPalOrderResult ParseOrderResult(JsonElement root)
    {
        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(status))
        {
            throw new PaymentException("PayPal order response was missing id or status.", 502, "PAYPAL_MALFORMED_RESPONSE");
        }

        EnsureNoPayerActionRequired(root, status);

        PayPalAuthorizationDetails? authorization = null;
        if (root.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
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
                    authorization = ParseAuthorization(auth);
                    if (authorization is not null)
                    {
                        break;
                    }
                }
            }
        }

        return new PayPalOrderResult
        {
            Id = id,
            Status = status,
            PayerActionUrl = FindLink(root, "payer-action"),
            Authorization = authorization
        };
    }

    private static void EnsureNoPayerActionRequired(JsonElement root, string? status)
    {
        var payerAction = FindLink(root, "payer-action");
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(payerAction))
        {
            throw new PayerActionRequiredException(
                "PayPal required the shopper to complete a browser challenge (for example 3-D Secure). This integration does not support an approval round-trip.");
        }
    }

    private static string? FindLink(JsonElement root, string rel)
    {
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var link in links.EnumerateArray())
        {
            var linkRel = link.TryGetProperty("rel", out var relEl) ? relEl.GetString() : null;
            if (string.Equals(linkRel, rel, StringComparison.OrdinalIgnoreCase))
            {
                return link.TryGetProperty("href", out var hrefEl) ? hrefEl.GetString() : null;
            }
        }

        return null;
    }

    private static PayPalAuthorizationDetails? ParseAuthorization(JsonElement element)
    {
        var id = element.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        DateTimeOffset? expiration = null;
        if (element.TryGetProperty("expiration_time", out var expEl) &&
            DateTimeOffset.TryParse(expEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exp))
        {
            expiration = exp;
        }

        DateTimeOffset? created = null;
        if (element.TryGetProperty("create_time", out var createdEl) &&
            DateTimeOffset.TryParse(createdEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            created = createdAt;
        }

        string? amount = null, currency = null;
        if (element.TryGetProperty("amount", out var amountEl) && amountEl.ValueKind == JsonValueKind.Object)
        {
            amount = amountEl.TryGetProperty("value", out var valueEl) ? valueEl.GetString() : null;
            currency = amountEl.TryGetProperty("currency_code", out var ccEl) ? ccEl.GetString() : null;
        }

        return new PayPalAuthorizationDetails
        {
            Id = id,
            Status = element.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
            Amount = amount,
            Currency = currency,
            ExpirationTime = expiration,
            CreateTime = created
        };
    }

    private static PayPalCaptureDetails? ParseCapture(JsonElement element)
    {
        var id = element.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        string? amount = null, currency = null, fee = null, net = null;
        if (element.TryGetProperty("amount", out var amountEl) && amountEl.ValueKind == JsonValueKind.Object)
        {
            amount = amountEl.TryGetProperty("value", out var valueEl) ? valueEl.GetString() : null;
            currency = amountEl.TryGetProperty("currency_code", out var ccEl) ? ccEl.GetString() : null;
        }

        if (element.TryGetProperty("seller_receivable_breakdown", out var breakdown) && breakdown.ValueKind == JsonValueKind.Object)
        {
            if (breakdown.TryGetProperty("paypal_fee", out var feeEl) && feeEl.TryGetProperty("value", out var feeVal))
            {
                fee = feeVal.GetString();
            }
            if (breakdown.TryGetProperty("net_amount", out var netEl) && netEl.TryGetProperty("value", out var netVal))
            {
                net = netVal.GetString();
            }
            if (string.IsNullOrEmpty(amount) && breakdown.TryGetProperty("gross_amount", out var gross) && gross.TryGetProperty("value", out var grossVal))
            {
                amount = grossVal.GetString();
            }
        }

        return new PayPalCaptureDetails
        {
            Id = id,
            Status = element.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
            Amount = amount,
            Currency = currency,
            PayPalFee = fee,
            NetAmount = net
        };
    }

    private static PayPalReportedTransaction ParseReportedTransaction(JsonElement info)
    {
        DateTimeOffset? initiated = null;
        if (info.TryGetProperty("transaction_initiation_date", out var dateEl) &&
            DateTimeOffset.TryParse(dateEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            initiated = parsed;
        }

        string? amount = null, currency = null;
        if (info.TryGetProperty("transaction_amount", out var amountEl) && amountEl.ValueKind == JsonValueKind.Object)
        {
            amount = amountEl.TryGetProperty("value", out var valueEl) ? valueEl.GetString() : null;
            currency = amountEl.TryGetProperty("currency_code", out var ccEl) ? ccEl.GetString() : null;
        }

        string? fee = null;
        if (info.TryGetProperty("fee_amount", out var feeEl) && feeEl.ValueKind == JsonValueKind.Object &&
            feeEl.TryGetProperty("value", out var feeVal))
        {
            fee = feeVal.GetString();
        }

        return new PayPalReportedTransaction
        {
            TransactionId = info.TryGetProperty("transaction_id", out var txn) ? txn.GetString() : null,
            PayPalReferenceId = info.TryGetProperty("paypal_reference_id", out var pref) ? pref.GetString() : null,
            InvoiceId = info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null,
            CustomField = info.TryGetProperty("custom_field", out var custom) ? custom.GetString() : null,
            EventCode = info.TryGetProperty("transaction_event_code", out var code) ? code.GetString() : null,
            Status = info.TryGetProperty("transaction_status", out var status) ? status.GetString() : null,
            Amount = amount,
            Currency = currency,
            FeeAmount = fee,
            InitiationDate = initiated
        };
    }

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitDateRange(DateTimeOffset from, DateTimeOffset to, TimeSpan maxWindow)
    {
        var cursor = from;
        while (cursor < to)
        {
            var windowEnd = cursor + maxWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }
            yield return (cursor, windowEnd);
            cursor = windowEnd;
        }
    }

    private static string FormatReportingTimestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static bool CanRetry(HttpMethod method, string? payPalRequestId)
    {
        return method == HttpMethod.Get || !string.IsNullOrEmpty(payPalRequestId);
    }

    private static string SanitizePath(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? path[..q] : path;
    }

    private static string? ExtractDebugId(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("debug_id", out var debug))
            {
                return debug.GetString();
            }
        }
        catch (JsonException)
        {
            // PayPal sometimes returns non-JSON on hard failures.
        }

        return null;
    }

    private PaymentException MapPayPalError(HttpStatusCode statusCode, string content)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        string? issue = null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;
            debugId = root.TryGetProperty("debug_id", out var debugEl) ? debugEl.GetString() : null;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var detail in details.EnumerateArray())
                {
                    issue = detail.TryGetProperty("issue", out var issueEl) ? issueEl.GetString() : issue;
                    var field = detail.TryGetProperty("field", out var fieldEl) ? fieldEl.GetString() : null;
                    var description = detail.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
                    var piece = string.Join(" ", new[] { field, issue, description }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (!string.IsNullOrWhiteSpace(piece))
                    {
                        parts.Add(piece);
                    }
                }
                if (parts.Count > 0)
                {
                    message = string.IsNullOrEmpty(message) ? string.Join("; ", parts) : $"{message}: {string.Join("; ", parts)}";
                }
            }
        }
        catch (JsonException)
        {
            message = "PayPal returned an unreadable error response.";
        }

        _logger.LogWarning("PayPal API error {Status} name={Name} issue={Issue} debug_id={DebugId}",
            (int)statusCode, name, issue, debugId);

        if (string.Equals(statusCode.ToString(), "UnprocessableEntity", StringComparison.OrdinalIgnoreCase) ||
            (int)statusCode == 422)
        {
            if (string.Equals(issue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(issue, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "UNPROCESSABLE_ENTITY", StringComparison.OrdinalIgnoreCase) &&
                (message?.Contains("expired", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return new PaymentException(
                    "The PayPal authorization can no longer be used. Ask the shopper to pay again so a new hold can be placed.",
                    409,
                    issue ?? name);
            }
        }

        var mappedStatus = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 409,
            (HttpStatusCode)429 => 503,
            _ when (int)statusCode >= 500 => 502,
            _ => 502
        };

        var safeMessage = string.IsNullOrWhiteSpace(message)
            ? "PayPal rejected the payment request."
            : message;

        return new PaymentException(safeMessage, mappedStatus, issue ?? name);
    }
}
