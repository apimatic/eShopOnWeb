using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Abstraction over the PayPal REST API for everything this integration needs. The concrete
/// implementation lives in Infrastructure and talks HTTP to PayPal; application services depend only
/// on this contract. All amounts are exact to the cent. Implementations throw
/// <see cref="Exceptions.PaymentChallengeRequiredException"/> when PayPal demands browser approval and
/// <see cref="Exceptions.PaymentGatewayException"/> for other PayPal-side failures.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Create a checkout order with intent=AUTHORIZE and place a hold using a raw card.</summary>
    Task<CardAuthorizationResult> AuthorizeWithCardAsync(Money amount, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Create a checkout order with intent=AUTHORIZE and place a hold using a vaulted card.</summary>
    Task<CardAuthorizationResult> AuthorizeWithVaultedCardAsync(Money amount, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture an authorization (take the money) at fulfilment.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, Money amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Void an authorization (release the hold) when cancelling before fulfilment.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reauthorize a stale authorization, returning a new one. Throws
    /// <see cref="Exceptions.PaymentGatewayException"/> (operator-actionable) when it can no longer be renewed.
    /// </summary>
    Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, Money amount, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture, in full (amount null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, Money? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault a card for later reuse (no purchase), returning the token and safe descriptors.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, string? customerId = null, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List PayPal's own record of transactions across the whole date range (chunked into the
    /// reporting API's allowed windows and fully paginated), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
