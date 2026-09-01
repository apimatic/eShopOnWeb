using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal Payments API (Orders v2, Payments v2, Vault v3,
/// Transaction Search v1). Implementations must never log or persist full card data.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Create an order with intent=AUTHORIZE (no payment source yet).</summary>
    Task<string> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Authorize payment for a previously created order, with either one-off card details
    /// or a vaulted card (vaultTokenId). Returns the resulting authorization (the hold).</summary>
    Task<PayPalAuthorization> AuthorizeOrderAsync(string payPalOrderId,
        PayPalCard? card, string? vaultTokenId, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Capture an authorization (takes the money). Returns gross/fee/net as reported by PayPal.</summary>
    Task<PayPalCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization hold.</summary>
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Void an authorization, releasing the shopper's held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture, in full (amount null) or in part. requestId is the idempotency key.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string? noteToPayer, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Vault a card and return the resulting payment token with safe display data.</summary>
    Task<PayPalVaultToken> CreateCardPaymentTokenAsync(PayPalCard card, string customerId,
        string requestId, CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>List PayPal's own record of transactions over [from, to], covering the whole range
    /// (chunks into supported windows and pages through all results).</summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
