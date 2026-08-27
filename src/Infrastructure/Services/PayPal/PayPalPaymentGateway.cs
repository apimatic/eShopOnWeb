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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal REST implementation of the payment gateway. Covers OAuth2 client-credentials
/// tokens, Orders v2 authorize, Payments v2 capture/reauthorize/void/refund, Vault v3
/// payment tokens, and Transaction Search v1.
///
/// Card details flow through this client to PayPal only; they are never persisted and
/// never written to logs (only PayPal request ids, resource ids and debug ids are logged).
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const int TransactionSearchMaxWindowDays = 31;
    private const int TransactionSearchPageSize = 100;

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalPaymentGateway(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<GatewayAuthorization> AuthorizeAsync(string invoiceId, string? customId, decimal amount, string currency,
        CardDetails? card, string? vaultPaymentTokenId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (card is null && vaultPaymentTokenId is null)
        {
            throw new ArgumentException("Either card details or a vault payment token id is required.");
        }

        var createRequest = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "default",
                    InvoiceId = invoiceId,
                    CustomId = customId,
                    Amount = Money(amount, currency)
                }
            }
        };

        var order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders", createRequest,
            requestId: $"{idempotencyKey}-order", cancellationToken: cancellationToken);

        if (order.Id is null)
        {
            throw new PaymentGatewayException(500, null, "PayPal create-order response did not contain an order id.", null);
        }

        var authorizeRequest = new PayPalAuthorizeOrderRequest
        {
            PaymentSource = new PayPalPaymentSourceRequest
            {
                Card = card is not null ? MapCard(card) : new PayPalCardRequest { VaultId = vaultPaymentTokenId }
            }
        };

        var authorized = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, $"/v2/checkout/orders/{order.Id}/authorize",
            authorizeRequest, requestId: $"{idempotencyKey}-authorize", cancellationToken: cancellationToken);

        if (authorized.Links?.Any(l => l.Rel == "payer-action") == true || authorized.Status == "PAYER_ACTION_REQUIRED")
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this payment in a browser (e.g. 3D Secure); " +
                "this integration does not support an approval round-trip.");
        }

        var authorization = authorized.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(500, null,
                $"PayPal authorize response for order {order.Id} (status {authorized.Status}) did not contain an authorization.", null);
        }

        return new GatewayAuthorization(
            order.Id,
            authorization.Id,
            authorization.Status ?? "UNKNOWN",
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? currency,
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<GatewayAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalAuthorizationResponse>(HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}", body: null, requestId: null, cancellationToken: cancellationToken);
        return MapAuthorizationInfo(response);
    }

    public async Task<GatewayAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalAuthorizationResponse>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            new PayPalReauthorizeRequest { Amount = Money(amount, currency) },
            requestId: idempotencyKey, cancellationToken: cancellationToken);
        return MapAuthorizationInfo(response);
    }

    public async Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currency, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalCaptureResponse>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            new PayPalCaptureRequest
            {
                Amount = Money(amount, currency),
                InvoiceId = invoiceId,
                FinalCapture = true
            },
            requestId: idempotencyKey, cancellationToken: cancellationToken);

        if (response.Id is null)
        {
            throw new PaymentGatewayException(500, null, "PayPal capture response did not contain a capture id.", null);
        }

        return new GatewayCapture(
            response.Id,
            response.Status ?? "UNKNOWN",
            ParseMoney(response.SellerReceivableBreakdown?.GrossAmount ?? response.Amount),
            response.Amount?.CurrencyCode ?? currency,
            ParseNullableMoney(response.SellerReceivableBreakdown?.PaypalFee),
            ParseNullableMoney(response.SellerReceivableBreakdown?.NetAmount));
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorizationResponse>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", body: null,
            requestId: idempotencyKey, cancellationToken: cancellationToken);
    }

    public async Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currency, string? customId,
        string idempotencyKey, string? note, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalRefundResponse>(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            new PayPalRefundRequest
            {
                Amount = amount.HasValue ? Money(amount.Value, currency) : null,
                CustomId = customId,
                NoteToPayer = note
            },
            requestId: idempotencyKey, cancellationToken: cancellationToken);

        if (response.Id is null)
        {
            throw new PaymentGatewayException(500, null, "PayPal refund response did not contain a refund id.", null);
        }

        return new GatewayRefund(response.Id, response.Status ?? "UNKNOWN",
            ParseMoney(response.Amount), response.Amount?.CurrencyCode ?? currency);
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalCreatePaymentTokenRequest
        {
            PaymentSource = new PayPalPaymentSourceRequest { Card = MapCard(card) },
            Customer = new PayPalCustomerRequest { Id = customerId }
        };

        var response = await SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens",
            request, requestId: idempotencyKey, cancellationToken: cancellationToken);

        if (response.Id is null)
        {
            throw new PaymentGatewayException(500, null, "PayPal vault response did not contain a payment token id.", null);
        }

        return new GatewayVaultedCard(
            response.Id,
            response.Customer?.Id,
            response.PaymentSource?.Card?.Brand,
            response.PaymentSource?.Card?.LastDigits,
            response.PaymentSource?.Card?.Expiry,
            response.PaymentSource?.Card?.Name);
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}", body: null,
            requestId: null, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();

        // The transaction search API supports a maximum range of 31 days per call.
        for (var windowStart = from; windowStart < to;)
        {
            var windowEnd = windowStart.AddDays(TransactionSearchMaxWindowDays) < to
                ? windowStart.AddDays(TransactionSearchMaxWindowDays)
                : to;

            var page = 1;
            while (true)
            {
                var query = $"/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatDate(windowEnd))}" +
                    $"&fields=all&balance_affecting_records_only=N" +
                    $"&total_required=true&page_size={TransactionSearchPageSize}&page={page}";

                var response = await SendAsync<PayPalTransactionListResponse>(HttpMethod.Get, query, body: null,
                    requestId: null, cancellationToken: cancellationToken);

                var details = response.TransactionDetails ?? new List<PayPalTransactionDetail>();
                results.AddRange(details.Select(MapTransaction));

                var totalPages = response.TotalPages ?? 1;
                if (page >= totalPages || details.Count == 0)
                {
                    break;
                }
                page++;
            }

            windowStart = windowEnd;
        }

        return results;
    }

    private static GatewayTransaction MapTransaction(PayPalTransactionDetail detail)
    {
        var info = detail.TransactionInfo ?? new PayPalTransactionInfo();
        return new GatewayTransaction(
            info.TransactionId ?? string.Empty,
            info.PaypalReferenceId,
            info.TransactionEventCode,
            info.TransactionStatus ?? string.Empty,
            ParseMoney(info.TransactionAmount),
            info.TransactionAmount?.CurrencyCode ?? string.Empty,
            ParseNullableMoney(info.FeeAmount),
            ParseDate(info.TransactionInitiationDate),
            info.InvoiceId,
            info.CustomField);
    }

    private static GatewayAuthorizationInfo MapAuthorizationInfo(PayPalAuthorizationResponse response)
    {
        if (response.Id is null)
        {
            throw new PaymentGatewayException(500, null, "PayPal authorization response did not contain an id.", null);
        }
        return new GatewayAuthorizationInfo(
            response.Id,
            response.Status ?? "UNKNOWN",
            ParseMoney(response.Amount),
            response.Amount?.CurrencyCode ?? string.Empty,
            ParseDate(response.ExpirationTime));
    }

    private static PayPalCardRequest MapCard(CardDetails card)
    {
        return new PayPalCardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = card.BillingAddress is null ? null : new PayPalAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea2 = card.BillingAddress.City,
                AdminArea1 = card.BillingAddress.State,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
        };
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, body, requestId, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions)!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        var response = await SendCoreAsync(method, path, body, requestId, cancellationToken);

        // Retry once with a fresh token if PayPal rejected the cached one.
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _accessToken = null;
            response.Dispose();
            response = await SendCoreAsync(method, path, body, requestId, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            throw ToGatewayException((int)response.StatusCode, content);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, $"{_settings.ResolveBaseUrl()}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (requestId is not null)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        _logger.LogDebug("PayPal {Method} {Path} (request id: {RequestId})", method, path, requestId);
        return await _httpClient.SendAsync(request, cancellationToken);
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
                throw new InvalidOperationException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret via user-secrets or environment configuration.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ResolveBaseUrl()}/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToGatewayException((int)response.StatusCode, content);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, JsonOptions);
            if (token?.AccessToken is null)
            {
                throw new PaymentGatewayException(500, null, "PayPal token response did not contain an access token.", null);
            }

            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn).Subtract(TimeSpan.FromMinutes(1));
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
            var error = JsonSerializer.Deserialize<PayPalErrorResponse>(content, JsonOptions);
            if (error is not null)
            {
                var issue = error.Details?.FirstOrDefault();
                var message = error.Message ?? "PayPal request failed.";
                if (issue?.Description is not null)
                {
                    message = $"{message} ({issue.Issue}: {issue.Description})";
                }
                return new PaymentGatewayException(statusCode, error.Name, message, error.DebugId);
            }
        }
        catch (JsonException)
        {
            // fall through to generic error below
        }
        return new PaymentGatewayException(statusCode, null, $"PayPal request failed with status {statusCode}.", null);
    }

    private static PayPalMoney Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal ParseMoney(PayPalMoney? money)
        => money?.Value is null ? 0m : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static decimal? ParseNullableMoney(PayPalMoney? money)
        => money?.Value is null ? null : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value)
        => value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
