using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). Every interaction with PayPal happens through
/// an implementation of this interface. The currency is owned by the gateway (from configuration)
/// so callers only ever supply amounts.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The configured ISO-4217 currency code every operation is denominated in.</summary>
    string CurrencyCode { get; }

    /// <summary>
    /// Place a hold (authorize, do not capture) for <paramref name="amount"/> using a one-off card.
    /// <paramref name="idempotencyKey"/> makes a repeated request a no-op on PayPal's side.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, CardPaymentDetails card, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Place a hold for <paramref name="amount"/> using a previously vaulted card.</summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string vaultId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Capture (take the money) against an authorization. Idempotent on the key.</summary>
    Task<CaptureResult> CaptureAsync(
        string authorizationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Renew a stale authorization for the same amount. Throws
    /// <see cref="Exceptions.ReauthorizationNotAllowedException"/> when it can no longer be renewed.
    /// </summary>
    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, CancellationToken cancellationToken);

    /// <summary>Release a hold before capture (void the authorization).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Refund a capture, in full (<paramref name="amount"/> null) or in part. The idempotency key
    /// is passed to PayPal so a repeat under the same key does not refund twice.
    /// </summary>
    Task<RefundResult> RefundAsync(
        string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Vault (save) a card for later reuse, returning a safe description and vault id.</summary>
    Task<VaultCardResult> VaultCardAsync(CardPaymentDetails card, CancellationToken cancellationToken);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken);

    /// <summary>
    /// PayPal's own record of transactions across the whole date range (walking every page),
    /// for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
