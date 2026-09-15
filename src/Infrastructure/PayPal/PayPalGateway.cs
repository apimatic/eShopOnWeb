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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalGateway : IPayPalGateway
{
    private const string TokenCacheKey = "paypal:access-token";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
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
        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl() + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string Currency =>
        string.IsNullOrWhiteSpace(_options.Currency) ? "USD" : _options.Currency.Trim().ToUpperInvariant();

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        CardPaymentDetails? card,
        string? vaultId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequestDto>
            {
                new()
                {
                    ReferenceId = "default",
                    InvoiceId = requestId,
                    CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                    Amount = new PayPalAmountDto
                    {
                        CurrencyCode = Currency,
                        Value = FormatAmount(amount)
                    }
                }
            },
            PaymentSource = new PayPalPaymentSourceDto
            {
                Card = BuildCard(card, vaultId)
            }
        };

        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "v2/checkout/orders",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        EnsureNoPayerAction(order);
        var authorization = GetAuthorization(order)
            ?? throw new PaymentException("PayPal did not return an authorization for the order.", 502);
        if (string.IsNullOrEmpty(order.Id) || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentException("PayPal authorization was missing identifiers.", 502);
        }

        return ToAuthorizationResult(order.Id, authorization);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            null,
            null,
            cancellationToken);

        return new PayPalAuthorizationDetails(
            dto.Id ?? authorizationId,
            dto.Status ?? string.Empty,
            ParseTimestamp(dto.ExpirationTime),
            dto.Amount?.Value,
            dto.Amount?.CurrencyCode);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequestDto
        {
            Amount = new PayPalMoneyDto { CurrencyCode = Currency, Value = FormatAmount(amount) }
        };

        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PaymentException("PayPal reauthorization did not return an authorization id.", 502);
        }

        return new PayPalAuthorizationResult(
            string.Empty,
            dto.Id,
            dto.Status ?? string.Empty,
            ParseTimestamp(dto.ExpirationTime),
            dto.Amount?.Value ?? FormatAmount(amount),
            dto.Amount?.CurrencyCode ?? Currency);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequestDto
        {
            Amount = new PayPalMoneyDto { CurrencyCode = Currency, Value = FormatAmount(amount) },
            FinalCapture = true
        };

        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        return ToCaptureResult(dto);
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void",
            null,
            requestId,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK)
        {
            return;
        }

        await ThrowIfErrorAsync(response, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object? body = null;
        if (amount.HasValue)
        {
            body = new PayPalRefundRequestDto
            {
                Amount = new PayPalMoneyDto
                {
                    CurrencyCode = currency,
                    Value = FormatAmount(amount.Value)
                }
            };
        }

        var dto = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PaymentException("PayPal refund did not return a refund id.", 502);
        }

        var refundAmount = ParseMoney(dto.Amount?.Value) ?? amount ?? 0m;
        return new PayPalRefundResult(dto.Id, dto.Status ?? string.Empty, refundAmount, dto.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalVaultResult> VaultCardAsync(
        string paypalCustomerId,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalVaultRequestDto
        {
            Customer = new PayPalCustomerDto { Id = paypalCustomerId },
            PaymentSource = new PayPalPaymentSourceDto { Card = BuildCard(card, vaultId: null) }
        };

        var dto = await SendAsync<PayPalVaultResponseDto>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            body,
            requestId,
            cancellationToken,
            paypalRequestIdHeader: "PayPal-Request-Id");

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PaymentException("PayPal vault did not return a payment token id.", 502);
        }

        var cardSource = dto.PaymentSource?.Card;
        return new PayPalVaultResult(
            dto.Id,
            cardSource?.Brand,
            cardSource?.LastDigits,
            cardSource?.Expiry,
            cardSource?.Name);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{paymentTokenId}",
            null,
            null,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
            return;
        }

        await ThrowIfErrorAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
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

            var page = 1;
            var totalPages = 1;
            do
            {
                var path =
                    "v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatTimestamp(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatTimestamp(windowEnd))}" +
                    "&fields=all&page_size=500" +
                    $"&page={page}";

                var dto = await SendAsync<PayPalTransactionSearchResponseDto>(
                    HttpMethod.Get, path, null, null, cancellationToken);

                if (dto.TransactionDetails != null)
                {
                    foreach (var detail in dto.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info == null || string.IsNullOrEmpty(info.TransactionId))
                        {
                            continue;
                        }

                        results.Add(new PayPalReportedTransaction(
                            info.TransactionId,
                            info.PaypalReferenceId,
                            info.TransactionEventCode,
                            info.TransactionStatus,
                            info.TransactionAmount?.Value,
                            info.TransactionAmount?.CurrencyCode,
                            info.InvoiceId,
                            info.CustomField,
                            ParseTimestamp(info.TransactionInitiationDate)));
                    }
                }

                totalPages = dto.TotalPages is > 0 ? dto.TotalPages.Value : 1;
                page++;
            } while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    private PayPalCardDto BuildCard(CardPaymentDetails? card, string? vaultId)
    {
        if (!string.IsNullOrEmpty(vaultId))
        {
            return new PayPalCardDto
            {
                VaultId = vaultId,
                StoredCredential = new PayPalStoredCredentialDto
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "UNSCHEDULED",
                    Usage = "SUBSEQUENT"
                }
            };
        }

        if (card == null)
        {
            throw new PaymentException("Card details are required.", 400);
        }

        return new PayPalCardDto
        {
            Number = NormalizeCardNumber(card.Number),
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress == null
                ? new PayPalAddressDto { CountryCode = "US" }
                : new PayPalAddressDto
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = NormalizeCountryCode(card.BillingAddress.CountryCode)
                }
        };
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false,
        string paypalRequestIdHeader = "PayPal-Request-Id")
    {
        using var response = await SendRawAsync(method, path, body, requestId, cancellationToken, preferRepresentation, paypalRequestIdHeader);
        await ThrowIfErrorAsync(response, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<T>(json, JsonOptions);
        if (parsed == null)
        {
            throw new PaymentException("PayPal returned an unreadable response.", 502);
        }

        return parsed;
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false,
        string paypalRequestIdHeader = "PayPal-Request-Id")
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation(paypalRequestIdHeader, requestId);
        }

        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        _logger.LogInformation("PayPal {Method} {Path} -> {StatusCode}", method, RedactPath(path), (int)response.StatusCode);
        return response;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PaymentException("PayPal ClientId and ClientSecret are not configured.", 500);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal token request failed with {StatusCode}.", (int)response.StatusCode);
            throw new PaymentException("Unable to authenticate with PayPal.", 502);
        }

        var token = JsonSerializer.Deserialize<PayPalTokenResponse>(json, JsonOptions);
        if (string.IsNullOrEmpty(token?.AccessToken))
        {
            throw new PaymentException("PayPal did not return an access token.", 502);
        }

        var lifetime = TimeSpan.FromSeconds(Math.Max(token.ExpiresIn - 60, 30));
        _cache.Set(TokenCacheKey, token.AccessToken, lifetime);
        return token.AccessToken;
    }

    private async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var error = SafeDeserialize<PayPalErrorResponse>(json);
        var issue = error?.Details is { Count: > 0 } ? error.Details[0].Issue : null;
        var description = error?.Details is { Count: > 0 } ? error.Details[0].Description : error?.Message;
        _logger.LogWarning(
            "PayPal error {Status} name={Name} issue={Issue} debugId={DebugId}",
            (int)response.StatusCode, error?.Name, issue, error?.DebugId);

        var status = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 409,
            _ => 502
        };

        throw new PaymentException(
            $"PayPal request failed ({error?.Name ?? response.StatusCode.ToString()}). {description ?? "See PayPal debug id."} DebugId={error?.DebugId}",
            status);
    }

    private static void EnsureNoPayerAction(PayPalOrderDto order)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            (order.Links != null && order.Links.Exists(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase))))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper challenge (for example 3-D Secure) that cannot be completed without a browser.");
        }
    }

    private static PayPalAuthorizationDto? GetAuthorization(PayPalOrderDto order)
        => order.PurchaseUnits is { Count: > 0 }
            ? order.PurchaseUnits[0].Payments?.Authorizations is { Count: > 0 } auths ? auths[0] : null
            : null;

    private PayPalAuthorizationResult ToAuthorizationResult(string orderId, PayPalAuthorizationDto authorization)
        => new(
            orderId,
            authorization.Id!,
            authorization.Status ?? string.Empty,
            ParseTimestamp(authorization.ExpirationTime),
            authorization.Amount?.Value ?? string.Empty,
            authorization.Amount?.CurrencyCode ?? Currency);

    private PayPalCaptureResult ToCaptureResult(PayPalCaptureDto dto)
    {
        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PaymentException("PayPal capture did not return a capture id.", 502);
        }

        var captured = ParseMoney(dto.SellerReceivableBreakdown?.GrossAmount?.Value)
                       ?? ParseMoney(dto.Amount?.Value)
                       ?? 0m;
        return new PayPalCaptureResult(
            dto.Id,
            dto.Status ?? string.Empty,
            captured,
            ParseMoney(dto.SellerReceivableBreakdown?.PaypalFee?.Value),
            ParseMoney(dto.SellerReceivableBreakdown?.NetAmount?.Value),
            dto.Amount?.CurrencyCode ?? Currency);
    }

    private string FormatAmount(decimal amount)
    {
        var decimals = Currency is "JPY" or "HUF" or "TWD" ? 0 : 2;
        return amount.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    private static string FormatTimestamp(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTimestamp(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static decimal? ParseMoney(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string NormalizeCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) ||
            string.Equals(countryCode, "United States", StringComparison.OrdinalIgnoreCase) ||
            countryCode.Length != 2)
        {
            return "US";
        }

        return countryCode.ToUpperInvariant();
    }

    private static string NormalizeCardNumber(string number)
        => number.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string NormalizeExpiry(string expiry)
    {
        var trimmed = expiry.Trim();
        if (trimmed.Contains('/', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('/');
            if (parts.Length == 2)
            {
                var month = parts[0].PadLeft(2, '0');
                var year = parts[1].Length == 2 ? "20" + parts[1] : parts[1];
                return $"{year}-{month}";
            }
        }

        return trimmed;
    }

    private static string RedactPath(string path)
        => path.Contains("payment-tokens", StringComparison.OrdinalIgnoreCase) ? "v3/vault/payment-tokens" : path;

    private static T? SafeDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
