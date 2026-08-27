using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Client for the PayPal Payments API (Orders v2, Payments v2, Vault v3, Transaction Search v1).
/// Implementations must never log or persist full card details.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates a PayPal order with intent AUTHORIZE for the given amount.</summary>
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string customId, string invoiceId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes payment for a PayPal order, either with full card details (one-off)
    /// or with a vaulted card (vaultId). Exactly one of card / vaultId must be supplied.
    /// </summary>
    Task<PayPalAuthorizeResult> AuthorizeOrderAsync(string payPalOrderId, CardPaymentSource? card, string? vaultId, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures an authorization in full (final capture).</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string invoiceId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization hold.</summary>
    Task<PayPalAuthorizationDetails> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the shopper's held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in part (amount supplied) or for the remaining balance (amount null).</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string? noteToPayer, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalSetupTokenResult> CreateSetupTokenAsync(CardPaymentSource card, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string setupTokenId, string merchantCustomerId, string requestId, CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions for a date range, paging through the
    /// whole range (and chunking into the API's maximum window) rather than returning
    /// only the first page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
