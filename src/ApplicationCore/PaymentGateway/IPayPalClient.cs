using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

/// <summary>
/// Abstraction over the PayPal REST API for the capabilities this integration needs. Concrete
/// implementation lives in the Infrastructure layer. Every money-moving call takes an
/// idempotency key (surfaced to PayPal as the PayPal-Request-Id header) so retries and
/// double-clicks never charge the shopper twice.
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// Creates a PayPal order for the amount and authorizes it with the given card in one step:
    /// places a hold on the money without taking it. Returns the PayPal order id and the
    /// resulting authorization id/status.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="AuthorizeWithCardAsync"/> but pays with a previously vaulted card,
    /// identified by its vault token id.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the current status of an authorization.</summary>
    Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes the money for) an authorization at fulfilment.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Voids (releases) an un-captured authorization when an order is cancelled.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reauthorizes a stale authorization, yielding a new authorization id that later captures must
    /// use. Throws when PayPal will not renew it (e.g. beyond the 29-day authorization window).
    /// </summary>
    Task<AuthorizationDetails> ReauthorizeAuthorizationAsync(string authorizationId, decimal amount,
        string currency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a capture, in full when <paramref name="amount"/> is null, or partially for the given amount.
    /// </summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vaults a card for later reuse without a purchase, returning the vault token and a safe
    /// description of the card. Pass an existing customer id to link the card to a shopper who
    /// already has vaulted cards.
    /// </summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string? customerId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns PayPal's own record of transactions over the whole date range (chunking the range and
    /// paging as needed), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
