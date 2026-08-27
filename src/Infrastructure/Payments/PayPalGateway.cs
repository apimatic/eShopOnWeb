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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Hand-written PayPal REST client built strictly against the OpenAPI specifications in
/// api-specs/paypal (checkout_orders_v2, payments_payment_v2, vault_payment_tokens_v3,
/// transaction_search_v1). OAuth client-credentials token per the specs' security scheme
/// (tokenUrl /v1/oauth2/token). Request/response bodies are never logged, so card
/// details cannot end up in logs.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(e.g. from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables via user-secrets).");
        }
        _httpClient.BaseAddress = new Uri(settings.ResolveBaseUrl());
    }

    public async Task<PayPalAuthorizationInfo> AuthorizePaymentAsync(string orderReference, decimal amount,
        string currency, CardDetails? card, string? vaultTokenId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var orderRequest = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new PayPalPurchaseUnitRequest
                {
                    ReferenceId = orderReference,
                    CustomId = $"eshop-order-{orderReference}",
                    // Deterministic per logical payment: a retried request carries the same
                    // PayPal-Request-Id and must carry an identical payload.
                    InvoiceId = idempotencyKey.Length <= 127 ? idempotencyKey : idempotencyKey.Substring(0, 127),
                    Description = $"eShop order {orderReference}",
                    Amount = Money(amount, currency)
                }
            },
            PaymentSource = new PayPalPaymentSourceRequest
            {
                Card = card != null ? MapCard(card) : new PayPalCardRequest { VaultId = vaultTokenId }
            }
        };

        var order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders",
            orderRequest, $"{idempotencyKey}-create", cancellationToken);

        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return new PayPalAuthorizationInfo { PayPalOrderId = order.Id ?? string.Empty, Status = order.Status! };
        }

        // With a card payment source the create call authorizes immediately; the
        // /authorize call is only needed when the create response holds no authorization yet.
        var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization?.Id == null)
        {
            var authorized = await SendAsync<PayPalOrderResponse>(HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize", new { }, $"{idempotencyKey}-authorize", cancellationToken);

            if (string.Equals(authorized.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                return new PayPalAuthorizationInfo { PayPalOrderId = order.Id ?? string.Empty, Status = authorized.Status! };
            }
            authorization = authorized.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        }

        if (authorization?.Id == null)
        {
            throw new PaymentGatewayException(500, "UNEXPECTED_RESPONSE",
                $"PayPal order {order.Id} was authorized but the response contained no authorization resource.");
        }

        return MapAuthorization(order.Id ?? string.Empty, authorization);
    }

    public async Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorization>(HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return MapAuthorization(string.Empty, authorization);
    }

    public async Task<PayPalCaptureInfo> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var capture = await SendAsync<PayPalCapture>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            new PayPalCaptureRequest { Amount = Money(amount, currency), FinalCapture = true },
            idempotencyKey, cancellationToken);
        return MapCapture(capture);
    }

    public async Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            new PayPalReauthorizeRequest { Amount = Money(amount, currency) },
            idempotencyKey, cancellationToken);
        return MapAuthorization(string.Empty, authorization);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalRefundInfo> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        // Per the spec: an empty body refunds the remaining captured amount in full.
        var body = amount.HasValue
            ? new PayPalRefundRequest { Amount = Money(amount.Value, currency), NoteToPayer = noteToPayer }
            : new PayPalRefundRequest { NoteToPayer = noteToPayer };

        var refund = await SendAsync<PayPalRefund>(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, cancellationToken);

        return new PayPalRefundInfo
        {
            RefundId = refund.Id ?? string.Empty,
            Status = refund.Status ?? string.Empty,
            Amount = ParseAmount(refund.Amount),
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCardInfo> VaultCardAsync(CardDetails card, string customerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalPaymentTokenRequest
        {
            Customer = new PayPalVaultCustomer { Id = customerId },
            PaymentSource = new PayPalPaymentSourceRequest { Card = MapCard(card) }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post,
            "/v3/vault/payment-tokens", request, idempotencyKey, cancellationToken);

        return new PayPalVaultedCardInfo
        {
            VaultTokenId = token.Id ?? string.Empty,
            Brand = token.PaymentSource?.Card?.Brand,
            LastDigits = token.PaymentSource?.Card?.LastDigits,
            Expiry = token.PaymentSource?.Card?.Expiry,
            CardholderName = token.PaymentSource?.Card?.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransactionInfo>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransactionInfo>();

        // The spec limits a query to a 31-day range; chunk wider ranges and follow
        // pagination within each chunk so the whole range is covered.
        var chunkStart = from;
        while (chunkStart <= to)
        {
            var chunkEnd = chunkStart.AddDays(31) < to ? chunkStart.AddDays(31) : to;
            await SearchTransactionChunkAsync(chunkStart, chunkEnd, results, cancellationToken);
            chunkStart = chunkEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task SearchTransactionChunkAsync(DateTimeOffset from, DateTimeOffset to,
        List<PayPalTransactionInfo> results, CancellationToken cancellationToken)
    {
        const int maxPages = 100;
        for (var page = 1; page <= maxPages; page++)
        {
            var path = "/v1/reporting/transactions"
                + $"?start_date={Uri.EscapeDataString(FormatInstant(from))}"
                + $"&end_date={Uri.EscapeDataString(FormatInstant(to))}"
                + "&fields=transaction_info"
                + "&page_size=100"
                + $"&page={page}";

            var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, path, null, null, cancellationToken);

            foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<PayPalTransactionDetail>())
            {
                var info = detail.TransactionInfo;
                if (info == null) continue;
                results.Add(new PayPalTransactionInfo
                {
                    TransactionId = info.TransactionId,
                    EventCode = info.TransactionEventCode,
                    Status = info.TransactionStatus,
                    Amount = info.TransactionAmount == null ? null : ParseAmount(info.TransactionAmount),
                    Currency = info.TransactionAmount?.CurrencyCode,
                    FeeAmount = info.FeeAmount == null ? null : ParseAmount(info.FeeAmount),
                    CustomId = info.CustomId,
                    InvoiceId = info.InvoiceId,
                    ReferenceId = info.PaypalReferenceId,
                    ReferenceIdType = info.PaypalReferenceIdType,
                    InitiationDate = info.TransactionInitiationDate,
                    UpdatedDate = info.TransactionUpdatedDate
                });
            }

            if (page >= response.TotalPages) break;
        }
    }

    private static string FormatInstant(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static PayPalCardRequest MapCard(CardDetails card)
        => new PayPalCardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = new PayPalAddress
            {
                AddressLine1 = card.BillingAddressLine1,
                AddressLine2 = card.BillingAddressLine2,
                AdminArea2 = card.BillingCity,
                AdminArea1 = card.BillingState,
                PostalCode = card.BillingPostalCode,
                CountryCode = card.BillingCountryCode
            }
        };

    private static PayPalMoney Money(decimal amount, string currency)
        => new PayPalMoney
        {
            CurrencyCode = currency,
            Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
        };

    private static decimal ParseAmount(PayPalMoney? money)
        => money?.Value != null
            ? decimal.Parse(money.Value, CultureInfo.InvariantCulture)
            : 0m;

    private static PayPalAuthorizationInfo MapAuthorization(string payPalOrderId, PayPalAuthorization authorization)
        => new PayPalAuthorizationInfo
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization.Id ?? string.Empty,
            Status = authorization.Status ?? string.Empty,
            Amount = ParseAmount(authorization.Amount),
            Currency = authorization.Amount?.CurrencyCode ?? string.Empty,
            ExpirationTime = authorization.ExpirationTime
        };

    private static PayPalCaptureInfo MapCapture(PayPalCapture capture)
        => new PayPalCaptureInfo
        {
            CaptureId = capture.Id ?? string.Empty,
            Status = capture.Status ?? string.Empty,
            Amount = ParseAmount(capture.Amount),
            Currency = capture.Amount?.CurrencyCode ?? string.Empty,
            PayPalFee = capture.SellerReceivableBreakdown?.PaypalFee == null
                ? null : ParseAmount(capture.SellerReceivableBreakdown.PaypalFee),
            NetAmount = capture.SellerReceivableBreakdown?.NetAmount == null
                ? null : ParseAmount(capture.SellerReceivableBreakdown.NetAmount)
        };

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken, bool isRetry = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8, "application/json");
        }

        // Method + path + status only; bodies may carry card details and are never logged.
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        _logger.LogInformation("PayPal {Method} {Path} -> {StatusCode}", method.Method, path.Split('?')[0], (int)response.StatusCode);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !isRetry)
        {
            _accessToken = null;
            return await SendAsync<T>(method, path, body, requestId, cancellationToken, isRetry: true);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToGatewayException((int)response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content) || typeof(T) == typeof(object))
        {
            return default!;
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new PaymentGatewayException(500, "UNEXPECTED_RESPONSE", "PayPal returned an empty response body.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials",
                Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToGatewayException((int)response.StatusCode, content);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, JsonOptions);
            if (token?.AccessToken == null)
            {
                throw new PaymentGatewayException(500, "UNEXPECTED_RESPONSE",
                    "PayPal did not return an access token.");
            }

            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PaymentGatewayException ToGatewayException(int statusCode, string content)
    {
        try
        {
            var error = JsonSerializer.Deserialize<PayPalError>(content, JsonOptions);
            if (error != null && (error.Name != null || error.Message != null))
            {
                var issues = error.Details?
                    .Select(d => d.Description != null ? $"{d.Issue}: {d.Description}" : d.Issue ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                return new PaymentGatewayException(statusCode, error.Name,
                    error.Message ?? $"PayPal error {error.Name}", issues);
            }
        }
        catch (JsonException)
        {
            // fall through to generic error below
        }
        return new PaymentGatewayException(statusCode, null, $"PayPal request failed with status {statusCode}.");
    }
}
