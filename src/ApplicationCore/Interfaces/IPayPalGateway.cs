using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single seam through which the application talks to PayPal. Every method maps to a
/// documented PayPal REST call (Orders v2, Payments v2, Vault v3, Reporting).
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Places a hold: creates an AUTHORIZE-intent order for the given card (or saved-card vault id)
    /// and returns the resulting authorization. Throws <see cref="PayPalChallengeRequiredException"/>
    /// if PayPal asks for browser approval.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(
        CreateAuthorizationCommand command, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string amount, string currencyCode, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Renews a stale/expired authorization, returning the new authorization.</summary>
    Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId, string amount, string currencyCode, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture. <paramref name="amount"/> null means refund the full remaining amount.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, string? amount, string currencyCode, string idempotencyKey, string? noteToPayer,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        CardDetails card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's transaction records across the whole range (chunked into ≤31-day windows and
    /// fully paged), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
