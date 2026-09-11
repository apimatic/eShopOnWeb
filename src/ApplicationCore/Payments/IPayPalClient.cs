using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Thin abstraction over the PayPal REST API. The implementation is the only place that knows how to
/// talk to PayPal; all callers work in terms of the domain records above.
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// Creates a PayPal order with intent AUTHORIZE funded by the given source and places a hold on the
    /// funds equal to <paramref name="amount"/>. No money is captured. <paramref name="requestId"/> makes
    /// the call idempotent. <paramref name="invoiceId"/> is stamped on the order for later reconciliation.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string invoiceId,
        PaymentSource source,
        string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures (settles) an authorization, fully or partially. The invoice/custom reference is inherited
    /// from the authorizing order, so it is not re-sent here.
    /// </summary>
    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        decimal? amount,
        string currency,
        string requestId,
        bool finalCapture,
        CancellationToken cancellationToken = default);

    /// <summary>Refreshes a stale authorization so it can be captured. Throws when it can no longer be renewed.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Voids (releases) an authorization so the held funds are returned to the shopper.</summary>
    Task VoidAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, fully (null amount) or partially.</summary>
    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Vaults a card and returns a durable payment token plus a safe description of the card.</summary>
    Task<VaultCardResult> VaultCardAsync(
        CardDetails card,
        string? customerId,
        string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole [from, to] range, transparently
    /// chunking the range into &lt;=31-day windows and paging through every page of each window.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
