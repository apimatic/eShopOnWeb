using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for the PayPal Payments API (Orders v2, Payments v2, Vault v3,
/// Transaction Search v1). Implementations must never log full card details.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Create an order with intent=AUTHORIZE. Returns the PayPal order id.</summary>
    Task<string> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorize an order paying with raw card details (one-off payment).</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(string payPalOrderId, PayPalCardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorize an order paying with a vaulted card (saved payment token).</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultedCardAsync(string payPalOrderId, string vaultTokenId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Fetch an order with its payments (authorizations/captures).</summary>
    Task<PayPalOrderDetails> GetOrderAsync(string payPalOrderId, CancellationToken cancellationToken = default);

    /// <summary>Fetch the current state of an authorization.</summary>
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization (extends the honor period).</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Capture an authorized payment (takes the money). The capture inherits the invoice id
    /// from the purchase unit, so it is not passed again here (PayPal rejects reused invoice ids).
    /// </summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Void an authorization, releasing the shopper's held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment, in full or in part.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency,
        string? noteToPayer, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Create a vault setup token for a card. Returns the setup token id.</summary>
    Task<string> CreateSetupTokenAsync(string customerId, PayPalCardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Exchange a setup token for a durable payment token. Returns the payment token id.</summary>
    Task<string> CreatePaymentTokenAsync(string customerId, string setupTokenId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Retrieve a vaulted payment token with safe card display data.</summary>
    Task<PayPalVaultedCard> GetPaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    /// <summary>Delete a vaulted payment token so it can no longer be used.</summary>
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List PayPal's own record of transactions for a date range, covering the whole
    /// range (pages and &gt;31-day windows are handled internally).
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
