using System;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for the PayPal REST APIs used by this application:
/// Orders v2 (authorize/capture), Payments v2 (capture, reauthorize, void, refund),
/// Payment Method Tokens v3 (saved cards) and Transaction Search v1 (reconciliation).
/// </summary>
public interface IPayPalClient
{
    /// <summary>The currency all payments are processed in (from configuration).</summary>
    string Currency { get; }

    /// <summary>
    /// Creates a PayPal order with intent AUTHORIZE and takes the authorization (hold).
    /// With a card payment source PayPal authorizes during order creation.
    /// Returns the PayPal order id and the authorization it produced.
    /// </summary>
    Task<(string OrderId, PayPalAuthorizationInfo Authorization)> AuthorizeAsync(
        decimal amount, string invoiceId, string customId,
        PayPalCardPayment? card, string? vaultId, Guid requestId);

    /// <summary>Reauthorizes an existing authorization; returns the new authorization.</summary>
    Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, Guid requestId);

    /// <summary>Voids an authorization so the held funds are released.</summary>
    Task VoidAuthorizationAsync(string authorizationId, Guid requestId);

    /// <summary>Shows the current state of an authorization.</summary>
    Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId);

    /// <summary>Captures an authorization. Returns the capture including fee/net breakdown.</summary>
    Task<PayPalCaptureInfo> CaptureAsync(string authorizationId, decimal amount, string invoiceId, Guid requestId);

    /// <summary>Refunds a capture, in full or in part. Idempotent under the same requestId.</summary>
    Task<PayPalRefundInfo> RefundAsync(string captureId, decimal? amount, string invoiceId, Guid requestId);

    /// <summary>Vaults a card and returns the payment token (saved card).</summary>
    Task<PayPalPaymentTokenInfo> CreatePaymentTokenAsync(
        PayPalCardPayment card, string customerId, Guid requestId);

    /// <summary>Deletes a vaulted payment method so it can no longer be charged.</summary>
    Task DeletePaymentTokenAsync(string paymentTokenId);

    /// <summary>Lists PayPal's transaction record for a date range, across all pages.</summary>
    Task<System.Collections.Generic.IReadOnlyList<PayPalTransactionInfo>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to);
}
