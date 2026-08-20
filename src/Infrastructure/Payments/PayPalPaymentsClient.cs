using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalPaymentsClient : IPayPalPaymentsClient
{
    private const string TokenCacheKey = "paypal:access_token";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalPaymentsClient> _logger;

    public PayPalPaymentsClient(
        HttpClient httpClient,
        IOptions<PayPalOptions> options,
        IMemoryCache cache,
        ILogger<PayPalPaymentsClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string Currency
    {
        get
        {
            EnsureConfigured();
            return _options.Currency;
        }
    }

    public async Task<string> CreateAuthorizeOrderAsync(int orderId, decimal amount, string invoiceId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = "default",
                    invoice_id = invoiceId,
                    custom_id = orderId.ToString(CultureInfo.InvariantCulture),
                    amount = new
                    {
                        currency_code = _options.Currency,
                        value = FormatAmount(amount)
                    }
                }
            }
        };

        var response = await SendAsync<PaypalOrderResponse>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            payload,
            requestId: $"eshop-create-{invoiceId}",
            cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(response.Id))
        {
            throw new PaymentException("PayPal did not return an order id.");
        }

        EnsureNoPayerAction(response.Status, "creating the PayPal order");
        return response.Id;
    }

    public async Task<PaypalAuthorizationResult> AuthorizeOrderAsync(
        string paypalOrderId,
        string invoiceId,
        CardPaymentInput? card,
        string? vaultId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        object payload;
        if (!string.IsNullOrEmpty(vaultId))
        {
            payload = new
            {
                payment_source = new
                {
                    card = new
                    {
                        vault_id = vaultId
                    }
                }
            };
        }
        else if (card is not null)
        {
            payload = new
            {
                payment_source = new
                {
                    card = BuildCardSource(card)
                }
            };
        }
        else
        {
            throw new PaymentException("A card or saved payment method is required to authorize payment.");
        }

        PaypalOrderResponse response;
        try
        {
            response = await SendAsync<PaypalOrderResponse>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{paypalOrderId}/authorize",
                payload,
                requestId: $"eshop-pay-{invoiceId}",
                preferRepresentation: true,
                cancellationToken: cancellationToken);
        }
        catch (PaymentConflictException)
        {
            response = await SendAsync<PaypalOrderResponse>(
                HttpMethod.Get,
                $"/v2/checkout/orders/{paypalOrderId}",
                body: null,
                cancellationToken: cancellationToken);
        }

        EnsureNoPayerAction(response.Status, "authorizing the payment");
        var authorization = response.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PaypalAuthorizationResource>())
            .FirstOrDefault();

        if (authorization?.Id is null || authorization.Amount is null)
        {
            throw new PaymentException("PayPal did not return an authorization for the order.");
        }

        return new PaypalAuthorizationResult(
            response.Id ?? paypalOrderId,
            authorization.Id,
            authorization.Status ?? "CREATED",
            authorization.ExpirationTime,
            ParseAmount(authorization.Amount.Value),
            authorization.Amount.CurrencyCode ?? _options.Currency);
    }

    public async Task<PaypalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var resource = await SendAsync<PaypalAuthorizationResource>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            cancellationToken: cancellationToken);

        return ToAuthorizationDetails(resource);
    }

    public async Task<PaypalAuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var payload = new
        {
            amount = new
            {
                currency_code = _options.Currency,
                value = FormatAmount(amount)
            }
        };

        var resource = await SendAsync<PaypalAuthorizationResource>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            payload,
            requestId: $"eshop-reauth-{authorizationId}",
            preferRepresentation: true,
            cancellationToken: cancellationToken);

        return ToAuthorizationDetails(resource);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        try
        {
            await SendAsync<object>(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/void",
                body: new { },
                requestId: $"eshop-void-{authorizationId}",
                allowNoContent: true,
                cancellationToken: cancellationToken);
        }
        catch (PaymentException ex) when (IsAlreadyVoided(ex))
        {
            _logger.LogInformation("PayPal authorization {AuthorizationId} was already voided.", authorizationId);
        }
    }

    public async Task<PaypalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string invoiceId, decimal amount, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var payload = new
        {
            amount = new
            {
                currency_code = _options.Currency,
                value = FormatAmount(amount)
            },
            invoice_id = invoiceId,
            final_capture = true
        };

        var resource = await SendAsync<PaypalCaptureResource>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            payload,
            requestId: $"eshop-capture-{invoiceId}",
            preferRepresentation: true,
            cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(resource.Id))
        {
            throw new PaymentException("PayPal did not return a capture id.");
        }

        var captured = ParseAmount(resource.Amount?.Value);
        var fee = ParseAmount(resource.SellerReceivableBreakdown?.PaypalFee?.Value);
        var net = resource.SellerReceivableBreakdown?.NetAmount?.Value is not null
            ? ParseAmount(resource.SellerReceivableBreakdown.NetAmount.Value)
            : captured - fee;

        return new PaypalCaptureResult(
            resource.Id,
            resource.Status ?? "COMPLETED",
            captured,
            fee,
            net,
            resource.Amount?.CurrencyCode ?? _options.Currency);
    }

    public async Task<PaypalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        object payload = amount.HasValue
            ? new { amount = new { currency_code = _options.Currency, value = FormatAmount(amount.Value) } }
            : new { };

        var resource = await SendAsync<PaypalRefundResource>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            payload,
            requestId: $"eshop-refund-{captureId}-{idempotencyKey}",
            preferRepresentation: true,
            cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(resource.Id))
        {
            throw new PaymentException("PayPal did not return a refund id.");
        }

        return new PaypalRefundResult(
            resource.Id,
            resource.Status ?? "COMPLETED",
            ParseAmount(resource.Amount?.Value),
            resource.Amount?.CurrencyCode ?? _options.Currency);
    }

    public async Task<PaypalVaultedCard> VaultCardAsync(CardPaymentInput card, string? paypalCustomerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        object customer = string.IsNullOrEmpty(paypalCustomerId) ? null! : new { id = paypalCustomerId };
        var setupPayload = new Dictionary<string, object?>
        {
            ["payment_source"] = new
            {
                card = BuildCardSource(card)
            }
        };
        if (!string.IsNullOrEmpty(paypalCustomerId))
        {
            setupPayload["customer"] = customer;
        }

        var setup = await SendAsync<PaypalSetupTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupPayload,
            requestId: Guid.NewGuid().ToString("N"),
            cancellationToken: cancellationToken);

        EnsureNoPayerAction(setup.Status, "saving the card");
        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(setup.Id))
        {
            throw new PaymentException($"PayPal could not approve the card for vaulting (status: {setup.Status}).");
        }

        var tokenPayload = new
        {
            payment_source = new
            {
                token = new
                {
                    id = setup.Id,
                    type = "SETUP_TOKEN"
                }
            }
        };

        var token = await SendAsync<PaypalPaymentTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenPayload,
            requestId: Guid.NewGuid().ToString("N"),
            cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(token.Id))
        {
            throw new PaymentException("PayPal did not return a payment token for the saved card.");
        }

        var customerId = token.Customer?.Id ?? setup.Customer?.Id;
        if (string.IsNullOrEmpty(customerId))
        {
            throw new PaymentException("PayPal did not return a customer id for the saved card.");
        }

        return new PaypalVaultedCard(
            token.Id,
            customerId,
            token.PaymentSource?.Card?.Brand ?? setup.PaymentSource?.Card?.Brand,
            token.PaymentSource?.Card?.LastDigits ?? setup.PaymentSource?.Card?.LastDigits,
            token.PaymentSource?.Card?.Expiry ?? setup.PaymentSource?.Card?.Expiry,
            token.PaymentSource?.Card?.Name ?? setup.PaymentSource?.Card?.Name);
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        try
        {
            await SendAsync<object>(
                HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{paymentTokenId}",
                body: null,
                allowNoContent: true,
                cancellationToken: cancellationToken);
        }
        catch (PaymentNotFoundException)
        {
            _logger.LogInformation("PayPal payment token was already deleted.");
        }
    }

    public async Task<IReadOnlyList<PaypalReportedTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var results = new List<PaypalReportedTransaction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var window = TimeSpan.FromDays(31);
        var cursor = from;

        while (cursor <= to)
        {
            var windowEnd = cursor + window;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            int totalPages;
            do
            {
                var start = FormatReportingDate(cursor);
                var end = FormatReportingDate(windowEnd);
                var path =
                    $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=500&page={page}&balance_affecting_records_only=N";

                var search = await SendAsync<PaypalTransactionSearchResponse>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    cancellationToken: cancellationToken);

                foreach (var detail in search.TransactionDetails ?? Enumerable.Empty<PaypalTransactionDetail>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null || !seen.Add(info.TransactionId))
                    {
                        continue;
                    }

                    results.Add(new PaypalReportedTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.PaypalReferenceIdType,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        info.TransactionInitiationDate,
                        info.TransactionAmount?.Value is null ? null : ParseAmount(info.TransactionAmount.Value),
                        info.TransactionAmount?.CurrencyCode,
                        info.FeeAmount?.Value is null ? null : ParseAmount(info.FeeAmount.Value),
                        info.InvoiceId,
                        info.CustomField));
                }

                totalPages = search.TotalPages > 0 ? search.TotalPages : 1;
                page++;
            } while (page <= totalPages);

            if (windowEnd == to)
            {
                break;
            }

            cursor = windowEnd.AddTicks(1);
        }

        return results;
    }

    private object BuildCardSource(CardPaymentInput card)
    {
        return new
        {
            number = NormalizeCardNumber(card.Number),
            expiry = NormalizeExpiry(card.Expiry),
            security_code = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode.Trim(),
            name = string.IsNullOrWhiteSpace(card.Name) ? null : card.Name.Trim(),
            billing_address = card.BillingAddress is null ? null : new
            {
                address_line_1 = card.BillingAddress.AddressLine1,
                address_line_2 = card.BillingAddress.AddressLine2,
                admin_area_2 = card.BillingAddress.AdminArea2,
                admin_area_1 = card.BillingAddress.AdminArea1,
                postal_code = card.BillingAddress.PostalCode,
                country_code = card.BillingAddress.CountryCode
            }
        };
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId = null,
        bool preferRepresentation = false,
        bool allowNoContent = false,
        bool tokenRetry = true,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, Combine(_options.ResolveBaseUrl(), relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await SendWithRetryAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentException($"PayPal request failed: {ex.Message}");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized && tokenRetry)
        {
            _cache.Remove(TokenCacheKey);
            return await SendAsync<T>(method, relativePath, body, requestId, preferRepresentation, allowNoContent, tokenRetry: false, cancellationToken);
        }

        var content = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToPaymentException(response.StatusCode, content);
        }

        if (allowNoContent && (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(content)))
        {
            return default!;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        var parsed = JsonSerializer.Deserialize<T>(content, JsonOptions);
        if (parsed is null)
        {
            throw new PaymentException("PayPal returned an empty response body.");
        }

        return parsed;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var attemptRequest = await CloneRequestAsync(request, cancellationToken);
            response = await _httpClient.SendAsync(attemptRequest, cancellationToken);
            if ((int)response.StatusCode < 500 && response.StatusCode != (HttpStatusCode)429)
            {
                return response;
            }

            if (attempt == 2)
            {
                return response;
            }

            var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt));
            if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
            {
                delay = retryAfter;
            }

            _logger.LogWarning("Retrying PayPal {Method} {Path} after {StatusCode}.", request.Method, request.RequestUri, (int)response.StatusCode);
            await Task.Delay(delay, cancellationToken);
        }

        return response!;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(_options.ResolveBaseUrl(), "/v1/oauth2/token"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PayPal token request failed with {StatusCode} debug_id from body omitted.", (int)response.StatusCode);
            throw new PaymentException("Unable to authenticate with PayPal. Check PayPal:ClientId and PayPal:ClientSecret.");
        }

        var token = JsonSerializer.Deserialize<PaypalTokenResponse>(content, JsonOptions);
        if (string.IsNullOrEmpty(token?.AccessToken))
        {
            throw new PaymentException("PayPal did not return an access token.");
        }

        var lifetime = TimeSpan.FromSeconds(Math.Max(token.ExpiresIn - 60, 30));
        _cache.Set(TokenCacheKey, token.AccessToken, lifetime);
        return token.AccessToken;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PaymentException("PayPal:ClientId and PayPal:ClientSecret must be configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new PaymentException("PayPal:Currency must be configured.");
        }
    }

    private static void EnsureNoPayerAction(string? status, string operation)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                $"PayPal required a shopper approval in the browser while {operation}. This integration does not perform a browser round-trip.");
        }
    }

    private PaypalAuthorizationDetails ToAuthorizationDetails(PaypalAuthorizationResource resource)
    {
        if (string.IsNullOrEmpty(resource.Id))
        {
            throw new PaymentException("PayPal did not return authorization details.");
        }

        return new PaypalAuthorizationDetails(
            resource.Id,
            resource.Status ?? "CREATED",
            resource.CreateTime,
            resource.ExpirationTime,
            ParseAmount(resource.Amount?.Value),
            resource.Amount?.CurrencyCode ?? _options.Currency);
    }

    private PaymentException ToPaymentException(HttpStatusCode statusCode, string content)
    {
        var error = TryDeserializeError(content);
        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description ?? error?.Message ?? "PayPal request failed.";
        var debug = string.IsNullOrEmpty(error?.DebugId) ? string.Empty : $" (debug_id {error.DebugId})";
        var message = string.IsNullOrEmpty(issue) ? $"{description}{debug}" : $"{issue}: {description}{debug}";

        _logger.LogWarning("PayPal API error {StatusCode} {Name} {Issue} {DebugId}", (int)statusCode, error?.Name, issue, error?.DebugId);

        if (string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || description.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return new PayerActionRequiredException(message);
        }

        return statusCode switch
        {
            HttpStatusCode.NotFound => new PaymentNotFoundException(message),
            HttpStatusCode.Conflict => new PaymentConflictException(message),
            HttpStatusCode.Forbidden => new PaymentForbiddenException(message),
            HttpStatusCode.Unauthorized => new PaymentException(message, HttpStatusCode.Unauthorized),
            HttpStatusCode.UnprocessableEntity => new PaymentException(message, HttpStatusCode.UnprocessableEntity),
            _ => new PaymentException(message, statusCode)
        };
    }

    private static PaypalErrorResponse? TryDeserializeError(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PaypalErrorResponse>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsAlreadyVoided(PaymentException ex)
    {
        return ex.Message.Contains("VOIDED", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("already voided", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCardNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new PaymentException("Card number is required.");
        }

        return new string(number.Where(char.IsDigit).ToArray());
    }

    private static string NormalizeExpiry(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            throw new PaymentException("Card expiry is required in YYYY-MM format.");
        }

        expiry = expiry.Trim();
        if (expiry.Length == 7 && expiry[4] == '-')
        {
            return expiry;
        }

        var parts = expiry.Split('/', '-', ' ');
        if (parts.Length == 2 && parts[0].Length is 1 or 2)
        {
            var month = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var yearPart = parts[1];
            var year = yearPart.Length == 2
                ? 2000 + int.Parse(yearPart, CultureInfo.InvariantCulture)
                : int.Parse(yearPart, CultureInfo.InvariantCulture);
            return $"{year:D4}-{month:D2}";
        }

        throw new PaymentException("Card expiry must be YYYY-MM.");
    }

    private static string FormatAmount(decimal amount)
        => decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Round(decimal.Parse(value, CultureInfo.InvariantCulture), 2, MidpointRounding.AwayFromZero);
    }

    private static string FormatReportingDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static Uri Combine(string baseUrl, string relativePath)
    {
        return new Uri($"{baseUrl.TrimEnd('/')}{relativePath}");
    }
}
