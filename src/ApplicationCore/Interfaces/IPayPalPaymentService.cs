using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Wraps every PayPal interaction the payment flows need. The implementation is the sole place the PayPal
/// SDK is used; callers work only with the domain-shaped models above and a single failure type
/// (<see cref="PayPalPaymentException"/>).
/// </summary>
public interface IPayPalPaymentService
{
    /// <summary>
    /// Authorizes (holds) <paramref name="amount"/> in <paramref name="currencyCode"/> for the order — the
    /// held amount equals the order total to the cent. Does not capture. The <paramref name="idempotencyKey"/>
    /// is sent as PayPal-Request-Id so a double-click never holds funds twice.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currencyCode, string invoiceReference,
        string customId, PaymentSourceInput source, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Captures the authorization (takes the money) at fulfilment. If the authorization has gone stale it is
    /// renewed (reauthorized) first; if it can no longer be renewed, throws
    /// <see cref="AuthorizationNotRenewableException"/>. Returns the captured amount, PayPal's fee, and the
    /// net proceeds, plus the authorization id actually captured (which may be a renewed one).
    /// </summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode,
        DateTimeOffset? authorizationExpiresAt, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Voids the authorization (releases held funds) before fulfilment.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Refunds a captured payment, in full (<paramref name="amount"/> null) or in part. The
    /// <paramref name="idempotencyKey"/> is the caller-supplied key sent as PayPal-Request-Id so a repeat
    /// under the same key does not refund twice.
    /// </summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>Vaults (saves) a card and returns its vault id plus a safe descriptor.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (chunked and fully paged), for
    /// reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
