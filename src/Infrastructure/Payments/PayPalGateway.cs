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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxTransactionSearchWindow = TimeSpan.FromDays(31);

    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly PayPalAccessTokenCache _tokenCache;

    public PayPalGateway(
        HttpClient http,
        IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger,
        PayPalAccessTokenCache tokenCache)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _tokenCache = tokenCache;
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public string Currency
    {
        get
        {
            EnsureConfigured();
            return _options.Currency;
        }
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        string merchantReference,
        decimal amount,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken) =>
        AuthorizeAsync(merchantReference, amount, BuildCardPaymentSource(card), requestId, cancellationToken);

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        string merchantReference,
        decimal amount,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var paymentSource = new Dictionary<string, object?>
        {
            ["card"] = new Dictionary<string, object?>
            {
                ["vault_id"] = vaultId
            }
        };
        return AuthorizeAsync(merchantReference, amount, paymentSource, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new CheckoutException("PayPal returned an authorization without an id.", 502);
        }

        return new PayPalAuthorizationDetails(
            dto.Id,
            dto.Status ?? string.Empty,
            ParseTime(dto.ExpirationTime),
            ParseTime(dto.CreateTime),
            ToMoney(dto.Amount));
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = MoneyPayload(amount)
        };

        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            cancellationToken);

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new CheckoutException("PayPal reauthorization did not return an authorization id.", 502);
        }

        return new PayPalAuthorizationResult(
            string.Empty,
            dto.Id,
            dto.Status ?? string.Empty,
            ParseTime(dto.ExpirationTime),
            ParseTime(dto.CreateTime),
            ToMoney(dto.Amount) ?? new PayPalMoney(_options.Currency, amount));
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = MoneyPayload(amount),
            ["final_capture"] = true
        };

        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken);

        return ToCaptureResult(dto, amount);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Get,
            $"/v2/payments/captures/{captureId}",
            body: null,
            requestId: null,
            cancellationToken);

        return ToCaptureResult(dto, 0m);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync<PayPalAuthorizationDto>(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/void",
                body: new Dictionary<string, object?>(),
                requestId,
                cancellationToken,
                allowEmpty: true);
        }
        catch (CheckoutException ex) when (ex.Message.Contains("AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase)
                                           || ex.Message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("PayPal authorization {AuthorizationId} was already voided.", authorizationId);
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string requestId,
        CancellationToken cancellationToken)
    {
        object body = amount.HasValue
            ? new Dictionary<string, object?> { ["amount"] = MoneyPayload(amount.Value) }
            : new Dictionary<string, object?>();

        var dto = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken);

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new CheckoutException("PayPal refund did not return a refund id.", 502);
        }

        return new PayPalRefundResult(
            dto.Id,
            dto.Status ?? string.Empty,
            ToMoney(dto.Amount) ?? new PayPalMoney(_options.Currency, amount ?? 0m));
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentDetails card,
        string? paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardObject(card)
            }
        };

        if (!string.IsNullOrEmpty(paypalCustomerId))
        {
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = paypalCustomerId };
        }

        var setup = await SendAsync<PayPalSetupTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupBody,
            requestId + "-setup",
            cancellationToken);

        ThrowIfChallenge(setup.Status, setup.Links, setup.Id);

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(
                $"PayPal did not approve the card for vaulting (status {setup.Status}).", 502);
        }

        if (string.IsNullOrEmpty(setup.Id))
        {
            throw new CheckoutException("PayPal setup token response did not include an id.", 502);
        }

        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = setup.Id,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenBody,
            requestId + "-token",
            cancellationToken);

        if (string.IsNullOrEmpty(token.Id))
        {
            throw new CheckoutException("PayPal payment token response did not include an id.", 502);
        }

        var cardSource = token.PaymentSource?.Card ?? setup.PaymentSource?.Card;
        return new PayPalVaultedCard(
            token.Id,
            token.Customer?.Id ?? setup.Customer?.Id,
            string.IsNullOrEmpty(cardSource?.Brand) ? "CARD" : cardSource.Brand,
            cardSource?.LastDigits ?? LastDigitsOf(card.Number),
            cardSource?.Expiry ?? card.Expiry,
            cardSource?.Name ?? card.Name);
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{paymentTokenId}",
            body: null,
            requestId: null,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from.ToUniversalTime();
        var end = to.ToUniversalTime();

        while (windowStart <= end)
        {
            var windowEnd = windowStart + MaxTransactionSearchWindow;
            if (windowEnd > end)
            {
                windowEnd = end;
            }

            results.AddRange(await ListTransactionsInWindow(windowStart, windowEnd, cancellationToken));
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task<List<PayPalReportedTransaction>> ListTransactionsInWindow(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var collected = new List<PayPalReportedTransaction>();
        var page = 1;
        int totalPages;
        do
        {
            var start = FormatReportingDate(from);
            var end = FormatReportingDate(to);
            var path =
                $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=500&page={page}";

            PayPalTransactionSearchResponse response;
            try
            {
                response = await SendAsync<PayPalTransactionSearchResponse>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    cancellationToken);
            }
            catch (CheckoutException ex) when (
                ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("RESULTSET_TOO_LARGE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("PayPal reporting returned no usable data for {Start} to {End}: {Message}", start, end, ex.Message);
                return collected;
            }

            if (response.TransactionDetails != null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info == null)
                    {
                        continue;
                    }

                    collected.Add(new PayPalReportedTransaction(
                        info.TransactionId ?? string.Empty,
                        info.PaypalReferenceId,
                        info.InvoiceId,
                        info.CustomField,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionAmount == null ? null : MoneyFormatter.Parse(info.TransactionAmount.Value),
                        ParseTime(info.TransactionInitiationDate),
                        ParseTime(info.TransactionUpdatedDate)));
                }
            }

            totalPages = response.TotalPages <= 0 ? 1 : response.TotalPages;
            page++;
        } while (page <= totalPages);

        return collected;
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        string merchantReference,
        decimal amount,
        object paymentSource,
        string requestId,
        CancellationToken cancellationToken)
    {
        var invoiceId = $"{merchantReference}-{Guid.NewGuid():N}";
        if (invoiceId.Length > 127)
        {
            invoiceId = invoiceId[..127];
        }

        var createBody = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["custom_id"] = merchantReference,
                    ["invoice_id"] = invoiceId,
                    ["amount"] = MoneyPayload(amount)
                }
            },
            ["payment_source"] = paymentSource
        };

        var order = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createBody,
            requestId,
            cancellationToken);

        ThrowIfChallenge(order.Status, order.Links, order.Id);

        var authorization = FirstAuthorization(order);
        if (authorization == null &&
            (string.Equals(order.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
             || string.Equals(order.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrEmpty(order.Id))
            {
                throw new CheckoutException("PayPal create order did not return an order id.", 502);
            }

            order = await SendAsync<PayPalOrderResponse>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize",
                new Dictionary<string, object?>(),
                requestId + "-authorize",
                cancellationToken);

            ThrowIfChallenge(order.Status, order.Links, order.Id);
            authorization = FirstAuthorization(order);
        }

        if (authorization == null || string.IsNullOrEmpty(authorization.Id) || string.IsNullOrEmpty(order.Id))
        {
            throw new CheckoutException(
                $"PayPal did not return an authorization for order {order.Id} (status {order.Status}).", 502);
        }

        return new PayPalAuthorizationResult(
            order.Id,
            authorization.Id,
            authorization.Status ?? string.Empty,
            ParseTime(authorization.ExpirationTime),
            ParseTime(authorization.CreateTime),
            ToMoney(authorization.Amount) ?? new PayPalMoney(_options.Currency, amount));
    }

    private static PayPalAuthorizationDto? FirstAuthorization(PayPalOrderResponse order) =>
        order.PurchaseUnits?.Count > 0 ? order.PurchaseUnits[0].Payments?.Authorizations?.Count > 0
            ? order.PurchaseUnits[0].Payments!.Authorizations![0]
            : null
            : null;

    private static Dictionary<string, object?> BuildCardPaymentSource(CardPaymentDetails card) =>
        new() { ["card"] = BuildCardObject(card) };

    private static Dictionary<string, object?> BuildCardObject(CardPaymentDetails card)
    {
        var billing = new Dictionary<string, object?>
        {
            ["address_line_1"] = card.BillingAddress.AddressLine1,
            ["admin_area_1"] = card.BillingAddress.AdminArea1,
            ["admin_area_2"] = card.BillingAddress.AdminArea2,
            ["postal_code"] = card.BillingAddress.PostalCode,
            ["country_code"] = card.BillingAddress.CountryCode
        };

        if (!string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine2))
        {
            billing["address_line_2"] = card.BillingAddress.AddressLine2;
        }

        var cardObject = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["name"] = card.Name,
            ["billing_address"] = billing
        };

        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            cardObject["security_code"] = card.SecurityCode;
        }

        return cardObject;
    }

    private PayPalCaptureResult ToCaptureResult(PayPalCaptureDto dto, decimal fallbackAmount)
    {
        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new CheckoutException("PayPal capture response did not include an id.", 502);
        }

        var amount = ToMoney(dto.Amount) ?? ToMoney(dto.SellerReceivableBreakdown?.GrossAmount)
                     ?? new PayPalMoney(_options.Currency, fallbackAmount);

        return new PayPalCaptureResult(
            dto.Id,
            dto.Status ?? string.Empty,
            amount,
            ToMoney(dto.SellerReceivableBreakdown?.PaypalFee),
            ToMoney(dto.SellerReceivableBreakdown?.NetAmount));
    }

    private Dictionary<string, string> MoneyPayload(decimal amount) =>
        new()
        {
            ["currency_code"] = _options.Currency,
            ["value"] = MoneyFormatter.ToPayPalValue(amount)
        };

    private static PayPalMoney? ToMoney(PayPalMoneyDto? dto)
    {
        if (dto == null || string.IsNullOrEmpty(dto.CurrencyCode) || string.IsNullOrEmpty(dto.Value))
        {
            return null;
        }

        return new PayPalMoney(dto.CurrencyCode, MoneyFormatter.Parse(dto.Value));
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

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string LastDigitsOf(string number) =>
        number.Length <= 4 ? number : number[^4..];

    private void ThrowIfChallenge(string? status, List<PayPalLinkDto>? links, string? resourceId)
    {
        var hasPayerAction = links?.Exists(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) == true;

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || hasPayerAction)
        {
            _logger.LogWarning("PayPal required a browser challenge for resource {ResourceId}.", resourceId);
            throw new PaymentChallengeRequiredException();
        }
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowEmpty = false)
        where T : class, new()
    {
        EnsureConfigured();
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = BuildRequest(method, path, body, requestId, token);
        using var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            InvalidateToken();
            token = await GetAccessTokenAsync(cancellationToken);
            using var retry = BuildRequest(method, path, body, requestId, token);
            using var retryResponse = await _http.SendAsync(retry, cancellationToken);
            var retryContent = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
            return ParseResponse<T>(retryResponse, retryContent, method, path, allowEmpty);
        }

        return ParseResponse<T>(response, content, method, path, allowEmpty);
    }

    private T ParseResponse<T>(HttpResponseMessage response, string content, HttpMethod method, string path, bool allowEmpty)
        where T : class, new()
    {
        _logger.LogInformation(
            "PayPal {Method} {Path} -> {StatusCode}",
            method,
            SanitizePath(path),
            (int)response.StatusCode);

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                if (allowEmpty)
                {
                    return new T();
                }

                throw new CheckoutException($"PayPal returned an empty success body for {method} {SanitizePath(path)}.", 502);
            }

            var parsed = JsonSerializer.Deserialize<T>(content, JsonOptions);
            return parsed ?? new T();
        }

        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(content, JsonOptions);
        }
        catch (JsonException)
        {
            // Fall through with a generic error.
        }

        var issue = error?.Details is { Count: > 0 } ? error.Details[0].Issue : null;
        var description = error?.Details is { Count: > 0 } ? error.Details[0].Description : null;
        var message =
            $"{error?.Name ?? "PAYPAL_ERROR"}: {error?.Message ?? "PayPal request failed."}" +
            (string.IsNullOrEmpty(issue) ? string.Empty : $" ({issue})") +
            (string.IsNullOrEmpty(description) ? string.Empty : $" {description}") +
            (string.IsNullOrEmpty(error?.DebugId) ? string.Empty : $" debug_id={error.DebugId}");

        var statusCode = response.StatusCode switch
        {
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            HttpStatusCode.UnprocessableEntity => 409,
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Forbidden => 403,
            _ => 502
        };

        throw new CheckoutException(message, statusCode);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body, string? requestId, string token)
    {
        var request = new HttpRequestMessage(method, Combine(GetBaseUrl(), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body != null && method != HttpMethod.Get)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_tokenCache.AccessToken) && DateTimeOffset.UtcNow < _tokenCache.ExpiresAt - TokenRefreshSkew)
        {
            return _tokenCache.AccessToken!;
        }

        await _tokenCache.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_tokenCache.AccessToken) && DateTimeOffset.UtcNow < _tokenCache.ExpiresAt - TokenRefreshSkew)
            {
                return _tokenCache.AccessToken!;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, Combine(GetBaseUrl(), "/v1/oauth2/token"));
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
                _logger.LogWarning("PayPal token request failed with {StatusCode}.", (int)response.StatusCode);
                throw new CheckoutException("Unable to authenticate with PayPal. Check PayPal:ClientId and PayPal:ClientSecret.", 502);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, JsonOptions);
            if (token?.AccessToken == null)
            {
                throw new CheckoutException("PayPal token response did not include an access token.", 502);
            }

            _tokenCache.AccessToken = token.AccessToken;
            _tokenCache.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 0 ? 300 : token.ExpiresIn);
            return _tokenCache.AccessToken;
        }
        finally
        {
            _tokenCache.Gate.Release();
        }
    }

    private void InvalidateToken()
    {
        _tokenCache.AccessToken = null;
        _tokenCache.ExpiresAt = DateTimeOffset.MinValue;
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return _options.BaseUrl.TrimEnd('/');
        }

        var environment = _options.Environment?.Trim();
        if (string.Equals(environment, "live", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new CheckoutException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret (from PAYPAL_CLIENT_ID and PAYPAL_CLIENT_SECRET).",
                500);
        }

        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new CheckoutException("PayPal:Currency is not configured (from PAYPAL_CURRENCY).", 500);
        }
    }

    private static string Combine(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static string SanitizePath(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? path[..q] : path;
    }
}
