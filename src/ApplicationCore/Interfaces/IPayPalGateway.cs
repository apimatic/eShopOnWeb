using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's boundary to PayPal. Implemented in Infrastructure over the PayPal Server SDK;
/// every method translates SDK models and failures into the application's own types
/// (<see cref="PayPal.PayPalModels"/> records and
/// <see cref="Exceptions.PayPalPaymentException"/>), so nothing above this line depends on the SDK.
///
/// Idempotency keys are passed through to PayPal's <c>PayPal-Request-Id</c> so a resend of the same
/// logical operation does not act twice.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Places a hold on <paramref name="amount"/> for the order (creates a PayPal order with intent
    /// AUTHORIZE, then authorizes it with the card). Does not take the money.
    /// Throws <see cref="Exceptions.PayPalChallengeException"/> if the card needs browser approval.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(
        decimal amount,
        string correlationId,
        CardPaymentInstrument instrument,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>
    /// Ensures the authorization can still be captured; if it has gone stale it is reauthorized,
    /// returning the new authorization id. If it can no longer be renewed, <c>CanCapture</c> is false
    /// with an operator-actionable <c>Reason</c>.
    /// </summary>
    Task<AuthorizationRenewalResult> EnsureCapturableAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>Takes the held money (captures the authorization) at fulfilment.</summary>
    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string correlationId,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>Releases a hold before fulfilment (voids the authorization).</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refunds a captured payment, in full (<paramref name="amount"/> null) or in part.</summary>
    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>Vaults a card for the given PayPal customer and returns a safe description of it.</summary>
    Task<SavedCardResult> VaultCardAsync(
        string merchantCustomerId,
        CardDetails card,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole date range, paging through all
    /// results (not just the first page).
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
