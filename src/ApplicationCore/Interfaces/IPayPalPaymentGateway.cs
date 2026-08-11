using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The seam between the application and PayPal. Everything the app needs from PayPal goes through
/// here; the implementation (Infrastructure) is the only place that talks to the PayPal SDK.
/// Idempotency keys are passed through to PayPal's PayPal-Request-Id so a retried/double-clicked
/// write does not authorize, capture or refund twice.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Place a hold (authorize) for <paramref name="amount"/> using either raw card details
    /// (<paramref name="card"/>) or one of the shopper's vaulted cards (<paramref name="vaultId"/>).
    /// Exactly one of the two must be supplied. Does not capture.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string referenceId,
        RawCard? card,
        string? vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Capture a previously placed hold (take the money) at fulfilment.</summary>
    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string referenceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renew a stale/expired authorization so it can be captured. Throws
    /// <see cref="Exceptions.AuthorizationNotRenewableException"/> when it can no longer be renewed.
    /// </summary>
    Task<PayPalReauthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Void a hold before fulfilment, releasing the shopper's funds.</summary>
    Task VoidAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment, in full (amount null) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a card and return its token id plus a safe description.</summary>
    Task<PayPalVaultResult> VaultCardAsync(
        RawCard card,
        CancellationToken cancellationToken = default);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(
        string tokenId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PayPal's own record of transactions across the whole date range (all pages), for
    /// reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
