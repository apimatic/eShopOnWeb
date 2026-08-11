using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The application's sole gateway to PayPal's REST API. Every PayPal interaction goes through this
/// abstraction; the implementation lives in Infrastructure. Idempotency keys map to PayPal's
/// <c>PayPal-Request-Id</c> header so retries never move money twice.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates a PayPal order with intent AUTHORIZE for the given amount. Returns the PayPal order id.</summary>
    Task<string> CreateAuthorizationOrderAsync(decimal amount, string currencyCode, string referenceId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (places a hold for) a PayPal order using raw card details.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(string payPalOrderId, PayPalCardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (places a hold for) a PayPal order using a previously vaulted card.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(string payPalOrderId, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a hold that is nearing or past expiry, returning the new authorization.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) the full authorized amount.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Voids (releases) an authorization so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture in full (<paramref name="amount"/> null) or in part.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vaults a card directly (no browser) under the given merchant customer reference. PayPal returns a
    /// generated customer id on the result.
    /// </summary>
    Task<PayPalVaultedCard> VaultCardAsync(PayPalCardDetails card, string merchantCustomerId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole date range (chunked and fully paged),
    /// for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
