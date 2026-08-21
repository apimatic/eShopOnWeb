using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

/// <summary>
/// Abstraction over the PayPal payment processor. Keeps the SDK entirely inside Infrastructure;
/// the application layer works only with these provider-agnostic models. Implementations translate
/// provider failures into <see cref="Exceptions.PaymentGatewayException"/> (and its subtypes).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a PayPal order and authorizes it, placing a hold equal to the amount. Does not capture.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) the funds of a previously placed authorization.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization; throws <see cref="Exceptions.AuthorizationNotRenewableException"/> when it cannot be renewed.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full (<paramref name="amount"/> null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card so it can be reused. Returns the reusable token and a safe description.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, string? payPalCustomerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>Lists PayPal's own transaction records for a date range, covering the whole range (all pages).</summary>
    Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
