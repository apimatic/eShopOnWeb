using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The app's window onto PayPal. Every method maps to one PayPal interaction defined by the
/// OpenAPI specs in <c>api-specs/paypal</c>. Implementations must talk to PayPal directly (no
/// third-party SDK) and translate the spec's wire shapes to these domain-friendly results.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Authorizes (holds) <paramref name="amount"/> against a new PayPal checkout order. Pays with
    /// either raw <paramref name="card"/> details (one-off) or a saved <paramref name="vaultTokenId"/>.
    /// <paramref name="idempotencyKey"/> makes the hold safe to retry (a double-click never holds twice).
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(Money amount, CardDetails? card, string? vaultTokenId,
        string invoiceId, string customId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes the money for) an existing authorization at fulfilment.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, Money amount, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a stale authorization so fulfilment can proceed. Throws
    /// <see cref="AuthorizationNotRenewableException"/> when PayPal will no longer reauthorize it.
    /// </summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, Money amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Voids (releases) an authorization on a cancel-before-fulfilment.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a capture, in full (<paramref name="amount"/> null) or in part. The
    /// <paramref name="idempotencyKey"/> guarantees a repeat under the same key never refunds twice.
    /// </summary>
    Task<RefundResult> RefundAsync(string captureId, Money? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Vaults a card, returning its token id and a safe description of it.</summary>
    Task<VaultResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across a date range, following pagination so the
    /// whole range is covered — not just the first page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
