using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's abstraction over PayPal. The implementation (in Infrastructure) is the only
/// place the PayPal SDK is referenced; it translates SDK calls, models and errors into the
/// domain-facing types below and into <see cref="Exceptions.PaymentGatewayException"/>.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>The ISO-4217 currency all payments are charged in (from configuration).</summary>
    string Currency { get; }

    /// <summary>
    /// Authorizes (holds) the order total. Creates a PayPal order with intent=AUTHORIZE, then
    /// authorizes it with the supplied payment source (a one-off card, or a saved card's vault id).
    /// The held amount equals the order total to the cent.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken);

    /// <summary>Captures (takes) the held funds at fulfilment, returning PayPal's settlement figures.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Renews a stale authorization, returning the fresh authorization id/status/expiry.</summary>
    Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(string authorizationId, string currency, decimal amount, CancellationToken cancellationToken);

    /// <summary>Voids (releases) the held funds before fulfilment.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>Refunds a captured payment, in full (amount null) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency, string payPalRequestId, CancellationToken cancellationToken);

    /// <summary>Vaults (saves) a card for a shopper. Returns the token id and a safe card description.</summary>
    Task<PayPalVaultedCard> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken cancellationToken);

    /// <summary>Lists all of a PayPal customer's vaulted cards (every page).</summary>
    Task<IReadOnlyList<PayPalVaultedCard>> ListVaultedCardsAsync(string payPalCustomerId, CancellationToken cancellationToken);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists PayPal's own record of transactions over a date range (every 31-day window, every
    /// page). Used to reconcile PayPal against eShop orders.
    /// </summary>
    Task<PayPalReconciliationResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    // --- Re-read helpers used to settle an unknown outcome after a transport failure ---
    Task<PayPalAuthorizationSnapshot?> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult?> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<PayPalOrderSnapshot?> GetOrderAsync(string payPalOrderId, CancellationToken cancellationToken);
}
