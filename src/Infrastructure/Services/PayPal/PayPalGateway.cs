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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Hand-written PayPal client built against the OpenAPI specifications in /api-specs/paypal:
/// checkout_orders_v2, payments_payment_v2, vault_payment_tokens_v3, transaction_search_v1,
/// plus the OAuth2 client-credentials token endpoint declared by the specs' security scheme.
/// Card numbers pass through to PayPal only; they are never persisted or logged here.
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalGateway> logger)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal:ClientId and PayPal:ClientSecret must be configured (e.g. via .NET user-secrets populated from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables).");
        }

        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(ResolveBaseUrl(settings), UriKind.Absolute);
    }

    public static string ResolveBaseUrl(PayPalSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl!.TrimEnd('/');
        }

        return string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com";
    }

    public async Task<GatewayOrder> CreateOrderAsync(string referenceId, decimal amount, string currency, string idempotencyKey)
    {
        var request = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = referenceId,
                    custom_id = referenceId,
                    amount = new { currency_code = currency, value = FormatMoney(amount) }
                }
            }
        };

        var order = await SendAsync<PayPalOrderDto>(HttpMethod.Post, "/v2/checkout/orders", request, idempotencyKey);
        return new GatewayOrder { Id = order.Id ?? string.Empty, Status = order.Status ?? string.Empty };
    }

    public Task<GatewayAuthorization> AuthorizeOrderWithCardAsync(string gatewayOrderId, GatewayCardDetails card, string idempotencyKey)
    {
        var request = new
        {
            payment_source = new
            {
                card = new
                {
                    name = card.CardholderName,
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    billing_address = ToAddressDto(card.BillingAddress)
                }
            }
        };
        return AuthorizeOrderAsync(gatewayOrderId, request, idempotencyKey);
    }

    public Task<GatewayAuthorization> AuthorizeOrderWithVaultedCardAsync(string gatewayOrderId, string vaultTokenId, string idempotencyKey)
    {
        var request = new
        {
            payment_source = new
            {
                card = new
                {
                    vault_id = vaultTokenId,
                    stored_credential = new
                    {
                        payment_initiator = "CUSTOMER",
                        payment_type = "ONE_TIME",
                        usage = "SUBSEQUENT"
                    }
                }
            }
        };
        return AuthorizeOrderAsync(gatewayOrderId, request, idempotencyKey);
    }

    private async Task<GatewayAuthorization> AuthorizeOrderAsync(string gatewayOrderId, object request, string idempotencyKey)
    {
        var order = await SendAsync<PayPalOrderDto>(HttpMethod.Post, $"/v2/checkout/orders/{gatewayOrderId}/authorize", request, idempotencyKey);
        var authorization = order.FirstAuthorization()
            ?? throw new PaymentGatewayException($"PayPal order {gatewayOrderId} authorization response contained no authorization.", order.Status);
        return ToGatewayAuthorization(authorization);
    }

    public async Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null);
        return ToGatewayAuthorization(dto);
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey)
    {
        var request = new { amount = new { currency_code = currency, value = FormatMoney(amount) } };
        var dto = await SendAsync<PayPalAuthorizationDto>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", request, idempotencyKey);
        return ToGatewayAuthorization(dto);
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey)
    {
        var request = new
        {
            amount = new { currency_code = currency, value = FormatMoney(amount) },
            invoice_id = invoiceId,
            final_capture = true
        };
        var dto = await SendAsync<PayPalCaptureDto>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", request, idempotencyKey);
        return new GatewayCapture
        {
            Id = dto.Id ?? string.Empty,
            Status = dto.Status ?? string.Empty,
            Amount = ParseMoney(dto.Amount?.Value),
            Currency = dto.Amount?.CurrencyCode ?? currency,
            PayPalFee = dto.SellerReceivableBreakdown?.PayPalFee is null ? null : ParseMoney(dto.SellerReceivableBreakdown.PayPalFee.Value),
            NetAmount = dto.SellerReceivableBreakdown?.NetAmount is null ? null : ParseMoney(dto.SellerReceivableBreakdown.NetAmount.Value)
        };
    }

    public async Task<GatewayAuthorization> VoidAuthorizationAsync(string authorizationId, string idempotencyKey)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, idempotencyKey);
        return ToGatewayAuthorization(dto);
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string? noteToPayer)
    {
        var request = new
        {
            amount = amount is null ? null : new { currency_code = currency, value = FormatMoney(amount.Value) },
            custom_id = idempotencyKey,
            note_to_payer = noteToPayer
        };
        var dto = await SendAsync<PayPalRefundDto>(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", request, idempotencyKey);
        return new GatewayRefund
        {
            Id = dto.Id ?? string.Empty,
            Status = dto.Status ?? string.Empty,
            Amount = ParseMoney(dto.Amount?.Value),
            Currency = dto.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<GatewayVaultedCard> SaveCardAsync(string merchantCustomerId, GatewayCardDetails card, string idempotencyKey)
    {
        var request = new
        {
            customer = new { merchant_customer_id = merchantCustomerId },
            payment_source = new
            {
                card = new
                {
                    name = card.CardholderName,
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    billing_address = ToAddressDto(card.BillingAddress)
                }
            }
        };

        var dto = await SendAsync<PayPalVaultTokenDto>(HttpMethod.Post, "/v3/vault/payment-tokens", request, idempotencyKey);
        return new GatewayVaultedCard
        {
            VaultTokenId = dto.Id ?? string.Empty,
            Brand = dto.PaymentSource?.Card?.Brand,
            LastDigits = dto.PaymentSource?.Card?.LastDigits,
            Expiry = dto.PaymentSource?.Card?.Expiry,
            CardholderName = dto.PaymentSource?.Card?.Name
        };
    }

    public async Task DeleteSavedCardAsync(string vaultTokenId)
    {
        await SendAsync<object>(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var results = new List<GatewayTransaction>();
        const int pageSize = 500;
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var query = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatInstant(from))}" +
                        $"&end_date={Uri.EscapeDataString(FormatInstant(to))}" +
                        $"&fields=transaction_info&page_size={pageSize}&page={page}";

            var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, query, null, null);
            totalPages = response.TotalPages <= 0 ? page : response.TotalPages;

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new GatewayTransaction
                    {
                        TransactionId = info.TransactionId ?? string.Empty,
                        ReferenceId = info.PayPalReferenceId,
                        ReferenceIdType = info.PayPalReferenceIdType,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Amount = info.TransactionAmount is null ? null : ParseMoney(info.TransactionAmount.Value),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        FeeAmount = info.FeeAmount is null ? null : ParseMoney(info.FeeAmount.Value),
                        InitiationTime = ParseInstant(info.TransactionInitiationDate),
                        UpdatedTime = ParseInstant(info.TransactionUpdatedDate)
                    });
                }
            }

            page++;
        }

        return results;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey, bool preferRepresentation = true)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync());
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw ToGatewayException(response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload) || typeof(T) == typeof(object))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
            ?? throw new PaymentGatewayException($"PayPal returned an unreadable response for {method} {path}.");
    }

    private PaymentGatewayException ToGatewayException(HttpStatusCode statusCode, string payload)
    {
        try
        {
            var error = JsonSerializer.Deserialize<PayPalErrorDto>(payload, JsonOptions);
            if (error is not null)
            {
                var issues = error.Details is null
                    ? string.Empty
                    : " [" + string.Join("; ", error.Details.ConvertAll(d => $"{d.Issue}: {d.Description}")) + "]";
                _logger.LogWarning("PayPal request failed: {Name} {Message} (debug id {DebugId})",
                    error.Name, error.Message, error.DebugId);
                return new PaymentGatewayException($"PayPal error {error.Name}: {error.Message}{issues}", error.Name, error.DebugId);
            }
        }
        catch (JsonException)
        {
            // fall through to generic error
        }
        return new PaymentGatewayException($"PayPal request failed with HTTP {(int)statusCode}.");
    }

    private async Task<string> GetAccessTokenAsync()
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync();
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw ToGatewayException(response.StatusCode, payload);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(payload, JsonOptions)
                ?? throw new PaymentGatewayException("PayPal returned an unreadable OAuth token response.");
            if (string.IsNullOrEmpty(token.AccessToken))
            {
                throw new PaymentGatewayException("PayPal OAuth token response did not contain an access token.");
            }

            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 120 ? token.ExpiresIn : token.ExpiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static GatewayAuthorization ToGatewayAuthorization(PayPalAuthorizationDto dto)
    {
        return new GatewayAuthorization
        {
            Id = dto.Id ?? string.Empty,
            Status = dto.Status ?? string.Empty,
            Amount = ParseMoney(dto.Amount?.Value),
            Currency = dto.Amount?.CurrencyCode ?? string.Empty,
            ExpirationTime = ParseInstant(dto.ExpirationTime)
        };
    }

    private static object? ToAddressDto(GatewayAddress? address)
    {
        if (address is null) return null;
        return new
        {
            address_line_1 = address.AddressLine1,
            address_line_2 = address.AddressLine2,
            admin_area_2 = address.City,
            admin_area_1 = address.State,
            postal_code = address.PostalCode,
            country_code = address.CountryCode
        };
    }

    private static string FormatMoney(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

    private static string FormatInstant(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseInstant(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
}
