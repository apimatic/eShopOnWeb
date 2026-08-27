using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Implements the payment gateway over the PayPal REST APIs, translating between
/// the application's neutral payment models and the PayPal spec DTOs.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalClient _client;

    public PayPalPaymentGateway(PayPalClient client)
    {
        _client = client;
    }

    public string Currency => _client.Currency;

    public Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, string customId, string invoiceId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSource { Card = ToCardRequest(card) };
        return AuthorizeAsync(amount, currency, paymentSource, idempotencyKey, customId, invoiceId, cancellationToken);
    }

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultTokenId,
        string idempotencyKey, string customId, string invoiceId, CancellationToken cancellationToken = default)
    {
        // checkout_orders_v2 card_request.vault_id: "The PayPal-generated ID for the saved card payment source."
        var paymentSource = new PayPalPaymentSource { Card = new PayPalCardRequest { VaultId = vaultTokenId } };
        return AuthorizeAsync(amount, currency, paymentSource, idempotencyKey, customId, invoiceId, cancellationToken);
    }

    public async Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await _client.GetAuthorizationAsync(authorizationId, cancellationToken);
        return ToAuthorizationState(authorization);
    }

    public async Task<AuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new PayPalReauthorizeRequest { Amount = Money(amount, currency) };
            var authorization = await _client.ReauthorizeAsync(authorizationId, request, idempotencyKey, cancellationToken);
            return ToAuthorizationState(authorization);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(ex.Message, ex);
        }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalCaptureRequest
        {
            Amount = Money(amount, currency),
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        PayPalCapture capture;
        try
        {
            capture = await _client.CaptureAuthorizationAsync(authorizationId, request, idempotencyKey, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(ex.Message, ex);
        }

        var breakdown = capture.SellerReceivableBreakdown;
        return new CaptureResult(
            capture.Id ?? throw new PaymentException("PayPal returned a capture without an id."),
            capture.Status ?? "UNKNOWN",
            ParseMoney(breakdown?.GrossAmount ?? capture.Amount),
            breakdown?.PayPalFee == null ? null : ParseMoney(breakdown.PayPalFee),
            breakdown?.NetAmount == null ? null : ParseMoney(breakdown.NetAmount),
            (breakdown?.GrossAmount ?? capture.Amount)?.CurrencyCode ?? currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.VoidAuthorizationAsync(authorizationId, idempotencyKey, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(ex.Message, ex);
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        var request = new PayPalRefundRequest
        {
            Amount = amount.HasValue ? Money(amount.Value, currency) : null,
            NoteToPayer = noteToPayer,
            CustomId = idempotencyKey
        };

        PayPalRefund refund;
        try
        {
            refund = await _client.RefundCaptureAsync(captureId, request, idempotencyKey, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(ex.Message, ex);
        }

        return new RefundResult(
            refund.Id ?? throw new PaymentException("PayPal returned a refund without an id."),
            refund.Status ?? "UNKNOWN",
            ParseMoney(refund.Amount),
            refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalPaymentTokenRequest
        {
            Customer = new PayPalPaymentTokenRequest.PayPalCustomer { MerchantCustomerId = customerId },
            PaymentSource = new PayPalPaymentTokenRequest.PayPalTokenPaymentSource { Card = ToCardRequest(card) }
        };

        PayPalPaymentTokenResponse token;
        try
        {
            token = await _client.CreatePaymentTokenAsync(request, idempotencyKey, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(ex.Message, ex);
        }

        var vaultedCard = token.PaymentSource?.Card;
        return new VaultedCardResult(
            token.Id ?? throw new PaymentException("PayPal returned a payment token without an id."),
            vaultedCard?.LastDigits,
            vaultedCard?.Brand,
            vaultedCard?.Expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeletePaymentTokenAsync(vaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone from the vault; deletion is idempotent in effect.
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(ex.Message, ex);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 500;
        var transactions = new List<GatewayTransaction>();

        var page = 1;
        while (true)
        {
            PayPalTransactionSearchResponse response;
            try
            {
                response = await _client.ListTransactionsAsync(from, to, page, pageSize, cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                throw new PaymentException(ex.Message, ex);
            }

            if (response.TransactionDetails != null)
            {
                transactions.AddRange(response.TransactionDetails
                    .Where(d => d.TransactionInfo?.TransactionId != null)
                    .Select(d => ToGatewayTransaction(d.TransactionInfo!)));
            }

            // Cover the whole range, not just the first page.
            if (response.TotalPages <= page || response.TransactionDetails == null || response.TransactionDetails.Count == 0)
            {
                break;
            }
            page++;
        }

        return transactions;
    }

    private async Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currency,
        PayPalPaymentSource paymentSource, string idempotencyKey, string customId, string invoiceId,
        CancellationToken cancellationToken)
    {
        var orderRequest = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "default",
                    Amount = Money(amount, currency),
                    CustomId = customId,
                    InvoiceId = invoiceId
                }
            },
            PaymentSource = paymentSource
        };

        PayPalOrderResponse order;
        try
        {
            order = await _client.CreateOrderAsync(orderRequest, idempotencyKey, cancellationToken);
            order = await _client.AuthorizeOrderAsync(
                order.Id ?? throw new PaymentException("PayPal returned an order without an id."),
                idempotencyKey, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(ex.Message, ex);
        }

        if (order.Status == "PAYER_ACTION_REQUIRED")
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this payment interactively (e.g. 3-D Secure). " +
                "This API integration does not support an approval round-trip.");
        }

        var authorization = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorization>())
            .FirstOrDefault();

        if (authorization?.Id == null)
        {
            throw new PaymentException(
                $"PayPal authorized order {order.Id} (status {order.Status}) but returned no authorization. " +
                "The payment was NOT taken; retry the payment or contact support.");
        }

        if (authorization.Status == "DENIED")
        {
            throw new PaymentException($"PayPal denied the card authorization for this payment (authorization {authorization.Id}).");
        }
        if (authorization.Status == "PENDING")
        {
            throw new PayerActionRequiredException(
                $"PayPal left the authorization {authorization.Id} in PENDING status, which this integration cannot complete automatically.");
        }

        return new AuthorizationResult(
            order.Id!,
            authorization.Id,
            authorization.Status ?? "UNKNOWN",
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? currency,
            ParseDateTime(authorization.ExpirationTime));
    }

    private static PayPalCardRequest ToCardRequest(CardDetails card)
    {
        return new PayPalCardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.Address == null ? null : new PayPalAddress
            {
                AddressLine1 = card.Address.AddressLine1,
                AddressLine2 = card.Address.AddressLine2,
                AdminArea2 = card.Address.AdminArea2,
                AdminArea1 = card.Address.AdminArea1,
                PostalCode = card.Address.PostalCode,
                CountryCode = card.Address.CountryCode
            }
        };
    }

    private static AuthorizationState ToAuthorizationState(PayPalAuthorization authorization)
    {
        return new AuthorizationState(
            authorization.Id ?? throw new PaymentException("PayPal returned an authorization without an id."),
            authorization.Status ?? "UNKNOWN",
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? "",
            ParseDateTime(authorization.ExpirationTime));
    }

    private static GatewayTransaction ToGatewayTransaction(PayPalTransactionInfo info)
    {
        return new GatewayTransaction(
            info.TransactionId!,
            info.TransactionEventCode,
            info.TransactionStatus,
            info.TransactionAmount == null ? null : ParseMoney(info.TransactionAmount),
            info.TransactionAmount?.CurrencyCode,
            ParseDateTime(info.TransactionInitiationDate),
            info.PayPalReferenceId,
            info.PayPalReferenceIdType,
            info.InvoiceId,
            info.CustomField);
    }

    private static PayPalMoney Money(decimal amount, string currency)
    {
        return new PayPalMoney
        {
            CurrencyCode = currency,
            Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
        };
    }

    private static decimal ParseMoney(PayPalMoney? money)
    {
        if (money?.Value == null)
        {
            return 0m;
        }
        return decimal.Parse(money.Value, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseDateTime(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
