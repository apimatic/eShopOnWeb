using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's abstraction over the PayPal payment processor. Implemented in Infrastructure
/// against the PayPal .NET SDK. Every type crossing this boundary is a plain domain type, so the
/// application/API layers never depend on the SDK.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Creates a PayPal order with intent=AUTHORIZE and authorizes it with a raw card (a hold is
    /// placed; no money is taken). The <paramref name="referenceId"/> is attached to the payment so
    /// it appears on PayPal's transaction record for reconciliation.
    /// </summary>
    Task<AuthorizeResult> AuthorizeWithCardAsync(PaymentAmount amount, string referenceId, CardPaymentDetails card, CancellationToken cancellationToken = default);

    /// <summary>Authorizes an order using a previously vaulted (saved) card.</summary>
    Task<AuthorizeResult> AuthorizeWithVaultedCardAsync(PaymentAmount amount, string referenceId, string vaultId, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization (status and expiry).</summary>
    Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization, returning the refreshed hold.</summary>
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, PaymentAmount amount, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the hold (used when cancelling before fulfilment).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures an authorization (takes the money) at fulfilment. The result carries what PayPal
    /// reported: the captured amount, PayPal's fee, and the net proceeds to the merchant.
    /// </summary>
    Task<PayPalCapture> CaptureAsync(string authorizationId, PaymentAmount amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a capture in full (<paramref name="amount"/> null) or in part. The
    /// <paramref name="idempotencyKey"/> makes a replayed request a no-op at PayPal.
    /// </summary>
    Task<PayPalRefund> RefundAsync(string captureId, PaymentAmount? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card, returning its vault token and a safe descriptor (brand, last4, expiry).</summary>
    Task<VaultedCard> VaultCardAsync(CardPaymentDetails card, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task RemoveVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions over a date range, following pagination so the
    /// whole range is covered, not just the first page.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
