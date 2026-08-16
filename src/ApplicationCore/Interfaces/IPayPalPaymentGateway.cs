using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of PayPal, in domain terms. The Infrastructure implementation is the only
/// place that talks to the PayPal SDK; everything above this seam works in these plain results so the
/// domain never depends on the SDK. Every write takes a <c>requestId</c> that is passed to PayPal as
/// its idempotency key (PayPal-Request-Id), so a retried call never moves money twice.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Authorizes (holds) <paramref name="amount"/> against a card — either raw <paramref name="card"/>
    /// details for a one-off payment, or a previously vaulted card named by <paramref name="vaultId"/>.
    /// The held amount equals the order total to the cent.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currency, CardDetails? card, string? vaultId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of a hold, so staleness can be detected before capture.</summary>
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale hold that has not yet been captured.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) a previously authorized hold.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Voids a hold, releasing the funds so no money moves.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for reuse and returns a safe description of it.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (paging through all
    /// pages), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
