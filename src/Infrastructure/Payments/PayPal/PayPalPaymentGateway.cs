using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// Implements the payment gateway abstraction against PayPal's REST APIs, exactly as
/// described by the OpenAPI specifications in api-specs/paypal.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalClient _client;

    public PayPalPaymentGateway(PayPalClient client)
    {
        _client = client;
    }

    public async Task<GatewayAuthorization> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = BuildOrderRequest(amount, currency, new PayPalPaymentSourceRequest { Card = ToPayPalCard(card) });
        return await CreateAndAuthorizeOrderAsync(request, idempotencyKey, cancellationToken: cancellationToken);
    }

    public async Task<GatewayAuthorization> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultTokenId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = BuildOrderRequest(amount, currency, new PayPalPaymentSourceRequest
        {
            Card = new PayPalCardRequest { VaultId = vaultTokenId }
        });
        return await CreateAndAuthorizeOrderAsync(request, idempotencyKey, cancellationToken: cancellationToken);
    }

    public async Task<GatewayAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await GetAuthorizationRawAsync(authorizationId, cancellationToken);
        return ToAuthorizationDetails(authorization);
    }

    private async Task<PayPalAuthorization?> GetAuthorizationRawAsync(string authorizationId, CancellationToken cancellationToken)
    {
        return await _client.SendAsync<PayPalAuthorization>(
            HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", cancellationToken: cancellationToken);
    }

    public async Task<GatewayAuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var authorization = await _client.SendAsync<PayPalAuthorization>(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            new PayPalReauthorizeRequest { Amount = Money(amount, currency) },
            idempotencyKey, preferRepresentation: true, cancellationToken: cancellationToken);
        if (authorization is not null && authorization.Amount is null)
        {
            authorization = await GetAuthorizationRawAsync(authorization.Id, cancellationToken) ?? authorization;
        }
        return ToAuthorizationDetails(authorization);
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var capture = await _client.SendAsync<PayPalCapture>(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            new PayPalCaptureRequest { Amount = Money(amount, currency), FinalCapture = true },
            idempotencyKey, preferRepresentation: true, cancellationToken: cancellationToken);

        if (capture is null)
        {
            throw new PaymentGatewayException("PayPal returned an empty capture response.");
        }
        if (capture.Amount is null || capture.SellerReceivableBreakdown is null)
        {
            // Defensive: fetch the full resource if the response came back minimal.
            capture = await _client.SendAsync<PayPalCapture>(
                HttpMethod.Get, $"/v2/payments/captures/{capture.Id}", cancellationToken: cancellationToken) ?? capture;
        }
        if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException($"PayPal capture {capture.Id} did not complete (status {capture.Status}).", capture.Status);
        }

        return new GatewayCapture(
            capture.Id,
            capture.Status ?? "UNKNOWN",
            ParseMoney(capture.Amount),
            capture.SellerReceivableBreakdown?.PaypalFee is null ? null : ParseMoney(capture.SellerReceivableBreakdown.PaypalFee),
            capture.SellerReceivableBreakdown?.NetAmount is null ? null : ParseMoney(capture.SellerReceivableBreakdown.NetAmount),
            capture.Amount?.CurrencyCode ?? currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await _client.SendAsync<PayPalAuthorization>(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            new { }, idempotencyKey, cancellationToken: cancellationToken);
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, string? note, CancellationToken cancellationToken = default)
    {
        var refund = await _client.SendAsync<PayPalRefund>(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            new PayPalRefundRequest
            {
                Amount = amount.HasValue ? Money(amount.Value, currency) : null,
                CustomId = idempotencyKey,
                NoteToPayer = note
            },
            idempotencyKey, preferRepresentation: true, cancellationToken: cancellationToken);

        if (refund is null)
        {
            throw new PaymentGatewayException("PayPal returned an empty refund response.");
        }
        if (refund.Amount is null)
        {
            refund = await _client.SendAsync<PayPalRefund>(
                HttpMethod.Get, $"/v2/payments/refunds/{refund.Id}", cancellationToken: cancellationToken) ?? refund;
        }

        return new GatewayRefund(
            refund.Id,
            refund.Status ?? "UNKNOWN",
            refund.Amount is null ? amount ?? 0m : ParseMoney(refund.Amount),
            refund.Amount?.CurrencyCode ?? currency,
            refund.SellerPayableBreakdown?.TotalRefundedAmount is null ? null : ParseMoney(refund.SellerPayableBreakdown.TotalRefundedAmount));
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var response = await _client.SendAsync<PayPalVaultTokenResponse>(
            HttpMethod.Post, "/v3/vault/payment-tokens",
            new PayPalVaultTokenRequest
            {
                PaymentSource = new PayPalVaultPaymentSourceRequest { Card = ToPayPalCard(card) }
            },
            idempotencyKey, cancellationToken: cancellationToken);

        if (response is null || string.IsNullOrEmpty(response.Id))
        {
            throw new PaymentGatewayException("PayPal did not return a vault token for the card.");
        }

        var vaulted = response.PaymentSource?.Card;
        return new GatewayVaultedCard(response.Id, vaulted?.Brand, vaulted?.LastDigits, vaulted?.Expiry, vaulted?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await _client.SendAsync<object>(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}", cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        const int pageSize = 500; // maximum allowed by the transaction_search_v1 spec
        var results = new List<GatewayTransaction>();
        var page = 1;
        while (true)
        {
            var start = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var end = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var path = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=transaction_info&page_size={pageSize}&page={page}";

            var response = await _client.SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, path, cancellationToken: cancellationToken);
            if (response?.TransactionDetails is { Count: > 0 })
            {
                results.AddRange(response.TransactionDetails.Select(ToGatewayTransaction));
            }

            if (response is null || page >= response.TotalPages || response.TransactionDetails is null or { Count: 0 })
            {
                break;
            }
            page++;
        }
        return results;
    }

    private async Task<GatewayAuthorization> CreateAndAuthorizeOrderAsync(PayPalOrderRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        // Distinct keys per call: PayPal scopes PayPal-Request-Id per endpoint.
        var order = await _client.SendAsync<PayPalOrderResponse>(
            HttpMethod.Post, "/v2/checkout/orders", request, idempotencyKey + "-order", cancellationToken: cancellationToken);
        if (order is null || string.IsNullOrEmpty(order.Id))
        {
            throw new PaymentGatewayException("PayPal did not return an order id.");
        }

        // With a direct card payment source PayPal may authorize the order as part of
        // creation; only call the authorize endpoint when the create response did not
        // already carry an authorization.
        var authorization = ExtractAuthorization(order);
        if (authorization is null)
        {
            ThrowIfPayerActionRequired(order);
            var authorized = await _client.SendAsync<PayPalOrderResponse>(
                HttpMethod.Post, $"/v2/checkout/orders/{order.Id}/authorize", new { }, idempotencyKey + "-auth", cancellationToken: cancellationToken);
            ThrowIfPayerActionRequired(authorized);
            authorization = ExtractAuthorization(authorized);
        }

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentGatewayException($"PayPal order {order.Id} did not return an authorization.");
        }
        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(authorization.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException(
                $"PayPal authorization {authorization.Id} was not created (status {authorization.Status}).", authorization.Status);
        }

        return new GatewayAuthorization(
            order.Id,
            authorization.Id,
            authorization.Status!,
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? request.PurchaseUnits[0].Amount.CurrencyCode,
            authorization.ExpirationTime);
    }

    private static PayPalAuthorization? ExtractAuthorization(PayPalOrderResponse? order)
    {
        return order?.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorization>())
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));
    }

    private static void ThrowIfPayerActionRequired(PayPalOrderResponse? order)
    {
        var requiresPayerAction = string.Equals(order?.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || (order?.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false);
        if (requiresPayerAction)
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this payment in a browser (e.g. a 3D Secure challenge), which this integration does not support.");
        }
    }

    private static PayPalOrderRequest BuildOrderRequest(decimal amount, string currency, PayPalPaymentSourceRequest paymentSource)
    {
        return new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new() { Amount = Money(amount, currency) }
            },
            PaymentSource = paymentSource
        };
    }

    private static PayPalCardRequest ToPayPalCard(CardDetails card)
    {
        return new PayPalCardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.HolderName,
            BillingAddress = card.BillingAddress is null ? null : new PayPalAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea2 = card.BillingAddress.AdminArea2,
                AdminArea1 = card.BillingAddress.AdminArea1,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
        };
    }

    private static GatewayAuthorizationDetails ToAuthorizationDetails(PayPalAuthorization? authorization)
    {
        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentGatewayException("PayPal did not return authorization details.");
        }
        return new GatewayAuthorizationDetails(
            authorization.Id,
            authorization.Status ?? "UNKNOWN",
            authorization.Amount is null ? 0m : ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? string.Empty,
            authorization.ExpirationTime);
    }

    private static GatewayTransaction ToGatewayTransaction(PayPalTransactionDetail detail)
    {
        var info = detail.TransactionInfo;
        return new GatewayTransaction(
            info?.TransactionId ?? string.Empty,
            info?.TransactionEventCode,
            info?.TransactionStatus,
            info?.TransactionAmount is null ? null : ParseMoney(info.TransactionAmount),
            info?.TransactionAmount?.CurrencyCode,
            info?.FeeAmount is null ? null : ParseMoney(info.FeeAmount),
            info?.InvoiceId,
            info?.CustomField,
            info?.TransactionInitiationDate,
            info?.TransactionUpdatedDate);
    }

    private static PayPalMoney Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static decimal ParseMoney(PayPalMoney? money) =>
        money is null ? 0m : decimal.Parse(money.Value, CultureInfo.InvariantCulture);
}
