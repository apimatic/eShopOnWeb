using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex PanPattern = new(@"\b[0-9]{13,19}\b", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;
    private readonly string _baseUrl;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _baseUrl = ResolveBaseUrl(_options).TrimEnd('/');
    }

    public async Task<PayPalAuthorizationResult> AuthorizePaymentAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        var payPalOrderId = request.PayPalOrderId;
        if (string.IsNullOrEmpty(payPalOrderId))
        {
            var created = await CreateOrderAsync(request, cancellationToken);
            payPalOrderId = created.Id!;
            var existingAuth = ExtractAuthorization(created);
            if (existingAuth != null)
            {
                return ToAuthorizationResult(payPalOrderId, existingAuth);
            }

            EnsureNoPayerActionRequired(created);
        }

        var authorized = await SendJsonAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            $"/v2/checkout/orders/{payPalOrderId}/authorize",
            BuildAuthorizeBody(request),
            request.AuthorizeRequestId,
            cancellationToken);

        EnsureNoPayerActionRequired(authorized);

        var authorization = ExtractAuthorization(authorized)
            ?? throw new PayPalGatewayException(502, "MISSING_AUTHORIZATION",
                "PayPal authorized the order but did not return an authorization id.");

        return ToAuthorizationResult(authorized.Id ?? payPalOrderId, authorization);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var resource = await SendJsonAsync<PayPalAuthorizationResource>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return new PayPalAuthorizationDetails
        {
            Id = resource.Id!,
            Status = resource.Status ?? string.Empty,
            CreateTime = ParseTime(resource.CreateTime),
            ExpirationTime = ParseTime(resource.ExpirationTime),
            Amount = resource.Amount?.Value
        };
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        string amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var resource = await SendJsonAsync<PayPalAuthorizationResource>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            new { amount = new { currency_code = currency, value = amount } },
            requestId,
            cancellationToken);

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = resource.Id!,
            Status = resource.Status ?? "CREATED",
            CreateTime = ParseTime(resource.CreateTime),
            ExpirationTime = ParseTime(resource.ExpirationTime),
            Amount = resource.Amount?.Value
        };
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        string amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var capture = await SendJsonAsync<PayPalCaptureResource>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            new
            {
                amount = new { currency_code = currency, value = amount },
                invoice_id = invoiceId,
                final_capture = true
            },
            requestId,
            cancellationToken);

        var breakdown = capture.SellerReceivableBreakdown;
        return new PayPalCaptureResult
        {
            CaptureId = capture.Id!,
            Status = capture.Status ?? "COMPLETED",
            CapturedAmount = PayPalMoney.Parse(breakdown?.GrossAmount?.Value ?? capture.Amount?.Value),
            PaypalFee = breakdown?.PaypalFee?.Value is null ? null : PayPalMoney.Parse(breakdown.PaypalFee.Value),
            NetAmount = breakdown?.NetAmount?.Value is null ? null : PayPalMoney.Parse(breakdown.NetAmount.Value),
            Currency = capture.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: new { },
            requestId,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.Created)
        {
            return;
        }

        await ThrowOnErrorAsync(response);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        string? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object body = amount is null
            ? new { }
            : new { amount = new { currency_code = currency, value = amount } };

        var refund = await SendJsonAsync<PayPalRefundResource>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken);

        return new PayPalRefundResult
        {
            RefundId = refund.Id!,
            Status = refund.Status ?? "COMPLETED",
            Amount = PayPalMoney.Parse(refund.Amount?.Value ?? amount),
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken cancellationToken = default)
    {
        var setupBody = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(request.PayPalCustomerId))
        {
            setupBody["customer"] = new { id = request.PayPalCustomerId };
        }

        setupBody["payment_source"] = new
        {
            card = BuildCardPayload(request.Card, includeExperienceContext: true)
        };

        var setup = await SendJsonAsync<PayPalVaultResource>(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupBody,
            request.RequestId + "-setup",
            cancellationToken);

        if (string.Equals(setup.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper to complete a browser challenge while saving the card. This API does not implement an approval round-trip.");
        }

        var token = await SendJsonAsync<PayPalVaultResource>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            new
            {
                payment_source = new
                {
                    token = new
                    {
                        id = setup.Id,
                        type = "SETUP_TOKEN"
                    }
                }
            },
            request.RequestId + "-token",
            cancellationToken);

        var card = token.PaymentSource?.Card
            ?? throw new PayPalGatewayException(502, "MISSING_CARD", "PayPal vaulted the card but did not return card metadata.");

        return new PayPalVaultedCard
        {
            PaymentTokenId = token.Id!,
            CustomerId = token.Customer?.Id ?? setup.Customer?.Id,
            LastDigits = card.LastDigits ?? "****",
            Brand = card.Brand ?? "CARD",
            Expiry = card.Expiry ?? request.Card.Expiry,
            CardholderName = card.Name ?? request.Card.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{paymentTokenId}",
            body: null,
            requestId: null,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound)
        {
            return;
        }

        await ThrowOnErrorAsync(response);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(31);
            if (chunkEnd > to)
            {
                chunkEnd = to;
            }

            if (chunkEnd - chunkStart > TimeSpan.FromDays(31))
            {
                chunkEnd = chunkStart.AddDays(31).AddSeconds(-1);
                if (chunkEnd > to) chunkEnd = to;
            }

            var page = 1;
            int totalPages;
            do
            {
                var query =
                    $"start_date={Uri.EscapeDataString(FormatPayPalDate(chunkStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatPayPalDate(chunkEnd))}" +
                    $"&page_size=500&page={page}&fields=transaction_info&balance_affecting_records_only=N";

                var payload = await SendJsonAsync<PayPalTransactionSearchResponse>(
                    HttpMethod.Get,
                    $"/v1/reporting/transactions?{query}",
                    body: null,
                    requestId: null,
                    cancellationToken);

                if (payload.TransactionDetails != null)
                {
                    foreach (var detail in payload.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null) continue;
                        results.Add(new PayPalReportedTransaction
                        {
                            TransactionId = info.TransactionId,
                            PaypalReferenceId = info.PaypalReferenceId,
                            TransactionEventCode = info.TransactionEventCode,
                            TransactionStatus = info.TransactionStatus,
                            InvoiceId = info.InvoiceId,
                            CustomField = info.CustomField,
                            Amount = info.TransactionAmount?.Value,
                            Currency = info.TransactionAmount?.CurrencyCode,
                            FeeAmount = info.FeeAmount?.Value,
                            InitiationDate = ParseTime(info.TransactionInitiationDate)
                        });
                    }
                }

                totalPages = payload.TotalPages ?? 1;
                page++;
            } while (page <= totalPages);

            chunkStart = chunkEnd <= chunkStart ? chunkEnd.AddSeconds(1) : chunkEnd;
            if (chunkStart == chunkEnd)
            {
                break;
            }
        }

        return results;
    }

    private async Task<PayPalOrderResponse> CreateOrderAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new
                {
                    invoice_id = request.InvoiceId,
                    custom_id = request.CustomId ?? request.InvoiceId,
                    amount = new
                    {
                        currency_code = request.Currency,
                        value = request.Amount
                    }
                }
            }
        };

        var created = await SendJsonAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            body,
            request.CreateRequestId,
            cancellationToken);

        EnsureNoPayerActionRequired(created);
        return created;
    }

    private static object BuildAuthorizeBody(PayPalAuthorizeRequest request)
    {
        if (request.Card != null)
        {
            return new { payment_source = new { card = BuildCardPayload(request.Card, includeExperienceContext: false) } };
        }

        if (!string.IsNullOrEmpty(request.VaultId))
        {
            return new { payment_source = new { card = new { vault_id = request.VaultId } } };
        }

        return new { };
    }

    private static Dictionary<string, object?> BuildCardPayload(PayPalCardSource card, bool includeExperienceContext)
    {
        var payload = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };

        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            payload["security_code"] = card.SecurityCode;
        }

        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            payload["name"] = card.Name;
        }

        if (card.BillingAddress != null)
        {
            payload["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = card.BillingAddress.AddressLine1,
                ["address_line_2"] = card.BillingAddress.AddressLine2,
                ["admin_area_1"] = card.BillingAddress.AdminArea1,
                ["admin_area_2"] = card.BillingAddress.AdminArea2,
                ["postal_code"] = card.BillingAddress.PostalCode,
                ["country_code"] = string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode)
                    ? "US"
                    : card.BillingAddress.CountryCode
            };
        }

        if (includeExperienceContext)
        {
            payload["experience_context"] = new
            {
                brand_name = "eShopOnWeb",
                locale = "en-US",
                return_url = "https://example.com/returnUrl",
                cancel_url = "https://example.com/cancelUrl"
            };
        }

        return payload;
    }

    private static void EnsureNoPayerActionRequired(PayPalOrderResponse order)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper to complete a browser challenge (for example 3-D Secure). This API does not implement an approval round-trip.");
        }

        if (order.Links is null) return;
        foreach (var link in order.Links)
        {
            if (string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase))
            {
                throw new PayerActionRequiredException(
                    "PayPal required a shopper to complete a browser challenge. This API does not implement an approval round-trip.");
            }
        }
    }

    private static PayPalAuthorizationResource? ExtractAuthorization(PayPalOrderResponse order)
    {
        var units = order.PurchaseUnits;
        if (units is null) return null;
        foreach (var unit in units)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is { Count: > 0 })
            {
                return authorizations[0];
            }
        }

        return null;
    }

    private static PayPalAuthorizationResult ToAuthorizationResult(string payPalOrderId, PayPalAuthorizationResource authorization)
    {
        return new PayPalAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization.Id!,
            Status = authorization.Status ?? "CREATED",
            CreateTime = ParseTime(authorization.CreateTime),
            ExpirationTime = ParseTime(authorization.ExpirationTime),
            Amount = authorization.Amount?.Value
        };
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, requestId, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ThrowPayPalError(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new PayPalGatewayException((int)response.StatusCode, "EMPTY_RESPONSE", "PayPal returned an empty response.");
        }

        var parsed = JsonSerializer.Deserialize<T>(content, SerializerOptions);
        if (parsed is null)
        {
            throw new PayPalGatewayException((int)response.StatusCode, "INVALID_RESPONSE", "PayPal returned a response that could not be parsed.");
        }

        return parsed;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var url = Url(path);
        using var message = new HttpRequestMessage(method, url);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, SerializerOptions);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("PayPal {Method} {Path} failed with {Status}: {Error}",
                method, path, (int)response.StatusCode, Redact(error));
        }

        return response;
    }

    private async Task ThrowOnErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        ThrowPayPalError(response.StatusCode, content);
    }

    private void ThrowPayPalError(HttpStatusCode statusCode, string content)
    {
        var error = TryDeserializeError(content);
        var issue = error?.Details is { Count: > 0 } ? error.Details[0].Issue ?? error.Name ?? "PAYPAL_ERROR" : error?.Name ?? "PAYPAL_ERROR";
        var description = error?.Details is { Count: > 0 }
            ? error.Details[0].Description ?? error.Message
            : error?.Message;
        var message = string.IsNullOrWhiteSpace(description)
            ? $"PayPal request failed ({(int)statusCode})."
            : description!;

        if (!string.IsNullOrEmpty(error?.DebugId))
        {
            message += $" PayPal debug id: {error.DebugId}.";
        }

        throw new PayPalGatewayException((int)statusCode, issue, message, error?.DebugId);
    }

    private static PayPalErrorResponse? TryDeserializeError(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<PayPalErrorResponse>(content, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken!;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken!;
            }

            EnsureConfigured();

            using var request = new HttpRequestMessage(HttpMethod.Post, Url("/v1/oauth2/token"));
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
                _logger.LogWarning("PayPal token request failed with {Status}: {Error}", (int)response.StatusCode, Redact(content));
                ThrowPayPalError(response.StatusCode, content);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, SerializerOptions)
                ?? throw new PayPalGatewayException(502, "TOKEN_ERROR", "PayPal did not return an access token.");

            _accessToken = token.AccessToken;
            var lifetime = TimeSpan.FromSeconds(Math.Max(30, token.ExpiresIn - 60));
            _tokenExpiresAt = DateTimeOffset.UtcNow.Add(lifetime);
            return _accessToken!;
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
            throw new PaymentException(500,
                "PayPal is not configured. Set PayPal:ClientId and PayPal:ClientSecret (from PAYPAL_CLIENT_ID and PAYPAL_CLIENT_SECRET).");
        }
    }

    private string Url(string path)
    {
        return $"{_baseUrl}/{path.TrimStart('/')}";
    }

    private static string ResolveBaseUrl(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.Trim();
        }

        var environment = options.Environment?.Trim() ?? "sandbox";
        if (environment.Equals("live", StringComparison.OrdinalIgnoreCase) ||
            environment.Equals("production", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, out var parsed)) return parsed;
        return null;
    }

    private static string FormatPayPalDate(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    private static string Redact(string value) => PanPattern.Replace(value, "[REDACTED]");

    private sealed class PayPalTokenResponse
    {
        public string? AccessToken { get; set; }
        public int ExpiresIn { get; set; }
    }

    private sealed class PayPalOrderResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
        public List<PayPalLink>? Links { get; set; }
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
        public PayPalMoneyValue? Amount { get; set; }
        public string? CreateTime { get; set; }
        public string? ExpirationTime { get; set; }
    }

    private sealed class PayPalCaptureResource
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalMoneyValue? Amount { get; set; }
        public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
    }

    private sealed class PayPalSellerReceivableBreakdown
    {
        public PayPalMoneyValue? GrossAmount { get; set; }
        public PayPalMoneyValue? PaypalFee { get; set; }
        public PayPalMoneyValue? NetAmount { get; set; }
    }

    private sealed class PayPalRefundResource
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalMoneyValue? Amount { get; set; }
    }

    private sealed class PayPalMoneyValue
    {
        public string? CurrencyCode { get; set; }
        public string? Value { get; set; }
    }

    private sealed class PayPalLink
    {
        public string? Href { get; set; }
        public string? Rel { get; set; }
        public string? Method { get; set; }
    }

    private sealed class PayPalVaultResource
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalVaultCustomer? Customer { get; set; }
        public PayPalVaultPaymentSource? PaymentSource { get; set; }
        public List<PayPalLink>? Links { get; set; }
    }

    private sealed class PayPalVaultCustomer
    {
        public string? Id { get; set; }
    }

    private sealed class PayPalVaultPaymentSource
    {
        public PayPalVaultCard? Card { get; set; }
    }

    private sealed class PayPalVaultCard
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
        public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
        public int? Page { get; set; }
        public int? TotalItems { get; set; }
        public int? TotalPages { get; set; }
    }

    private sealed class PayPalTransactionDetail
    {
        public PayPalTransactionInfo? TransactionInfo { get; set; }
    }

    private sealed class PayPalTransactionInfo
    {
        public string? TransactionId { get; set; }
        public string? PaypalReferenceId { get; set; }
        public string? TransactionEventCode { get; set; }
        public string? TransactionStatus { get; set; }
        public string? InvoiceId { get; set; }
        public string? CustomField { get; set; }
        public string? TransactionInitiationDate { get; set; }
        public PayPalMoneyValue? TransactionAmount { get; set; }
        public PayPalMoneyValue? FeeAmount { get; set; }
    }
}
