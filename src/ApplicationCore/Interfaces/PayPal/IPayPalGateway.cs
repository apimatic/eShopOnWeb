using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Abstraction over the PayPal REST API for every capability this integration needs.
/// The implementation lives in Infrastructure; application services depend on this interface.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Creates a PayPal order with intent AUTHORIZE and processes a raw card, putting a hold on
    /// the money equal to <paramref name="amount"/>. Does not capture.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency, string orderReference, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a PayPal order with intent AUTHORIZE paying with a previously vaulted card.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string currency, string orderReference, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization (used to detect a stale hold).</summary>
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale hold. Throws <see cref="PayPalException"/> if it can no longer be renewed.</summary>
    Task<PayPalReauthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures a hold (takes the money) at fulfilment.</summary>
    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId, decimal amount, string currency, string orderReference,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Releases a hold before fulfilment so no money moves.</summary>
    Task VoidAsync(
        string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full (null amount) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(
        string captureId, decimal? amount, string currency, string? invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a raw card and returns its token plus a safe description.</summary>
    Task<PayPalVaultResult> VaultCardAsync(
        CardDetails card, string? customerId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(
        string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (chunking the
    /// range into windows PayPal allows and paging through every page of each window).
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
