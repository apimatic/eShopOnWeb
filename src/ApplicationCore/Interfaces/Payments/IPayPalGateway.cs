using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstraction over the PayPal REST APIs used by this integration. The implementation builds
/// every request against the PayPal OpenAPI specifications in <c>api-specs/</c> (Checkout
/// Orders v2, Payments v2, Vault v3, Transaction Search v1) and handles OAuth2 token
/// acquisition, currency/amount formatting, idempotency headers and error mapping.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Authorizes (places a hold for) the order total using a one-off card or a vaulted card.
    /// The <paramref name="idempotencyKey"/> is sent as PayPal-Request-Id so a repeated call
    /// does not create a second hold.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes the money for) a previously created authorization at fulfilment.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization so a later capture can succeed.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Voids (releases) an authorization before fulfilment so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a capture in full (<paramref name="amount"/> null) or in part. The
    /// <paramref name="idempotencyKey"/> makes a repeated request return the same refund.
    /// </summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card, returning the token id and a safe description of the card.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns PayPal's own record of transactions across the whole date range (following
    /// pagination), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
