using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal.Dto;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// Implements IPaymentProcessor against the PayPal API contract from api-specs/paypal.
/// PayPal's two-step authorize (create order with intent=AUTHORIZE, then authorize it)
/// is collapsed into a single AuthorizeAsync. Translates PayPal errors into
/// PaymentProcessorException without ever exposing card data.
/// </summary>
internal sealed class PayPalPaymentProcessor : IPaymentProcessor
{
    private readonly PayPalClient _client;

    public PayPalPaymentProcessor(PayPalClient client)
    {
        _client = client;
    }

    public async Task<ProcessorAuthorization> AuthorizeAsync(decimal amount, string currency, PaymentSourceSelection source,
        string merchantReference, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var createRequest = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            {
                new PayPalPurchaseUnitRequest
                {
                    ReferenceId = merchantReference,
                    CustomId = merchantReference,
                    InvoiceId = invoiceId,
                    Description = $"eShopOnWeb {merchantReference}",
                    Amount = Money(amount, currency)
                }
            },
            PaymentSource = BuildPaymentSource(source)
        };

        PayPalOrderResponse order;
        try
        {
            order = await _client.CreateOrderAsync(createRequest, $"{idempotencyKey}-order", cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw ToProcessorException(ex, "create the PayPal order");
        }

        // With a card payment source and intent=AUTHORIZE, PayPal authorizes at creation;
        // the authorization is then already on the order. Only call the authorize endpoint
        // when the create response carries no authorization yet.
        var authorization = ExtractAuthorization(order);
        if (authorization is null)
        {
            PayPalOrderResponse authorized;
            try
            {
                authorized = await _client.AuthorizeOrderAsync(order.Id!, $"{idempotencyKey}-auth", cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                throw ToProcessorException(ex, "authorize the payment");
            }

            authorization = ExtractAuthorization(authorized);
            if (authorization?.Id is null)
            {
                throw new PaymentProcessorException(
                    $"PayPal order {order.Id} was authorized but returned no authorization id (order status {authorized.Status}).");
            }
            order = authorized;
        }

        var card = order.PaymentSource?.Card;
        return new ProcessorAuthorization(
            order.Id!,
            authorization.Id,
            authorization.Status ?? "UNKNOWN",
            ParseMoney(authorization.Amount) ?? amount,
            authorization.Amount?.CurrencyCode ?? currency,
            authorization.ExpirationTime,
            card?.Brand,
            card?.LastDigits);
    }

    public async Task<ProcessorAuthorizationState> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var auth = await _client.GetAuthorizationAsync(authorizationId, cancellationToken);
            return new ProcessorAuthorizationState(auth.Id!, auth.Status ?? "UNKNOWN",
                ParseMoney(auth.Amount) ?? 0m, auth.Amount?.CurrencyCode ?? string.Empty, auth.ExpirationTime);
        }
        catch (PayPalApiException ex)
        {
            throw ToProcessorException(ex, "read the authorization");
        }
    }

    public async Task<ProcessorAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var auth = await _client.ReauthorizeAsync(authorizationId,
                new PayPalReauthorizeRequest { Amount = Money(amount, currency) }, idempotencyKey, cancellationToken);
            return new ProcessorAuthorizationState(auth.Id!, auth.Status ?? "UNKNOWN",
                ParseMoney(auth.Amount) ?? amount, auth.Amount?.CurrencyCode ?? currency, auth.ExpirationTime);
        }
        catch (PayPalApiException ex)
        {
            throw ToProcessorException(ex, "renew the authorization");
        }
    }

    public async Task<ProcessorCapture> CaptureAsync(string authorizationId, decimal amount, string currency, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var capture = await _client.CaptureAuthorizationAsync(authorizationId, new PayPalCaptureRequest
            {
                Amount = Money(amount, currency),
                InvoiceId = invoiceId,
                FinalCapture = true,
                NoteToPayer = "Thank you for your order."
            }, idempotencyKey, cancellationToken);

            var breakdown = capture.SellerReceivableBreakdown;
            if (breakdown is null)
            {
                // The capture response can omit the fee breakdown; the payments API's
                // show-captured-payment-details endpoint returns the same capture with it.
                try
                {
                    breakdown = (await _client.GetCaptureAsync(capture.Id!, cancellationToken)).SellerReceivableBreakdown;
                }
                catch (PayPalApiException)
                {
                    // The capture itself succeeded; fee/net stay unknown rather than failing fulfilment.
                }
            }
            return new ProcessorCapture(
                capture.Id!,
                capture.Status ?? "UNKNOWN",
                ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(capture.Amount) ?? amount,
                ParseMoney(breakdown?.PaypalFee),
                ParseMoney(breakdown?.NetAmount),
                breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode ?? currency,
                capture.CreateTime ?? DateTimeOffset.UtcNow);
        }
        catch (PayPalApiException ex)
        {
            throw ToProcessorException(ex, "capture the payment");
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.VoidAuthorizationAsync(authorizationId, idempotencyKey, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404 || ex.StatusCode == 422)
        {
            // Already voided / not voidable: treat as released so cancel stays idempotent.
        }
        catch (PayPalApiException ex)
        {
            throw ToProcessorException(ex, "void the authorization");
        }
    }

    public async Task<ProcessorRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string invoiceId,
        string? note, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var refund = await _client.RefundCaptureAsync(captureId, new PayPalRefundRequest
            {
                Amount = amount.HasValue ? Money(amount.Value, currency) : null,
                InvoiceId = invoiceId,
                CustomId = idempotencyKey,
                NoteToPayer = note
            }, idempotencyKey, cancellationToken);

            return new ProcessorRefund(
                refund.Id!,
                refund.Status ?? "UNKNOWN",
                ParseMoney(refund.Amount) ?? amount ?? 0m,
                refund.Amount?.CurrencyCode ?? currency,
                ParseMoney(refund.SellerPayableBreakdown?.TotalRefundedAmount));
        }
        catch (PayPalApiException ex)
        {
            throw ToProcessorException(ex, "refund the payment");
        }
    }

    public async Task<ProcessorVaultedCard> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await _client.CreatePaymentTokenAsync(new PayPalCreatePaymentTokenRequest
            {
                PaymentSource = new PayPalVaultPaymentSource { Card = BuildCardRequest(card) },
                Customer = new PayPalVaultCustomer { Id = ToVaultCustomerId(customerId) }
            }, idempotencyKey, cancellationToken);

            var vaultedCard = token.PaymentSource?.Card;
            return new ProcessorVaultedCard(token.Id!, vaultedCard?.Brand, vaultedCard?.LastDigits,
                vaultedCard?.Expiry, vaultedCard?.Name);
        }
        catch (PayPalApiException ex)
        {
            throw ToProcessorException(ex, "save the card");
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeletePaymentTokenAsync(vaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw ToProcessorException(ex, "delete the saved card");
        }
    }

    public async Task<IReadOnlyList<ProcessorTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var details = await _client.ListAllTransactionsAsync(from, to, cancellationToken);
            return details
                .Where(d => d.TransactionInfo is not null)
                .Select(d => d.TransactionInfo!)
                .Select(info => new ProcessorTransaction(
                    info.TransactionId ?? string.Empty,
                    info.PayPalReferenceId,
                    info.PayPalReferenceIdType,
                    info.TransactionEventCode,
                    info.TransactionStatus,
                    ParseMoney(info.TransactionAmount),
                    info.TransactionAmount?.CurrencyCode,
                    ParseMoney(info.FeeAmount),
                    info.InvoiceId,
                    info.CustomField,
                    info.TransactionInitiationDate,
                    info.TransactionUpdatedDate))
                .ToList();
        }
        catch (PayPalApiException ex)
        {
            throw ToProcessorException(ex, "list transactions");
        }
    }

    private static PayPalAuthorization? ExtractAuthorization(PayPalOrderResponse order)
        => order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? new List<PayPalAuthorization>())
            .FirstOrDefault(a => a.Id is not null);

    private static PayPalPaymentSourceRequest BuildPaymentSource(PaymentSourceSelection source)
        => source switch
        {
            PaymentSourceSelection.OneOffCard oneOff => new PayPalPaymentSourceRequest { Card = BuildCardRequest(oneOff.Card) },
            PaymentSourceSelection.VaultedCardToken vaulted => new PayPalPaymentSourceRequest
            {
                Card = new PayPalCardRequest
                {
                    VaultId = vaulted.VaultTokenId,
                    StoredCredential = new PayPalStoredCredential
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "ONE_TIME",
                        Usage = "SUBSEQUENT"
                    }
                }
            },
            _ => throw new PaymentRequestValidationException($"Unsupported payment source {source.GetType().Name}.")
        };

    private static PayPalCardRequest BuildCardRequest(CardDetails card)
        => new PayPalCardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = card.BillingAddress is null
                ? null
                : new PayPalAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        };

    /// <summary>
    /// The spec constrains merchant_partner_customer_id to ^[0-9a-zA-Z_-]{1,22}$. eShop buyer
    /// ids are emails, so map them deterministically onto a compliant id (SHA-256 prefix).
    /// </summary>
    private static string ToVaultCustomerId(string customerId)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(customerId, "^[0-9a-zA-Z_-]{1,22}$"))
        {
            return customerId;
        }
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(customerId));
        return Convert.ToHexString(hash)[..22];
    }

    private static PayPalMoney Money(decimal amount, string currency)
        => new PayPalMoney
        {
            CurrencyCode = currency,
            Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
        };

    private static decimal? ParseMoney(PayPalMoney? money)
        => money?.Value is null
            ? null
            : decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static PaymentProcessorException ToProcessorException(PayPalApiException ex, string operation)
        => new PaymentProcessorException(
            $"PayPal could not {operation}: {ex.Message}",
            ex.StatusCode,
            ex.ErrorName,
            ex.DebugId);
}
