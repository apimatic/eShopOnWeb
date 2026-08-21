using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
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

public class PayPalPaymentsGateway : IPayPalPaymentsGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalPaymentsGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalPaymentsGateway(
        HttpClient httpClient,
        IOptions<PayPalOptions> options,
        ILogger<PayPalPaymentsGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PayPalCreatedOrder> CreateAuthorizeOrderAsync(
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
                    amount = new { currency_code = request.Amount.CurrencyCode, value = request.Amount.Value },
                    invoice_id = request.InvoiceId,
                    custom_id = request.CustomId,
                    description = request.Description
                }
            }
        };

        using var response = await SendAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            body,
            payPalRequestId,
            cancellationToken);

        using var document = await ReadDocumentAsync(response, cancellationToken);
        var root = document.RootElement;
        EnsureNoPayerActionRequired(root);

        return new PayPalCreatedOrder(
            RequiredString(root, "id"),
            OptionalString(root, "status") ?? "CREATED");
    }

    public async Task<PayPalAuthorization> AuthorizeOrderAsync(
        string payPalOrderId,
        PayPalCardPaymentSource paymentSource,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            payment_source = new
            {
                card = BuildCardPayload(paymentSource)
            }
        };

        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            body,
            payPalRequestId,
            cancellationToken);

        using var document = await ReadDocumentAsync(response, cancellationToken);
        var root = document.RootElement;
        EnsureNoPayerActionRequired(root);

        return ParseAuthorizationFromOrder(root, payPalOrderId);
    }

    public async Task<PayPalAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            requestId: null,
            cancellationToken);

        using var document = await ReadDocumentAsync(response, cancellationToken);
        return ParseAuthorization(document.RootElement, payPalOrderId: null);
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(
        string authorizationId,
        PayPalMoney amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = new { currency_code = amount.CurrencyCode, value = amount.Value }
        };

        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body,
            payPalRequestId,
            cancellationToken);

        using var document = await ReadDocumentAsync(response, cancellationToken);
        return ParseAuthorization(document.RootElement, payPalOrderId: null);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            new { },
            payPalRequestId,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK)
        {
            return;
        }

        await ThrowIfUnsuccessfulAsync(response, cancellationToken);
    }

    public async Task<PayPalCapture> CaptureAuthorizationAsync(
        string authorizationId,
        PayPalCaptureRequest request,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        object body = request.Amount is null
            ? new { final_capture = request.FinalCapture, invoice_id = request.InvoiceId }
            : new
            {
                amount = new { currency_code = request.Amount.CurrencyCode, value = request.Amount.Value },
                final_capture = request.FinalCapture,
                invoice_id = request.InvoiceId
            };

        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body,
            payPalRequestId,
            cancellationToken);

        using var document = await ReadDocumentAsync(response, cancellationToken);
        var capture = ParseCapture(document.RootElement);
        if (capture.PayPalFeeValue is null || capture.NetAmountValue is null)
        {
            return await GetCaptureAsync(capture.CaptureId, cancellationToken);
        }

        return capture;
    }

    public async Task<PayPalCapture> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}",
            null,
            requestId: null,
            cancellationToken);

        using var document = await ReadDocumentAsync(response, cancellationToken);
        return ParseCapture(document.RootElement);
    }

    public async Task<PayPalRefund> RefundCaptureAsync(
        string captureId,
        PayPalMoney? amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        object body = amount is null
            ? new { }
            : new { amount = new { currency_code = amount.CurrencyCode, value = amount.Value } };

        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body,
            payPalRequestId,
            cancellationToken);

        using var document = await ReadDocumentAsync(response, cancellationToken);
        var root = document.RootElement;
        return new PayPalRefund(
            RequiredString(root, "id"),
            OptionalString(root, "status") ?? "COMPLETED",
            OptionalMoneyValue(root, "amount"),
            OptionalMoneyCurrency(root, "amount"));
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await ListTransactionsForWindowAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        PayPalCardPaymentSource card,
        string merchantCustomerId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            payment_source = new { card = BuildCardPayload(card, forVault: true) },
            customer = new { merchant_customer_id = SanitizeMerchantCustomerId(merchantCustomerId) }
        };

        using var response = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            payPalRequestId,
            cancellationToken);

        using var document = await ReadDocumentAsync(response, cancellationToken);
        var root = document.RootElement;
        EnsureNoPayerActionRequired(root);
        return ParseVaultedCard(root);
    }

    public async Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}",
            null,
            requestId: null,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound)
        {
            return;
        }

        await ThrowIfUnsuccessfulAsync(response, cancellationToken);
    }

    private async Task ListTransactionsForWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<PayPalReportedTransaction> results,
        CancellationToken cancellationToken)
    {
        var page = 1;
        var totalPages = 1;
        do
        {
            var start = Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            var end = Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            var path = $"/v1/reporting/transactions?start_date={start}&end_date={end}&page_size=100&page={page}&fields=all&balance_affecting_records_only=N";

            using var response = await SendAsync(HttpMethod.Get, path, null, requestId: null, cancellationToken);
            using var document = await ReadDocumentAsync(response, cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var info = detail.TryGetProperty("transaction_info", out var transactionInfo)
                        ? transactionInfo
                        : detail;

                    results.Add(new PayPalReportedTransaction(
                        OptionalString(info, "transaction_id") ?? string.Empty,
                        OptionalString(info, "paypal_reference_id"),
                        OptionalString(info, "invoice_id"),
                        OptionalString(info, "custom_field"),
                        OptionalString(info, "transaction_event_code"),
                        OptionalString(info, "transaction_status"),
                        OptionalMoneyValue(info, "transaction_amount"),
                        OptionalMoneyCurrency(info, "transaction_amount"),
                        OptionalMoneyValue(info, "fee_amount"),
                        ParseTimestamp(OptionalString(info, "transaction_initiation_date"))));
                }
            }

            totalPages = root.TryGetProperty("total_pages", out var pagesEl) && pagesEl.TryGetInt32(out var pages)
                ? Math.Max(pages, 1)
                : page;

            page++;
        } while (page <= totalPages);
    }

    private static object BuildCardPayload(PayPalCardPaymentSource source, bool forVault = false)
    {
        object? storedCredential = null;
        if (source.IsStoredCredential)
        {
            storedCredential = new
            {
                payment_initiator = "CUSTOMER",
                payment_type = "UNSCHEDULED",
                usage = "SUBSEQUENT"
            };
        }

        object? billingAddress = null;
        if (source.BillingAddress != null)
        {
            billingAddress = new
            {
                country_code = source.BillingAddress.CountryCode,
                address_line_1 = source.BillingAddress.AddressLine1,
                address_line_2 = source.BillingAddress.AddressLine2,
                admin_area_2 = source.BillingAddress.AdminArea2,
                admin_area_1 = source.BillingAddress.AdminArea1,
                postal_code = source.BillingAddress.PostalCode
            };
        }

        object? attributes = null;

        return new
        {
            name = source.Name,
            number = source.Number,
            expiry = source.Expiry,
            security_code = source.SecurityCode,
            vault_id = source.VaultId,
            billing_address = billingAddress,
            stored_credential = storedCredential,
            attributes
        };
    }

    private PayPalAuthorization ParseAuthorizationFromOrder(JsonElement root, string fallbackOrderId)
    {
        var orderId = OptionalString(root, "id") ?? fallbackOrderId;
        JsonElement? authorization = null;
        if (root.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments) &&
                    payments.TryGetProperty("authorizations", out var auths) &&
                    auths.ValueKind == JsonValueKind.Array)
                {
                    foreach (var auth in auths.EnumerateArray())
                    {
                        authorization = auth;
                        break;
                    }
                }
            }
        }

        if (authorization is null)
        {
            throw new PaymentGatewayException(
                "PayPal authorized the order but did not return an authorization id.",
                502);
        }

        return ParseAuthorization(authorization.Value, orderId);
    }

    private static PayPalAuthorization ParseAuthorization(JsonElement root, string? payPalOrderId)
    {
        var relatedOrderId = payPalOrderId;
        if (root.TryGetProperty("supplementary_data", out var supplementary) &&
            supplementary.TryGetProperty("related_ids", out var related) &&
            related.TryGetProperty("order_id", out var relatedOrder))
        {
            relatedOrderId = relatedOrder.GetString() ?? relatedOrderId;
        }

        return new PayPalAuthorization(
            relatedOrderId ?? string.Empty,
            RequiredString(root, "id"),
            OptionalString(root, "status") ?? "CREATED",
            ParseTimestamp(OptionalString(root, "create_time")),
            ParseTimestamp(OptionalString(root, "expiration_time")),
            OptionalMoneyValue(root, "amount"),
            OptionalMoneyCurrency(root, "amount"));
    }

    private static PayPalCapture ParseCapture(JsonElement root)
    {
        string? fee = null;
        string? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = OptionalMoneyValue(breakdown, "paypal_fee");
            net = OptionalMoneyValue(breakdown, "net_amount");
        }

        return new PayPalCapture(
            RequiredString(root, "id"),
            OptionalString(root, "status") ?? "COMPLETED",
            OptionalMoneyValue(root, "amount"),
            OptionalMoneyCurrency(root, "amount"),
            fee,
            net);
    }

    private static PayPalVaultedCard ParseVaultedCard(JsonElement root)
    {
        string lastDigits = "0000";
        string brand = "CARD";
        string? expiry = null;
        if (root.TryGetProperty("payment_source", out var paymentSource) &&
            paymentSource.TryGetProperty("card", out var card))
        {
            lastDigits = OptionalString(card, "last_digits") ?? lastDigits;
            brand = OptionalString(card, "brand") ?? brand;
            expiry = OptionalString(card, "expiry");
        }

        string? customerId = null;
        if (root.TryGetProperty("customer", out var customer))
        {
            customerId = OptionalString(customer, "id");
        }

        return new PayPalVaultedCard(
            RequiredString(root, "id"),
            customerId,
            lastDigits,
            brand,
            expiry);
    }

    private void EnsureNoPayerActionRequired(JsonElement root)
    {
        var status = OptionalString(root, "status");
        var hasPayerActionLink = false;
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(OptionalString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    hasPayerActionLink = true;
                    break;
                }
            }
        }

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || hasPayerActionLink)
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper approval challenge that cannot be completed without a browser. Direct card processing did not complete.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var token = await GetAccessTokenAsync(cancellationToken);
        Exception? lastException = null;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(method, ApiUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (body != null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastException = ex;
                await DelayBackoffAsync(attempt, cancellationToken);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                _accessToken = null;
                token = await GetAccessTokenAsync(cancellationToken);
                continue;
            }

            var retryable = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
            var canRetry = retryable && (method == HttpMethod.Get || !string.IsNullOrEmpty(requestId)) && attempt < 3;
            if (canRetry)
            {
                response.Dispose();
                await DelayBackoffAsync(attempt, cancellationToken);
                continue;
            }

            await ThrowIfUnsuccessfulAsync(response, cancellationToken);
            return response;
        }

        throw new PaymentGatewayException(
            "PayPal request failed after retries.",
            502,
            payPalDebugId: null,
            payPalErrorName: lastException?.Message);
    }

    private async Task ThrowIfUnsuccessfulAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        string? name = null;
        string? message = payload;
        string? debugId = null;
        string? issue = null;
        try
        {
            using var errorDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            var root = errorDoc.RootElement;
            name = OptionalString(root, "name");
            message = OptionalString(root, "message") ?? message;
            debugId = OptionalString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    issue = OptionalString(detail, "issue") ?? issue;
                    var field = OptionalString(detail, "field");
                    var description = OptionalString(detail, "description");
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(issue)) parts.Add(issue);
                    if (!string.IsNullOrEmpty(field)) parts.Add($"field={field}");
                    if (!string.IsNullOrEmpty(description)) parts.Add(description);
                    if (parts.Count > 0)
                    {
                        message = $"{message}: {string.Join(" — ", parts)}";
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Keep the raw payload as the message when PayPal does not return JSON.
        }

        _logger.LogWarning(
            "PayPal API error status={Status} name={Name} issue={Issue} debugId={DebugId} message={Message}",
            (int)response.StatusCode,
            name,
            issue,
            debugId,
            message);

        if (string.Equals(issue, "ORDER_ALREADY_AUTHORIZED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentConflictException("This order has already been authorized with PayPal.");
        }

        var statusCode = (int)response.StatusCode is >= 400 and < 500 ? (int)response.StatusCode : 502;
        throw new PaymentGatewayException(
            $"PayPal request failed ({name ?? response.StatusCode.ToString()}): {message}",
            statusCode,
            debugId,
            name);
    }

    private async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new PaymentGatewayException("PayPal returned an empty response body.", 502);
        }

        return JsonDocument.Parse(payload);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUri("/v1/oauth2/token"));
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
                _logger.LogWarning("PayPal token request failed with status {Status}", (int)response.StatusCode);
                throw new PaymentGatewayException("Unable to authenticate with PayPal. Check PayPal:ClientId and PayPal:ClientSecret.", 502);
            }

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            _accessToken = RequiredString(root, "access_token");
            var expiresIn = root.TryGetProperty("expires_in", out var expiresEl) && expiresEl.TryGetInt32(out var seconds)
                ? seconds
                : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 30));
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
            throw new PaymentException("PayPal is not configured. Set PayPal:ClientId and PayPal:ClientSecret.", 500);
        }

        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new PaymentException("PayPal is not configured. Set PayPal:Currency.", 500);
        }
    }

    private Uri ApiUri(string path)
    {
        var baseUrl = ResolveBaseUrl().TrimEnd('/');
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return new Uri(baseUrl + path, UriKind.Absolute);
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return _options.BaseUrl.Trim();
        }

        var environment = _options.Environment?.Trim() ?? string.Empty;
        if (environment.Equals("live", StringComparison.OrdinalIgnoreCase) ||
            environment.Equals("production", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var builder = new StringBuilder();
        foreach (var c in buyerId)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '^' or '*' or '$' or '@' or '#')
            {
                builder.Append(c);
            }
        }

        var sanitized = builder.ToString();
        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64];
        }

        return string.IsNullOrEmpty(sanitized) ? "shopper" : sanitized;
    }

    private static async Task DelayBackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = (int)(Math.Pow(2, attempt) * 200 + Random.Shared.Next(0, 100));
        await Task.Delay(delayMs, cancellationToken);
    }

    private static string RequiredString(JsonElement element, string name)
    {
        var value = OptionalString(element, name);
        if (string.IsNullOrEmpty(value))
        {
            throw new PaymentGatewayException($"PayPal response was missing required field '{name}'.", 502);
        }

        return value;
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string? OptionalMoneyValue(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var money) && money.ValueKind == JsonValueKind.Object)
        {
            return OptionalString(money, "value");
        }

        return null;
    }

    private static string? OptionalMoneyCurrency(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var money) && money.ValueKind == JsonValueKind.Object)
        {
            return OptionalString(money, "currency_code");
        }

        return null;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }
}
