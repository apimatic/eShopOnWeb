using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's sole seam onto PayPal. Every method is idempotent in effect through the
/// caller-supplied <c>idempotencyKey</c> (mapped to PayPal's request id), and translates PayPal
/// failures into <see cref="Exceptions.PayPalGatewayException"/> so callers see one failure type.
/// The gateway owns currency (from configuration) and formats amounts to the cent.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Authorizes (holds) the order total on the given card. Returns the PayPal order id and the
    /// created authorization. Throws if PayPal answers with a browser challenge (3DS).
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string orderReference,
        string invoiceId,
        CardPaymentInput card,
        string idempotencyKeyBase,
        CancellationToken ct);

    /// <summary>Current status of an authorization (e.g. CREATED, CAPTURED, EXPIRED, VOIDED).</summary>
    Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renews a stale authorization, returning the (possibly new) authorization reference.</summary>
    Task<PayPalAuthorizationRef> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>Captures (takes) funds against an authorization. Full amount when <paramref name="amount"/> is null.</summary>
    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>Voids an authorization, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>Vaults a card for later reuse and returns a safe description of it.</summary>
    Task<PayPalVaultResult> VaultCardAsync(
        CardPaymentInput card,
        string buyerReference,
        string? existingCustomerId,
        string idempotencyKey,
        CancellationToken ct);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct);

    /// <summary>
    /// PayPal's own record of transactions across the whole date range (all pages), for
    /// reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
