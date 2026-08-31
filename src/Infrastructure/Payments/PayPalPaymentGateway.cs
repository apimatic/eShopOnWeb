using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Hand-written PayPal client built against the OpenAPI specifications in api-specs/paypal:
/// checkout_orders_v2 (create/authorize order), payments_payment_v2 (capture, reauthorize,
/// void, refund, get authorization), vault_payment_tokens_v3 (create/delete payment tokens)
/// and transaction_search_v1 (list transactions).
/// Request bodies containing card details are never logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly PaymentGatewayOptions _options;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(HttpClient httpClient, PaymentGatewayOptions options, ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _tokenProvider = new PayPalAccessTokenProvider(httpClient, options);
    }

    public async Task<GatewayAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string referenceId,
        GatewayCardDetails? card,
        string? vaultTokenId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = referenceId,
                    CustomId = referenceId,
                    Amount = Money(amount, currency)
                }
            },
            PaymentSource = new PayPalPaymentSourceRequest
            {
                Card = BuildCardRequest(card, vaultTokenId)
            }
        };

        // POST /v2/checkout/orders (checkout_orders_v2, CreateOrder)
        var order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders", request, idempotencyKey, cancellationToken);
        ThrowIfPayerActionRequired(order);

        var authorization = FindAuthorization(order);
        if (authorization == null)
        {
            // POST /v2/checkout/orders/{id}/authorize (checkout_orders_v2, AuthorizeOrder)
            order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, $"/v2/checkout/orders/{order!.Id}/authorize", new { }, idempotencyKey, cancellationToken);
            ThrowIfPayerActionRequired(order);
            authorization = FindAuthorization(order);
        }

        if (authorization?.Id == null)
        {
            throw new PaymentGatewayException($"PayPal order {order?.Id} returned no authorization (order status: {order?.Status}).");
        }
        if (authorization.Status == "DENIED")
        {
            throw new PaymentDeclinedException($"PayPal declined the payment for {referenceId}.");
        }

        var authorizedAmount = ParseMoney(authorization.Amount);
        if (authorizedAmount != amount)
        {
            throw new PaymentGatewayException(
                $"PayPal authorized {authorizedAmount} but the order total is {amount} {currency}; refusing to proceed with a mismatched hold.");
        }

        return new GatewayAuthorizationResult(
            order!.Id!,
            authorization.Id,
            authorization.Status ?? "CREATED",
            authorizedAmount,
            authorization.Amount?.CurrencyCode ?? currency,
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        // GET /v2/payments/authorizations/{authorization_id} (payments_payment_v2, GetAuthorizedPayment)
        var authorization = await SendAsync<PayPalAuthorization>(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return new GatewayAuthorizationStatus(
            authorization.Id ?? authorizationId,
            authorization.Status ?? "UNKNOWN",
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? _options.Currency,
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<GatewayCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // POST /v2/payments/authorizations/{authorization_id}/capture (payments_payment_v2, CaptureAuthorizedPayment)
        var request = new PayPalCaptureRequest
        {
            Amount = Money(amount, currency),
            FinalCapture = true
        };
        var capture = await SendAsync<PayPalCapture>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", request, idempotencyKey, cancellationToken);

        return new GatewayCaptureResult(
            capture.Id ?? string.Empty,
            capture.Status ?? "UNKNOWN",
            ParseMoney(capture.SellerReceivableBreakdown?.GrossAmount ?? capture.Amount),
            capture.SellerReceivableBreakdown?.PayPalFee == null ? null : ParseMoney(capture.SellerReceivableBreakdown.PayPalFee),
            capture.SellerReceivableBreakdown?.NetAmount == null ? null : ParseMoney(capture.SellerReceivableBreakdown.NetAmount),
            capture.Amount?.CurrencyCode ?? currency);
    }

    public async Task<GatewayAuthorizationStatus> ReauthorizeAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // POST /v2/payments/authorizations/{authorization_id}/reauthorize (payments_payment_v2, ReauthorizePayment)
        var request = new PayPalReauthorizeRequest { Amount = Money(amount, currency) };
        var authorization = await SendAsync<PayPalAuthorization>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", request, idempotencyKey, cancellationToken);

        return new GatewayAuthorizationStatus(
            authorization.Id ?? authorizationId,
            authorization.Status ?? "UNKNOWN",
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? currency,
            ParseDate(authorization.ExpirationTime));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // POST /v2/payments/authorizations/{authorization_id}/void (payments_payment_v2, VoidPayment)
        await SendAsync<PayPalAuthorization>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", new { }, idempotencyKey, cancellationToken);
    }

    public async Task<GatewayRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        string? customId,
        string? noteToPayer,
        CancellationToken cancellationToken = default)
    {
        // POST /v2/payments/captures/{capture_id}/refund (payments_payment_v2, RefundCapturedPayment)
        var request = new PayPalRefundRequest
        {
            Amount = amount.HasValue ? Money(amount.Value, currency) : null,
            CustomId = customId,
            NoteToPayer = noteToPayer
        };
        var refund = await SendAsync<PayPalRefund>(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", request, idempotencyKey, cancellationToken);

        return new GatewayRefundResult(
            refund.Id ?? string.Empty,
            refund.Status ?? "UNKNOWN",
            ParseMoney(refund.Amount),
            refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<GatewayVaultedCard> CreatePaymentTokenAsync(
        string customerId,
        GatewayCardDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // POST /v3/vault/payment-tokens (vault_payment_tokens_v3, CreatePaymentToken)
        var request = new PayPalCreatePaymentTokenRequest
        {
            Customer = new PayPalCustomer { Id = customerId },
            PaymentSource = new PayPalVaultPaymentSource
            {
                Card = new PayPalVaultCardRequest
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", request, idempotencyKey, cancellationToken);
        if (string.IsNullOrEmpty(token.Id))
        {
            throw new PaymentGatewayException("PayPal returned a payment token without an id.");
        }

        return new GatewayVaultedCard(
            token.Id,
            token.PaymentSource?.Card?.Name,
            token.PaymentSource?.Card?.Brand,
            token.PaymentSource?.Card?.LastDigits,
            token.PaymentSource?.Card?.Expiry);
    }

    public async Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        // DELETE /v3/vault/payment-tokens/{id} (vault_payment_tokens_v3, DeletePaymentToken)
        await SendAsync<object>(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // GET /v1/reporting/transactions (transaction_search_v1, SearchTransactions).
        // balance_affecting_records_only=N so authorizations (non-balance records) appear too.
        var results = new List<GatewayTransaction>();
        const int pageSize = 100;
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var query = $"/v1/reporting/transactions?start_date={FormatDate(from)}&end_date={FormatDate(to)}" +
                        $"&fields=all&balance_affecting_records_only=N&page_size={pageSize}&page={page}";
            var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, query, null, null, cancellationToken);

            if (response.TransactionDetails != null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId == null)
                    {
                        continue;
                    }
                    results.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.PayPalReferenceId,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        info.TransactionAmount == null ? null : ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        info.FeeAmount == null ? null : ParseMoney(info.FeeAmount),
                        ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate)));
                }
            }

            totalPages = response.TotalPages <= 0 ? 1 : response.TotalPages;
            if (response.TransactionDetails == null || response.TransactionDetails.Count == 0)
            {
                break;
            }
            page++;
        }

        return results;
    }

    private static PayPalCardRequest BuildCardRequest(GatewayCardDetails? card, string? vaultTokenId)
    {
        if (!string.IsNullOrEmpty(vaultTokenId))
        {
            return new PayPalCardRequest
            {
                VaultId = vaultTokenId,
                StoredCredential = new PayPalStoredCredential()
            };
        }

        if (card == null)
        {
            throw new PaymentGatewayException("No payment source was provided (neither card details nor a vaulted card).");
        }

        return new PayPalCardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = MapAddress(card.BillingAddress)
        };
    }

    private static PayPalAddress? MapAddress(GatewayAddress? address)
    {
        if (address == null)
        {
            return null;
        }
        return new PayPalAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static PayPalAuthorization? FindAuthorization(PayPalOrderResponse? order)
    {
        return order?.PurchaseUnits?
            .Select(u => u.Payments?.Authorizations)
            .FirstOrDefault(a => a is { Count: > 0 })
            ?.First();
    }

    private static void ThrowIfPayerActionRequired(PayPalOrderResponse? order)
    {
        var requiresBrowserApproval =
            string.Equals(order?.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            (order?.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false);
        if (requiresBrowserApproval)
        {
            throw new PaymentGatewayException(
                "PayPal answered the card payment with a challenge that requires the shopper to approve it in a browser " +
                "(payer-action). This integration does not implement an approval round-trip.");
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativeUrl, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var baseUrl = _options.ResolveBaseUrl();
        var token = await _tokenProvider.GetTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, baseUrl + relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            // PayPal-Request-Id: the specs' idempotency header (max 108 chars).
            request.Headers.Add("PayPal-Request-Id", idempotencyKey.Length <= 108 ? idempotencyKey : idempotencyKey[..108]);
        }
        if (body != null)
        {
            // Never log this payload: it may contain full card details.
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal {Method} {Url} failed with {StatusCode}: {Body}", method, relativeUrl, (int)response.StatusCode, responseBody);
            throw ToGatewayException(response.StatusCode, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)!;
    }

    private static PaymentGatewayException ToGatewayException(System.Net.HttpStatusCode statusCode, string body)
    {
        PaymentGatewayException exception;
        try
        {
            var error = JsonSerializer.Deserialize<PayPalError>(body, JsonOptions);
            if (error != null)
            {
                var issues = error.Details == null
                    ? string.Empty
                    : " [" + string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}")) + "]";
                exception = new PaymentGatewayException($"PayPal error {error.Name} (HTTP {(int)statusCode}): {error.Message}{issues}")
                {
                    PayPalDebugId = error.DebugId,
                    ProcessorStatusCode = (int)statusCode
                };
                return exception;
            }
        }
        catch (JsonException)
        {
            // fall through to the generic error below
        }
        return new PaymentGatewayException($"PayPal request failed with HTTP {(int)statusCode}.")
        {
            ProcessorStatusCode = (int)statusCode
        };
    }

    private static PayPalMoney Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal ParseMoney(PayPalMoney? money)
    {
        if (money?.Value == null)
        {
            return 0m;
        }
        return decimal.Parse(money.Value, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
