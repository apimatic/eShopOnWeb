using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level PayPal REST client. Every capability required by the payment flows,
/// verified against the PayPal Orders v2, Payments v2, Vault v3 and Reporting APIs.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Creates a PayPal order with intent AUTHORIZE and processes the card, placing a hold.</summary>
    Task<PayPalAuthorizationResult> CreateOrderAndAuthorizeAsync(string customId, string invoiceId, decimal amount,
        string currency, PayPalCardSource cardSource, string requestId);

    /// <summary>Takes the money: captures an authorized payment.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId);

    /// <summary>Releases the hold on an authorized payment without taking money.</summary>
    Task<PayPalVoidResult> VoidAuthorizationAsync(string authorizationId);

    /// <summary>Renews a stale authorization so it can be captured.</summary>
    Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency);

    /// <summary>Refunds a captured payment, in full (amount null) or in part.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string requestId);

    /// <summary>Vaults a card: creates a setup token from raw card details.</summary>
    Task<PayPalSetupTokenResult> CreateSetupTokenAsync(CardDetails card, string merchantCustomerId, string requestId);

    /// <summary>Upgrades an approved setup token into a permanent payment token.</summary>
    Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string setupTokenId, string requestId);

    /// <summary>Removes a vaulted payment token so it can no longer be used to pay.</summary>
    Task DeletePaymentTokenAsync(string paymentTokenId);

    /// <summary>Lists PayPal's own record of transactions over a range (whole range, all pages).</summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to);
}