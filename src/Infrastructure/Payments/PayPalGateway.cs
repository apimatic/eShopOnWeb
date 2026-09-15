using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private const string TokenCacheKey = "paypal:access-token";
    private static readonly Regex PanPattern = new(@"\d{13,19}", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly string _baseUrl;

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
        _baseUrl = _options.ResolveBaseUrl();
        _httpClient.BaseAddress ??= new Uri(_baseUrl + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public string Currency =>
        string.IsNullOrWhiteSpace(_options.Currency) ? "USD" : _options.Currency.Trim().ToUpperInvariant();

    public async Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        string invoiceId,
        string customId,
        decimal amount,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceDto { Card = ToCardDto(card) };
        return await AuthorizePaymentAsync(invoiceId, customId, amount, paymentSource, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeVaultedCardPaymentAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceDto
        {
            Card = new PayPalCardDto { VaultId = vaultId }
        };
        return await AuthorizePaymentAsync(invoiceId, customId, amount, paymentSource, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            preferRepresentation: true,
            cancellationToken);

        return new PayPalAuthorizationDetails(
            dto.Id ?? authorizationId,
            dto.Status ?? string.Empty,
            Money.Parse(dto.Amount?.Value),
            dto.Amount?.CurrencyCode ?? Currency,
            ParseTimestamp(dto.CreateTime),
            ParseTimestamp(dto.ExpirationTime));
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string originalAuthorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalReauthorizeRequest
        {
            Amount = new PayPalMoneyDto
            {
                CurrencyCode = Currency,
                Value = Money.ToPayPalValue(amount)
            }
        };

        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{originalAuthorizationId}/reauthorize",
            request,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new PaymentException(502, "PayPal reauthorization succeeded but did not return an authorization id.");
        }

        var authorizedAt = ParseTimestamp(dto.CreateTime) ?? DateTimeOffset.UtcNow;
        return new PayPalAuthorizationResult(
            PayPalOrderId: string.Empty,
            dto.Id,
            dto.Status ?? "CREATED",
            Money.Parse(dto.Amount?.Value),
            dto.Amount?.CurrencyCode ?? Currency,
            authorizedAt,
            ParseTimestamp(dto.ExpirationTime));
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalCaptureRequest
        {
            Amount = new PayPalMoneyDto
            {
                CurrencyCode = Currency,
                Value = Money.ToPayPalValue(amount)
            },
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            request,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new PaymentException(502, "PayPal capture succeeded but did not return a capture id.");
        }

        if (dto.SellerReceivableBreakdown == null)
        {
            dto = await SendAsync<PayPalCaptureDto>(
                HttpMethod.Get,
                $"/v2/payments/captures/{dto.Id}",
                body: null,
                requestId: null,
                preferRepresentation: true,
                cancellationToken);
        }

        return ToCaptureResult(dto);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<PayPalAuthorizationDto>(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/void",
                body: new { },
                requestId: null,
                preferRepresentation: true,
                cancellationToken,
                allowEmpty: true);
        }
        catch (PaymentException ex) when (IsAlreadyVoided(ex))
        {
            _logger.LogInformation("PayPal authorization {AuthorizationId} was already voided.", authorizationId);
        }
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        object request;
        if (amount.HasValue)
        {
            request = new PayPalRefundRequest
            {
                Amount = new PayPalMoneyDto
                {
                    CurrencyCode = currency,
                    Value = Money.ToPayPalValue(amount.Value)
                }
            };
        }
        else
        {
            request = new { };
        }

        var dto = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            request,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new PaymentException(502, "PayPal refund succeeded but did not return a refund id.");
        }

        return new PayPalRefundResult(
            dto.Id,
            dto.Status ?? "COMPLETED",
            Money.Parse(dto.Amount?.Value),
            dto.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string? payPalCustomerId,
        CancellationToken cancellationToken = default)
    {
        var setupRequest = new PayPalSetupTokenRequest
        {
            Customer = string.IsNullOrWhiteSpace(payPalCustomerId)
                ? null
                : new PayPalCustomerDto { Id = payPalCustomerId },
            PaymentSource = new PayPalPaymentSourceDto
            {
                Card = ToCardDto(card, includeExperienceContext: true)
            }
        };

        var setup = await SendAsync<PayPalSetupTokenDto>(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupRequest,
            Guid.NewGuid().ToString("N"),
            preferRepresentation: true,
            cancellationToken);

        EnsureNoPayerAction(setup.Status, setup.Links, "vault setup");

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(422,
                $"PayPal did not approve the card for vaulting (status: {setup.Status}).");
        }

        if (string.IsNullOrWhiteSpace(setup.Id))
        {
            throw new PaymentException(502, "PayPal setup token was missing an id.");
        }

        var tokenRequest = new PayPalPaymentTokenRequest
        {
            PaymentSource = new PayPalPaymentSourceDto
            {
                Token = new PayPalTokenDto
                {
                    Id = setup.Id,
                    Type = "SETUP_TOKEN"
                }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenDto>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenRequest,
            Guid.NewGuid().ToString("N"),
            preferRepresentation: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(token.Id))
        {
            throw new PaymentException(502, "PayPal did not return a payment token id for the saved card.");
        }

        var savedCard = token.PaymentSource?.Card ?? setup.PaymentSource?.Card;
        return new PayPalVaultedCard(
            token.Id,
            token.Customer?.Id ?? setup.Customer?.Id,
            savedCard?.Brand ?? "CARD",
            savedCard?.LastDigits ?? LastFourFromNumber(card.Number),
            savedCard?.Expiry ?? card.Expiry,
            savedCard?.Name ?? card.Name);
    }

    public async Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<PayPalPaymentTokenDto>(
                HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{paymentTokenId}",
                body: null,
                requestId: null,
                preferRepresentation: false,
                cancellationToken,
                allowEmpty: true);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation("PayPal payment token {PaymentTokenId} was already deleted.", paymentTokenId);
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var (chunkFrom, chunkTo) in SplitIntoPayPalWindows(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = FormatPayPalDate(chunkFrom);
                var end = FormatPayPalDate(chunkTo);
                var path =
                    $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&page={page}&page_size=500&fields=all";

                var search = await SendAsync<PayPalTransactionSearchDto>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    preferRepresentation: false,
                    cancellationToken);

                if (search.TransactionDetails != null)
                {
                    foreach (var detail in search.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info == null)
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
                            string.IsNullOrWhiteSpace(info.TransactionAmount?.Value)
                                ? null
                                : Money.Parse(info.TransactionAmount.Value),
                            string.IsNullOrWhiteSpace(info.FeeAmount?.Value)
                                ? null
                                : Money.Parse(info.FeeAmount.Value),
                            info.TransactionAmount?.CurrencyCode,
                            ParseTimestamp(info.TransactionInitiationDate)));
                    }
                }

                totalPages = search.TotalPages.GetValueOrDefault(1);
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> AuthorizePaymentAsync(
        string invoiceId,
        string customId,
        decimal amount,
        PayPalPaymentSourceDto paymentSource,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var createRequest = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitDto>
            {
                new()
                {
                    InvoiceId = invoiceId,
                    CustomId = customId,
                    Amount = new PayPalMoneyDto
                    {
                        CurrencyCode = Currency,
                        Value = Money.ToPayPalValue(amount)
                    }
                }
            },
            PaymentSource = paymentSource
        };

        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createRequest,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        EnsureNoPayerAction(order.Status, order.Links, "payment");

        var authorization = ExtractAuthorization(order);
        if (authorization == null)
        {
            order = await SendAsync<PayPalOrderDto>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize",
                new PayPalAuthorizeRequest(),
                idempotencyKey + "-authorize",
                preferRepresentation: true,
                cancellationToken);

            EnsureNoPayerAction(order.Status, order.Links, "payment authorization");
            authorization = ExtractAuthorization(order);
        }

        if (authorization == null || string.IsNullOrWhiteSpace(authorization.Id))
        {
            throw new PaymentException(502, "PayPal did not return an authorization for the payment.");
        }

        var authorizedAt = ParseTimestamp(authorization.CreateTime) ?? DateTimeOffset.UtcNow;
        return new PayPalAuthorizationResult(
            order.Id ?? string.Empty,
            authorization.Id,
            authorization.Status ?? "CREATED",
            Money.Parse(authorization.Amount?.Value),
            authorization.Amount?.CurrencyCode ?? Currency,
            authorizedAt,
            ParseTimestamp(authorization.ExpirationTime));
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        bool preferRepresentation,
        CancellationToken cancellationToken,
        bool allowEmpty = false)
    {
        var response = await SendRawAsync(method, path, body, requestId, preferRepresentation, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _cache.Remove(TokenCacheKey);
            response.Dispose();
            response = await SendRawAsync(method, path, body, requestId, preferRepresentation, cancellationToken);
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw MapPayPalError(response.StatusCode, content);
            }

            if (allowEmpty && string.IsNullOrWhiteSpace(content))
            {
                return Activator.CreateInstance<T>();
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new PaymentException(502, "PayPal returned an empty success response.");
            }

            var parsed = JsonSerializer.Deserialize<T>(content, JsonOptions);
            if (parsed == null)
            {
                throw new PaymentException(502, "PayPal returned a response that could not be read.");
            }

            return parsed;
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        bool preferRepresentation,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, Combine(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PaymentException(500, "PayPal credentials are not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Combine("/v1/oauth2/token"));
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
            throw MapPayPalError(response.StatusCode, content);
        }

        var token = JsonSerializer.Deserialize<PayPalOAuthTokenDto>(content, JsonOptions);
        if (token?.AccessToken == null)
        {
            throw new PaymentException(502, "PayPal token response did not include an access token.");
        }

        var lifetime = token.ExpiresIn > 120 ? token.ExpiresIn - 60 : Math.Max(token.ExpiresIn / 2, 30);
        _cache.Set(TokenCacheKey, token.AccessToken, TimeSpan.FromSeconds(lifetime));
        return token.AccessToken;
    }

    private string Combine(string path)
    {
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return _baseUrl.TrimEnd('/') + path;
    }

    private static PayPalCardDto ToCardDto(CardPaymentSource card, bool includeExperienceContext = false)
    {
        return new PayPalCardDto
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress == null
                ? null
                : new PayPalAddressDto
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                },
            ExperienceContext = includeExperienceContext
                ? new PayPalExperienceContextDto
                {
                    BrandName = "eShopOnWeb",
                    Locale = "en-US",
                    ReturnUrl = "https://example.com/return",
                    CancelUrl = "https://example.com/cancel"
                }
                : null
        };
    }

    private static PayPalAuthorizationDto? ExtractAuthorization(PayPalOrderDto order)
    {
        var units = order.PurchaseUnits;
        if (units == null)
        {
            return null;
        }

        foreach (var unit in units)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is { Count: > 0 } && !string.IsNullOrWhiteSpace(authorizations[0].Id))
            {
                return authorizations[0];
            }
        }

        return null;
    }

    private static PayPalCaptureResult ToCaptureResult(PayPalCaptureDto dto)
    {
        var captured = Money.Parse(dto.Amount?.Value);
        var fee = dto.SellerReceivableBreakdown?.PaypalFee?.Value;
        var net = dto.SellerReceivableBreakdown?.NetAmount?.Value;
        return new PayPalCaptureResult(
            dto.Id ?? string.Empty,
            dto.Status ?? string.Empty,
            captured,
            string.IsNullOrWhiteSpace(fee) ? null : Money.Parse(fee),
            string.IsNullOrWhiteSpace(net) ? null : Money.Parse(net),
            dto.Amount?.CurrencyCode ?? string.Empty);
    }

    private static void EnsureNoPayerAction(string? status, List<PayPalLinkDto>? links, string operation)
    {
        var hasPayerActionLink = links?.Exists(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true;

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || hasPayerActionLink)
        {
            throw new PayerActionRequiredException(
                $"PayPal required a shopper approval challenge during {operation}. This integration does not support a browser round-trip.");
        }
    }

    private static bool IsAlreadyVoided(PaymentException exception)
    {
        var message = exception.Message;
        return message.Contains("AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase)
               || message.Contains("already been voided", StringComparison.OrdinalIgnoreCase)
               || message.Contains("previously voided", StringComparison.OrdinalIgnoreCase);
    }

    private PaymentException MapPayPalError(HttpStatusCode statusCode, string content)
    {
        var sanitized = PanPattern.Replace(content ?? string.Empty, "[redacted]");
        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(content ?? string.Empty, JsonOptions);
        }
        catch (JsonException)
        {
            // Fall through to a generic mapped error.
        }

        var issue = error?.Details is { Count: > 0 } ? error.Details[0].Issue : null;
        var description = error?.Details is { Count: > 0 } ? error.Details[0].Description : null;
        var message = !string.IsNullOrWhiteSpace(description)
            ? $"{error?.Name}: {issue} — {description}"
            : !string.IsNullOrWhiteSpace(error?.Message)
                ? $"{error?.Name}: {error!.Message}"
                : $"PayPal request failed with {(int)statusCode}.";

        message = PanPattern.Replace(message, "[redacted]");
        _logger.LogWarning("PayPal API error {StatusCode} debugId={DebugId}: {Message}",
            (int)statusCode, error?.DebugId, message);

        var mapped = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 422,
            HttpStatusCode.TooManyRequests => 429,
            _ => 502
        };

        if (string.Equals(issue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "MAX_CAPTURE_AMOUNT_EXCEEDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "CAPTURE_FULLY_REFUNDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "REFUND_AMOUNT_EXCEEDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "CARD_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "DECLINED", StringComparison.OrdinalIgnoreCase))
        {
            mapped = 422;
        }

        _ = sanitized;
        return new PaymentException(mapped, message);
    }

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitIntoPayPalWindows(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var cursor = from;
        var maxWindow = TimeSpan.FromDays(31);
        while (cursor < to)
        {
            var windowEnd = cursor + maxWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            if (windowEnd - cursor > maxWindow)
            {
                windowEnd = cursor.AddDays(31).AddSeconds(-1);
            }

            yield return (cursor, windowEnd);
            cursor = windowEnd;
            if (cursor < to)
            {
                cursor = cursor.AddSeconds(1);
            }
        }
    }

    private static string FormatPayPalDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string LastFourFromNumber(string number)
    {
        var digits = new string((number ?? string.Empty).ToCharArray());
        digits = Regex.Replace(digits, @"\D", string.Empty);
        return digits.Length >= 4 ? digits[^4..] : digits;
    }
}
