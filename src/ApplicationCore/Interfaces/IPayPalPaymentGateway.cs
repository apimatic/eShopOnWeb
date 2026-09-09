using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST API for the capabilities this integration needs.
/// Implemented in Infrastructure against the PayPal sandbox/live REST endpoints.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Create a PayPal order with intent=AUTHORIZE and place a hold for <paramref name="amount"/>
    /// using raw card details for a one-off payment. Money is held, not taken.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a PayPal order with intent=AUTHORIZE and place a hold using a previously vaulted card.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithVaultAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Read the current state of an authorization (hold).</summary>
    Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization so it can still be captured.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default);

    /// <summary>Release a hold without taking the money (cancel before fulfilment).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Capture (take) the money held by an authorization (at fulfilment).</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a capture, in full (<paramref name="amount"/> null) or in part. The idempotency key
    /// guarantees a repeated request under the same key does not refund twice.
    /// </summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vault (save) a card for later reuse, without taking a payment, and return a reusable
    /// vault id plus a safe descriptor (brand / last four / expiry).
    /// </summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List PayPal's own record of transactions over a date range, paging through the whole range.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
