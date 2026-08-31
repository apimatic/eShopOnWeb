using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal Payments API client: OAuth client-credentials auth, Orders v2, Payments v2,
/// Vault v3 and Transaction Search v1. Request bodies (which may carry card data) are
/// never logged; only method, path, status code and PayPal's debug id are.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const int MaxSearchWindowDays = 31;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, PayPalSettings settings, IAppLogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(settings.GetBaseUrl() + "/");
    }

    // ----- Orders v2 -----

    public async Task<string> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new CreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequestDto>
            {
                new()
                {
                    ReferenceId = referenceId,
                    InvoiceId = invoiceId,
                    CustomId = referenceId,
                    Amount = new PayPalMoneyDto(currency, FormatMoney(amount))
                }
            }
        };

        var response = await SendAsync<OrderResponseDto>(HttpMethod.Post, "v2/checkout/orders", request,
            idempotencyKey, cancellationToken);
        return response.Id ?? throw new PayPalApiException(200, null, null, "PayPal create order response had no id.");
    }

    public Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(string payPalOrderId, PayPalCardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new AuthorizeOrderRequestDto
        {
            PaymentSource = new PaymentSourceRequestDto
            {
                Card = new CardRequestDto
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };
        return AuthorizeOrderAsync(payPalOrderId, request, idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultedCardAsync(string payPalOrderId, string vaultTokenId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new AuthorizeOrderRequestDto
        {
            PaymentSource = new PaymentSourceRequestDto
            {
                Card = new CardRequestDto
                {
                    VaultId = vaultTokenId,
                    StoredCredential = new StoredCredentialDto
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "ONE_TIME"
                    }
                }
            }
        };
        return AuthorizeOrderAsync(payPalOrderId, request, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalOrderDetails> GetOrderAsync(string payPalOrderId, CancellationToken cancellationToken = default)
    {
        var order = await SendAsync<OrderResponseDto>(HttpMethod.Get, $"v2/checkout/orders/{payPalOrderId}",
            null, null, cancellationToken);
        return MapOrderDetails(order);
    }

    // ----- Payments v2 -----

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<AuthorizationDto>(HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return MapAuthorization(authorization, requiresPayerAction: false);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new ReauthorizeRequestDto { Amount = new PayPalMoneyDto(currency, FormatMoney(amount)) };
        var authorization = await SendAsync<AuthorizationDto>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize", request, idempotencyKey, cancellationToken);
        return MapAuthorization(authorization, requiresPayerAction: false);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new CaptureRequestDto
        {
            Amount = new PayPalMoneyDto(currency, FormatMoney(amount)),
            FinalCapture = true
        };
        var capture = await SendAsync<CaptureDto>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture", request, idempotencyKey, cancellationToken);
        return MapCapture(capture);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync<AuthorizationDto>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void", new { }, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency,
        string? noteToPayer, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new RefundRequestDto
        {
            Amount = new PayPalMoneyDto(currency, FormatMoney(amount)),
            NoteToPayer = noteToPayer,
            CustomId = idempotencyKey
        };
        var refund = await SendAsync<RefundDto>(HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund", request, idempotencyKey, cancellationToken);

        return new PayPalRefundResult(
            refund.Id ?? string.Empty,
            refund.Status ?? string.Empty,
            ParseMoney(refund.Amount?.Value),
            refund.Amount?.CurrencyCode ?? currency);
    }

    // ----- Vault v3 -----

    public async Task<string> CreateSetupTokenAsync(string customerId, PayPalCardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new SetupTokenRequestDto
        {
            Customer = new CustomerDto(customerId),
            PaymentSource = new SetupTokenPaymentSourceDto
            {
                Card = new CardRequestDto
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        var response = await SendAsync<SetupTokenResponseDto>(HttpMethod.Post, "v3/vault/setup-tokens", request,
            idempotencyKey, cancellationToken);
        return response.Id ?? throw new PayPalApiException(200, null, null, "PayPal setup token response had no id.");
    }

    public async Task<string> CreatePaymentTokenAsync(string customerId, string setupTokenId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PaymentTokenRequestDto
        {
            Customer = new CustomerDto(customerId),
            PaymentSource = new PaymentTokenSourceRequestDto
            {
                Token = new TokenIdDto { Id = setupTokenId, Type = "SETUP_TOKEN" }
            }
        };

        var response = await SendAsync<PaymentTokenResponseDto>(HttpMethod.Post, "v3/vault/payment-tokens", request,
            idempotencyKey, cancellationToken);
        return response.Id ?? throw new PayPalApiException(200, null, null, "PayPal payment token response had no id.");
    }

    public async Task<PayPalVaultedCard> GetPaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PaymentTokenResponseDto>(HttpMethod.Get, $"v3/vault/payment-tokens/{paymentTokenId}",
            null, null, cancellationToken);
        var card = response.PaymentSource?.Card;
        return new PayPalVaultedCard(
            response.Id ?? paymentTokenId,
            card?.Brand,
            card?.LastDigits,
            card?.Expiry,
            card?.Name);
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{paymentTokenId}", null, null, cancellationToken);
    }

    // ----- Transaction Search v1 -----

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransactionRecord>();

        // The API window is limited to 31 days per request; chunk the range.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(MaxSearchWindowDays) < to ? windowStart.AddDays(MaxSearchWindowDays) : to;

            var page = 1;
            var totalPages = 1;
            do
            {
                var query = $"v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatTimestamp(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatTimestamp(windowEnd))}" +
                    $"&fields=transaction_info" +
                    $"&balance_affecting_records_only=N" +
                    $"&page_size=100&page={page}";

                var response = await SendAsync<TransactionSearchResponseDto>(HttpMethod.Get, query, null, null, cancellationToken);
                totalPages = response.TotalPages > 0 ? response.TotalPages : 1;

                foreach (var detail in response.TransactionDetails ?? new List<TransactionDetailDto>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    results.Add(new PayPalTransactionRecord(
                        info.TransactionId,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        info.TransactionAmount is null ? null : ParseMoney(info.TransactionAmount.Value),
                        info.TransactionAmount?.CurrencyCode,
                        info.FeeAmount is null ? null : ParseMoney(info.FeeAmount.Value),
                        ParseTimestamp(info.TransactionInitiationDate),
                        info.InvoiceId,
                        info.PayPalReferenceId));
                }

                page++;
            } while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    // ----- internals -----

    private async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, AuthorizeOrderRequestDto request,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var order = await SendAsync<OrderResponseDto>(HttpMethod.Post,
            $"v2/checkout/orders/{payPalOrderId}/authorize", request, idempotencyKey, cancellationToken);

        // A "payer-action" link means PayPal wants the shopper to complete a challenge
        // (e.g. 3-D Secure) in a browser, which this integration does not support.
        var requiresPayerAction = order.Links?.Any(l => l.Rel == "payer-action") == true;

        var authorization = order.PurchaseUnits?
            .Select(u => u.Payments?.Authorizations)
            .FirstOrDefault(a => a is { Count: > 0 })
            ?.First();

        if (authorization?.Id is null)
        {
            throw new PayPalApiException(200, null, null,
                $"PayPal authorize response for order {payPalOrderId} contained no authorization (order status: {order.Status}).");
        }

        return MapAuthorization(authorization, requiresPayerAction);
    }

    private static PayPalOrderDetails MapOrderDetails(OrderResponseDto order)
    {
        var authorizations = new List<PayPalAuthorizationResult>();
        var captures = new List<PayPalCaptureResult>();

        foreach (var unit in order.PurchaseUnits ?? new List<PurchaseUnitResponseDto>())
        {
            foreach (var authorization in unit.Payments?.Authorizations ?? new List<AuthorizationDto>())
            {
                if (authorization.Id is not null)
                {
                    authorizations.Add(MapAuthorization(authorization, requiresPayerAction: false));
                }
            }
            foreach (var capture in unit.Payments?.Captures ?? new List<CaptureDto>())
            {
                if (capture.Id is not null)
                {
                    captures.Add(MapCapture(capture));
                }
            }
        }

        return new PayPalOrderDetails(order.Id ?? string.Empty, order.Status ?? string.Empty, authorizations, captures);
    }

    private static PayPalAuthorizationResult MapAuthorization(AuthorizationDto authorization, bool requiresPayerAction)
    {
        return new PayPalAuthorizationResult(
            authorization.Id ?? string.Empty,
            authorization.Status ?? string.Empty,
            ParseMoney(authorization.Amount?.Value),
            authorization.Amount?.CurrencyCode ?? string.Empty,
            ParseTimestamp(authorization.ExpirationTime),
            requiresPayerAction);
    }

    private static PayPalCaptureResult MapCapture(CaptureDto capture)
    {
        return new PayPalCaptureResult(
            capture.Id ?? string.Empty,
            capture.Status ?? string.Empty,
            ParseMoney(capture.Amount?.Value),
            capture.Amount?.CurrencyCode ?? string.Empty,
            capture.SellerReceivableBreakdown?.PayPalFee is null ? null : ParseMoney(capture.SellerReceivableBreakdown.PayPalFee.Value),
            capture.SellerReceivableBreakdown?.NetAmount is null ? null : ParseMoney(capture.SellerReceivableBreakdown.NetAmount.Value),
            ParseTimestamp(capture.CreateTime));
    }

    private static PayPalAddressDto? MapAddress(PayPalBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }
        return new PayPalAddressDto
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, idempotencyKey, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrEmpty(content))
        {
            return Activator.CreateInstance<TResponse>();
        }
        return JsonSerializer.Deserialize<TResponse>(content, JsonOptions)
            ?? throw new PayPalApiException((int)response.StatusCode, null, null, $"PayPal returned an unreadable response for {method} {path}.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(method, path, response, cancellationToken);
        }

        _logger.LogInformation($"PayPal {method} {path} -> {(int)response.StatusCode}");
        return response;
    }

    private async Task<PayPalApiException> ToExceptionAsync(HttpMethod method, string path, HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(content, JsonOptions);
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through to the generic message.
        }

        var issues = error?.Details?.Where(d => d.Issue is not null).Select(d => d.Issue!).ToList();
        var message = error?.Message ?? $"PayPal {method} {path} failed with status {(int)response.StatusCode}.";
        _logger.LogWarning($"PayPal {method} {path} -> {(int)response.StatusCode} {error?.Name} debug_id={error?.DebugId} body={content}");

        return new PayPalApiException((int)response.StatusCode, error?.Name, error?.DebugId, message, issues);
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

            if (string.IsNullOrEmpty(_settings.ClientId) || string.IsNullOrEmpty(_settings.ClientSecret))
            {
                throw new PayPalApiException(0, null, null,
                    "PayPal credentials are not configured. Set PAYPAL_CLIENT_ID and PAYPAL_CLIENT_SECRET " +
                    "(or the PayPal:ClientId / PayPal:ClientSecret configuration keys).");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await ToExceptionAsync(HttpMethod.Post, "v1/oauth2/token", response, cancellationToken);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, JsonOptions);
            if (token?.AccessToken is null)
            {
                throw new PayPalApiException((int)response.StatusCode, null, null, "PayPal token response had no access_token.");
            }

            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string FormatMoney(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
