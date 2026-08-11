using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The single boundary through which this app talks to PayPal. Everything it exposes maps
/// directly to a PayPal REST call; there is no PayPal behaviour outside this interface.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Places a hold on the order total (creates a PayPal order with intent=AUTHORIZE and
    /// authorizes it with the supplied instrument). Does not capture. The held amount equals
    /// the order total to the cent.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeOrderCommand command, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) the money for an authorization. Marked as the final capture.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale hold that expired before fulfilment, returning the new authorization.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold without taking money (cancel before fulfilment).</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card with PayPal and returns its token id plus a safe descriptor.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole date range (chunking and
    /// paging as required by Transaction Search), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
