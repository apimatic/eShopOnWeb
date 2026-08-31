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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Plain-HTTP client for the PayPal REST APIs (OAuth, Orders v2, Payments v2,
/// Vault v3, Transaction Search v1). Request bodies may contain full card
/// details; they are therefore never logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_settings.GetBaseUrl() + "/");
    }

    public async Task<PayPalOrderCreated> CreateOrderAsync(decimal amount, string currency, string referenceId,
        PayPalCardDetails? card, string? vaultTokenId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new CreateOrderRequestDto
        {
            PurchaseUnits = new List<PurchaseUnitDto>
            {
                new PurchaseUnitDto
                {
                    ReferenceId = referenceId,
                    CustomId = referenceId,
                    Amount = new AmountDto { CurrencyCode = currency, Value = Format(amount) }
                }
            },
            PaymentSource = new PaymentSourceDto
            {
                Card = vaultTokenId is not null
                    ? new CardDto { VaultId = vaultTokenId }
                    : ToCardDto(card!)
            }
        };

        var response = await SendAsync<OrderResponseDto>(HttpMethod.Post, "v2/checkout/orders", request, requestId, cancellationToken);
        var inlineAuthorization = response.PurchaseUnits?
            .SelectMany(p => p.Payments?.Authorizations ?? new List<AuthorizationDto>())
            .FirstOrDefault();
        return new PayPalOrderCreated(
            response.Id ?? string.Empty,
            response.Status ?? string.Empty,
            response.PaymentSource?.Card?.Brand,
            response.PaymentSource?.Card?.LastDigits,
            inlineAuthorization?.Id is null ? null : ToAuthorizationInfo(inlineAuthorization));
    }

    public async Task<PayPalAuthorizationInfo> AuthorizeOrderAsync(string payPalOrderId, string requestId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<OrderResponseDto>(
            HttpMethod.Post, $"v2/checkout/orders/{payPalOrderId}/authorize", body: null, requestId, cancellationToken);

        var authorization = response.PurchaseUnits?
            .SelectMany(p => p.Payments?.Authorizations ?? new List<AuthorizationDto>())
            .FirstOrDefault();
        if (authorization?.Id is null)
        {
            throw new PaymentException(
                $"PayPal order {payPalOrderId} returned status {response.Status} without an authorization.");
        }
        return ToAuthorizationInfo(authorization);
    }

    public async Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationDto>(
            HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", body: null, requestId: null, cancellationToken);
        return ToAuthorizationInfo(response);
    }

    public async Task<PayPalCaptureInfo> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var request = new CaptureRequestDto
        {
            Amount = new AmountDto { CurrencyCode = currency, Value = Format(amount) },
            FinalCapture = true
        };
        var response = await SendAsync<CaptureDto>(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture", request, requestId, cancellationToken);
        return ToCaptureInfo(response, currency);
    }

    public async Task<PayPalCaptureInfo> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<CaptureDto>(
            HttpMethod.Get, $"v2/payments/captures/{captureId}", body: null, requestId: null, cancellationToken);
        return ToCaptureInfo(response, string.Empty);
    }

    public async Task<PayPalAuthorizationInfo> ReauthorizeAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var request = new ReauthorizeRequestDto
        {
            Amount = new AmountDto { CurrencyCode = currency, Value = Format(amount) }
        };
        var response = await SendAsync<AuthorizationDto>(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize", request, requestId, cancellationToken);
        return ToAuthorizationInfo(response);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", body: null, requestId, cancellationToken);
    }

    public async Task<PayPalRefundInfo> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var request = new RefundRequestDto
        {
            Amount = amount.HasValue
                ? new AmountDto { CurrencyCode = currency, Value = Format(amount.Value) }
                : null
        };
        var response = await SendAsync<RefundResponseDto>(
            HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", request, requestId, cancellationToken);
        return new PayPalRefundInfo(
            response.Id ?? string.Empty,
            response.Status ?? string.Empty,
            ParseAmount(response.Amount?.Value),
            response.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalSetupTokenInfo> CreateSetupTokenAsync(PayPalCardDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new SetupTokenRequestDto
        {
            PaymentSource = new PaymentSourceDto { Card = ToCardDto(card) }
        };
        var response = await SendAsync<SetupTokenResponseDto>(
            HttpMethod.Post, "v3/vault/setup-tokens", request, requestId, cancellationToken);
        return new PayPalSetupTokenInfo(response.Id ?? string.Empty, response.Status ?? string.Empty, response.Customer?.Id);
    }

    public async Task<PayPalPaymentTokenInfo> CreatePaymentTokenAsync(string setupTokenId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PaymentTokenRequestDto
        {
            PaymentSource = new PaymentSourceDto
            {
                Token = new TokenDto { Id = setupTokenId, Type = "SETUP_TOKEN" }
            }
        };
        var response = await SendAsync<PaymentTokenResponseDto>(
            HttpMethod.Post, "v3/vault/payment-tokens", request, requestId, cancellationToken);
        return new PayPalPaymentTokenInfo(
            response.Id ?? string.Empty,
            response.Customer?.Id,
            response.PaymentSource?.Card?.Brand,
            response.PaymentSource?.Card?.LastDigits,
            response.PaymentSource?.Card?.Expiry);
    }

    public async Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultTokenId}", body: null, requestId: null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransactionInfo>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // The Transaction Search API supports a maximum range of 31 days per call.
        var results = new Dictionary<string, PayPalTransactionInfo>(StringComparer.OrdinalIgnoreCase);
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;
            var page = 1;
            var totalPages = 1;
            while (page <= totalPages)
            {
                var url = "v1/reporting/transactions"
                    + $"?start_date={FormatInstant(windowStart)}&end_date={FormatInstant(windowEnd)}"
                    + $"&fields=transaction_info&page_size=500&page={page}";
                var response = await SendAsync<TransactionsResponseDto>(HttpMethod.Get, url, body: null, requestId: null, cancellationToken);
                totalPages = Math.Max(response.TotalPages, 1);

                foreach (var detail in response.TransactionDetails ?? new List<TransactionDetailDto>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }
                    results[info.TransactionId] = new PayPalTransactionInfo(
                        info.TransactionId,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseAmount(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionInitiationDate);
                }
                page++;
            }
            windowStart = windowEnd;
        }
        return results.Values.ToList();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            _settings.Validate();

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with status {StatusCode}", (int)response.StatusCode);
                throw new PayPalApiException(response.StatusCode, null,
                    "PayPal rejected the client credentials; verify PayPal:ClientId / PayPal:ClientSecret.");
            }

            var token = await ReadJsonAsync<OAuthTokenResponse>(response, cancellationToken);
            _accessToken = token.AccessToken
                ?? throw new PayPalApiException(response.StatusCode, null, "PayPal returned no access token.");
            // Refresh a minute early to avoid using a token at the edge of expiry.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, string? requestId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, url, body, requestId, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, string? requestId, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        // Never log the request body: it can contain full card details.
        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        ErrorResponseDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<ErrorResponseDto>(errorBody, JsonOptions);
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through to a generic message.
        }

        var message = error?.Message ?? $"PayPal call {method} {url} failed with status {(int)response.StatusCode}.";
        if (error?.Details?.Count > 0)
        {
            message += " " + string.Join("; ", error.Details.Select(d => d.Description ?? d.Issue).Where(d => d is not null));
        }
        _logger.LogWarning("PayPal {Method} {Url} failed: {StatusCode} {ErrorName} (debug id {DebugId})",
            method, url, (int)response.StatusCode, error?.Name, error?.DebugId);
        response.Dispose();
        throw new PayPalApiException(response.StatusCode, error?.Name, message, error?.DebugId);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default!;
        }
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new PayPalApiException(response.StatusCode, null, "PayPal returned an empty response body.");
    }

    private static CardDto ToCardDto(PayPalCardDetails card)
    {
        return new CardDto
        {
            Number = card.Number,
            Expiry = card.Expiry,
            Name = card.Name,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress is null ? null : new AddressDto
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AdminArea1 = card.BillingAddress.AdminArea1,
                AdminArea2 = card.BillingAddress.AdminArea2,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
        };
    }

    private static PayPalAuthorizationInfo ToAuthorizationInfo(AuthorizationDto dto)
    {
        return new PayPalAuthorizationInfo(
            dto.Id ?? string.Empty,
            dto.Status ?? string.Empty,
            ParseAmount(dto.Amount?.Value) ?? 0m,
            dto.Amount?.CurrencyCode ?? string.Empty,
            dto.ExpirationTime);
    }

    private static PayPalCaptureInfo ToCaptureInfo(CaptureDto dto, string currency)
    {
        var breakdown = dto.SellerReceivableBreakdown;
        return new PayPalCaptureInfo(
            dto.Id ?? string.Empty,
            dto.Status ?? string.Empty,
            ParseAmount(breakdown?.GrossAmount?.Value ?? dto.Amount?.Value) ?? 0m,
            ParseAmount(breakdown?.PayPalFee?.Value),
            ParseAmount(breakdown?.NetAmount?.Value),
            breakdown?.GrossAmount?.CurrencyCode ?? dto.Amount?.CurrencyCode ?? currency);
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string FormatInstant(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
