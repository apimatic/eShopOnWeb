using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST integration. The application core depends only on this
/// interface; the concrete implementation (in Infrastructure) is the only place that references
/// the PayPal SDK. All money amounts are in the currency configured for the merchant.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Creates a PayPal order with intent=AUTHORIZE and authorizes it against the supplied card
    /// (a one-off, direct card payment). Optionally stores the card in the vault during the payment.
    /// The held amount equals <paramref name="amount"/> to the cent.
    /// </summary>
    Task<CardAuthorizationResult> AuthorizeWithCardAsync(decimal amount, CardDetails card,
        bool storeInVault, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Creates and authorizes a PayPal order paying with a previously vaulted card.</summary>
    Task<CardAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults (saves) a card for later reuse, without taking any payment.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Captures an authorization at fulfilment (takes the money).</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Renews a stale/expired authorization before fulfilment.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization before fulfilment (releases the held funds).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (all pages),
    /// for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
